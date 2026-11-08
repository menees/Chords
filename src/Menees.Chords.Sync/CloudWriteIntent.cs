#region Using Directives

using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Records the candidate sheet bytes before in-place writes and reconciles only uploads that landed.</summary>
internal static class CloudWriteIntent
{
	#region Private Data

	private const string IntentName = "chordbook-sync-intent.json";

	#endregion

	#region Public Methods

	public static async Task RecordAsync(
		ICloudReplica replica, SyncTransferDocument transfer, CloudBookSnapshot? before, CancellationToken cancellationToken)
	{
		ChordDatabase target = transfer.GetTargetDatabase();
		HashSet<string> retained = new(target.SongFiles.Select(file => file.RelativePath), PortableManagedFileName.Comparer);
		SyncItemReceipt[] obsolete = before is null ? [] : [.. before.ManagedItems.Values
			.Where(item => !retained.Contains(item.Name)).Select(SyncItemReceipt.From)];
		if (obsolete.Length == 0 && !transfer.Assets.Any(asset => asset.Upload))
		{
			return;
		}

		var existing = await FindAsync(replica, cancellationToken).ConfigureAwait(false);
		if (existing is null)
		{
			Dictionary<Guid, SongFile> files = target.SongFiles.ToDictionary(file => file.Id);
			Dictionary<Guid, Song> songs = target.Songs.ToDictionary(song => song.Id);
			Upload[] uploads = [.. transfer.Assets.Where(asset => asset.Upload).Select(asset =>
			{
				SongFile file = files[asset.FileId];
				return new Upload(file, songs[file.SongId]);
			})];
			Intent intent = new(transfer.Id, transfer.BookId, target.Name, transfer.CloudCatalog, uploads, obsolete);
			using MemoryStream content = new(JsonSerializer.SerializeToUtf8Bytes(intent), writable: false);
			await replica.CreateAsync(IntentName, content, cancellationToken).ConfigureAwait(false);
		}
		else if (existing.Value.Intent.TransferId != transfer.Id)
		{
			throw new SyncMergeException("An interrupted cloud write must be reconciled before publishing another transfer.");
		}
	}

	public static async Task RecoverIfNeededAsync(
		IBookStore store,
		BookLocation location,
		CloudBookLease replica,
		SyncTransferDocument? pending,
		SyncDirection direction,
		Guid recoveryEpoch,
		IProgress<string>? progress,
		CancellationToken cancellationToken)
	{
		var existing = await FindAsync(replica, cancellationToken).ConfigureAwait(false);
		if (existing is { } captured)
		{
			string localJson = await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
			CloudBookSnapshot? snapshot = await CloudBookReader.ReadAsync(replica, cancellationToken).ConfigureAwait(false);
			bool resume = pending is not null && pending.CloudLeaseToken == replica.Token && pending.Id == captured.Intent.TransferId
				&& pending.Direction == direction && pending.RecoveryEpoch == recoveryEpoch
				&& (localJson == pending.LocalJson || localJson == pending.TargetJson)
				&& (Matches(captured.Intent, snapshot) || snapshot?.DatabaseJson == pending.TargetJson);
			if (!resume)
			{
				await ReconcileAsync(replica, captured, snapshot, DatabaseJson.Deserialize(localJson).Id, progress, cancellationToken)
					.ConfigureAwait(false);
			}
		}
	}

	public static async Task RecoverDownloadAsync(
		CloudBookLease replica, Guid? expectedBookId, IProgress<string>? progress, CancellationToken cancellationToken)
	{
		var existing = await FindAsync(replica, cancellationToken).ConfigureAwait(false);
		if (existing is { } captured)
		{
			CloudBookSnapshot? snapshot = await CloudBookReader.ReadAsync(replica, cancellationToken).ConfigureAwait(false);
			await ReconcileAsync(replica, captured, snapshot, expectedBookId, progress, cancellationToken).ConfigureAwait(false);
		}
	}

	public static async Task CleanupAsync(
		ICloudReplica replica, ChordDatabase? previous, ChordDatabase target, IProgress<string>? progress, CancellationToken cancellationToken)
	{
		CloudBookSnapshot snapshot = await CloudBookReader.ReadAsync(replica, cancellationToken).ConfigureAwait(false)
			?? throw new SyncMergeException("The cloud catalog disappeared after publication.");
		if (DatabaseJson.Serialize(snapshot.Database) != DatabaseJson.Serialize(target))
		{
			throw new SyncMergeException("The cloud catalog changed after publication. Cleanup was deferred.");
		}

		HashSet<string> retained = new(target.SongFiles.Select(file => file.RelativePath), PortableManagedFileName.Comparer);
		HashSet<string> obsolete = new(previous?.SongFiles.Select(file => file.RelativePath) ?? [], PortableManagedFileName.Comparer);
		HashSet<Guid> known = [.. target.SongFiles.Select(file => file.Id), .. target.Tombstones
			.Where(item => item.EntityType == nameof(SongFile)).Select(item => item.EntityId)];
		List<CloudReplicaItem> removals = [];
		foreach (CloudReplicaItem item in snapshot.FolderItems.Values)
		{
			bool legacy = PortableManagedFileName.TryGetSongFileId(item.Name, out Guid id) && known.Contains(id)
				&& PortableManagedFileName.WithoutLegacySyncToken(item.Name, id) != item.Name;
			if (!retained.Contains(item.Name) && (obsolete.Contains(item.Name) || legacy))
			{
				removals.Add(item);
			}
		}

		await RemoveSheetsAsync(replica, removals, progress, cancellationToken).ConfigureAwait(false);
		if (snapshot.FolderItems.TryGetValue(IntentName, out CloudReplicaItem? intent))
		{
			await DeleteAsync(replica, intent, cancellationToken).ConfigureAwait(false);
		}
	}

	#endregion

	#region Private Methods

	private static bool Matches(Intent intent, CloudBookSnapshot? snapshot)
		=> intent.Baseline is null ? snapshot is null
			: snapshot is not null && snapshot.DatabaseItem.Id.Value == intent.Baseline.Id
				&& snapshot.DatabaseItem.Version.Value == intent.Baseline.Version;

	private static async Task<(CloudReplicaItem Item, Intent Intent)?> FindAsync(ICloudReplica replica, CancellationToken cancellationToken)
	{
		var listing = await replica.ListOrGetChangesAsync(null, cancellationToken).ConfigureAwait(false);
		CloudReplicaItem? item = CloudBookReader.IndexItems(listing).GetValueOrDefault(IntentName);
		(CloudReplicaItem, Intent)? result = null;
		if (item is not null)
		{
			string json = await CloudBookReader.ReadJsonAsync(replica, item, cancellationToken).ConfigureAwait(false);
			Intent intent = JsonSerializer.Deserialize<Intent>(json) ?? throw new InvalidDataException("The interrupted cloud write record is invalid.");
			if (intent.BookId == Guid.Empty || intent.TransferId == Guid.Empty || intent.Uploads is null
				|| intent.Uploads.Any(upload => upload?.File is null || upload.Song is null || upload.Song.Id != upload.File.SongId
					|| PortableManagedFileName.Validate(upload.File.RelativePath).Count > 0))
			{
				throw new InvalidDataException("The interrupted cloud write record is invalid.");
			}

			result = (item, intent);
		}

		return result;
	}

	private static async Task ReconcileAsync(
		CloudBookLease replica,
		(CloudReplicaItem Item, Intent Intent) captured,
		CloudBookSnapshot? snapshot,
		Guid? expectedBookId,
		IProgress<string>? progress,
		CancellationToken cancellationToken)
	{
		Intent intent = captured.Intent;
		ChordDatabase book = snapshot?.Database ?? new() { Id = intent.BookId, Name = intent.BookName };
		if (book.Id != intent.BookId || (expectedBookId.HasValue && expectedBookId != intent.BookId))
		{
			throw new SyncMergeException("The interrupted cloud write belongs to another book.");
		}

		bool originalCatalog = Matches(intent, snapshot);
		Dictionary<Guid, SongFile> files = book.SongFiles.ToDictionary(file => file.Id);
		HashSet<Guid> songs = [.. book.Songs.Select(song => song.Id)];
		var items = snapshot?.FolderItems
			?? CloudBookReader.IndexItems(await replica.ListOrGetChangesAsync(null, cancellationToken).ConfigureAwait(false));
		bool changed = false;
		foreach (Upload upload in intent.Uploads)
		{
			SongFile candidate = upload.File;
			files.TryGetValue(candidate.Id, out SongFile? current);
			string name = originalCatalog ? candidate.RelativePath : current?.RelativePath ?? candidate.RelativePath;
			if (!items.TryGetValue(name, out CloudReplicaItem? item) || (!originalCatalog && current is null))
			{
				continue;
			}

			await using Stream input = await replica.DownloadAsync(item.Id, cancellationToken).ConfigureAwait(false);
			string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
			if (SameHash(hash, current?.ContentHash) && PortableManagedFileName.Comparer.Equals(name, current?.RelativePath))
			{
				continue;
			}

			if (!originalCatalog || !SameHash(hash, candidate.ContentHash))
			{
				throw new SyncMergeException($"The interrupted upload of {name} has unexpected bytes. The cloud sync lock was retained.");
			}

			if (songs.Add(candidate.SongId))
			{
				book.Songs.Add(upload.Song);
			}

			if (current is not null)
			{
				book.SongFiles.Remove(current);
			}

			candidate.ObservedLength = item.Length;
			book.SongFiles.Add(candidate);
			changed = true;
		}

		if (changed)
		{
			book.Revision = new()
			{
				Revision = checked(book.Revision.Revision + 1),
				DeviceId = replica.DeviceId,
				ModifiedUtc = DateTimeOffset.UtcNow,
			};
			using MemoryStream content = new(System.Text.Encoding.UTF8.GetBytes(DatabaseJson.Serialize(book)), writable: false);
			if (snapshot is null)
			{
				await replica.CreateAsync("database.json", content, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				await replica.ReplaceAsync(snapshot.DatabaseItem.Id, snapshot.DatabaseItem.Version, content, cancellationToken)
					.ConfigureAwait(false);
			}
		}

		if (intent.Obsolete is { Length: > 0 })
		{
			CloudBookSnapshot? published = await CloudBookReader.ReadAsync(replica, cancellationToken).ConfigureAwait(false);
			if (published is not null)
			{
				HashSet<string> retained = new(published.Database.SongFiles.Select(file => file.RelativePath), PortableManagedFileName.Comparer);
				List<CloudReplicaItem> removals = [];
				foreach (SyncItemReceipt old in intent.Obsolete)
				{
					if (!retained.Contains(old.Name) && published.FolderItems.TryGetValue(old.Name, out CloudReplicaItem? item)
						&& item.Id.Value == old.Id && item.Version.Value == old.Version)
					{
						removals.Add(item);
					}
				}

				await RemoveSheetsAsync(replica, removals, progress, cancellationToken).ConfigureAwait(false);
			}
		}

		await DeleteAsync(replica, captured.Item, cancellationToken).ConfigureAwait(false);
	}

	private static bool SameHash(string hash, string? expected) => StringComparer.OrdinalIgnoreCase.Equals(hash, expected);

	private static async Task RemoveSheetsAsync(
		ICloudReplica replica, List<CloudReplicaItem> removals, IProgress<string>? progress, CancellationToken cancellationToken)
	{
		int total = removals.Count;
		if (total > 0)
		{
			int completed = 0;
			void Report() => BookSyncProgressAdapter.Report(
				progress, new($"Removing obsolete cloud sheets: {completed:N0} of {total:N0} files removed.", completed, total));

			Report();
			foreach (CloudReplicaItem item in removals)
			{
				await DeleteAsync(replica, item, cancellationToken).ConfigureAwait(false);
				completed++;
				Report();
			}
		}
	}

	private static async Task DeleteAsync(ICloudReplica replica, CloudReplicaItem item, CancellationToken cancellationToken)
	{
		try
		{
			await replica.DeleteAsync(item.Id, item.Version, cancellationToken).ConfigureAwait(false);
		}
		catch (CloudRequestException exception) when (exception.StatusCode == (int)HttpStatusCode.NotFound)
		{
			// Cleanup may be retried after a successful deletion whose response was lost.
		}
	}

	#endregion

	#region Private Types

	private sealed record Intent(Guid TransferId, Guid BookId, string BookName, SyncItemReceipt? Baseline, Upload[] Uploads, SyncItemReceipt[]? Obsolete);

	private sealed record Upload(SongFile File, Song Song);
	#endregion
}
