#region Using Directives

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Preserves captured sheets before retiring a stale confirmed transfer. Never rolls back a newer cloud catalog.</summary>
public static class BookSyncRecovery
{
	#region Public Methods

	public static async Task<int> RefreshReviewAsync(
		IBookStore store,
		BookLocation location,
		ICloudReplica replica,
		ReplicaStateLease lease,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(replica);
		ArgumentNullException.ThrowIfNull(lease);
		if (!lease.Key.Equals(new CloudReplicaKey(lease.Key.BookId, lease.Key.DeviceId, replica.Identity)))
		{
			throw new ArgumentException("The recovery belongs to another cloud replica.", nameof(replica));
		}

		SyncTransferJournal journal = new(lease);
		SyncTransferDocument? pending = await journal.LoadAsync(cancellationToken).ConfigureAwait(false);
		int count = 0;
		if (pending is not null)
		{
			string original = await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
			ChordDatabase current = DatabaseJson.Deserialize(original);
			if (current.Id != pending.BookId)
			{
				throw new SyncMergeException("The pending transfer belongs to another local book.");
			}

			ChordDatabase captured = DatabaseJson.Deserialize(pending.TargetJson);
			Dictionary<Guid, SongFile> capturedFiles = captured.SongFiles.ToDictionary(file => file.Id);
			Dictionary<Guid, Song> capturedSongs = captured.Songs.ToDictionary(song => song.Id);
			Dictionary<Guid, Song> currentSongs = current.Songs.ToDictionary(song => song.Id);
			Dictionary<Guid, SongFile> currentFiles = current.SongFiles.ToDictionary(file => file.Id);
			Dictionary<Guid, SyncTransferAsset> sources = [];
			DateTimeOffset now = DateTimeOffset.UtcNow;
			foreach (SyncTransferAsset asset in pending.Assets.Where(asset => asset.Stage))
			{
				cancellationToken.ThrowIfCancellationRequested();
				string path = journal.AssetPath(pending, asset.FileId);
				if (!File.Exists(path))
				{
					await SyncAssetStager.StageAsync(store, location, replica, asset, path, cancellationToken).ConfigureAwait(false);
				}

				Guid recoveryId = RecoveryId(pending.Id, asset.FileId);
				if (!currentFiles.ContainsKey(recoveryId))
				{
					SongFile recovery = SyncEntityFingerprint.Clone(capturedFiles[asset.FileId]);
					if (!currentSongs.TryGetValue(recovery.SongId, out Song? owner))
					{
						Song song = SyncEntityFingerprint.Clone(capturedSongs[recovery.SongId]);
						song.Id = RecoveryId(pending.Id, song.Id);
						song.Title = "Recovered " + song.Title;
						song.IsArchived = true;
						song.Revision = RevisionStamp.Initial(lease.Key.DeviceId, now);
						if (currentSongs.TryAdd(song.Id, song))
						{
							current.Songs.Add(song);
						}

						recovery.SongId = song.Id;
						owner = song;
					}

					recovery.Id = recoveryId;
					recovery.RelativePath = PortableManagedFileName.Create(
						"Recovered " + owner.Title, recoveryId, Path.GetExtension(asset.Name));
					recovery.IsArchived = true;
					recovery.DisplayPriority = int.MaxValue;
					recovery.RecoveryVersion = new()
					{
						OriginalConflictingFileId = asset.FileId,
						WinningFileId = asset.FileId,
						RecoveredFromSyncUtc = now,
					};
					recovery.Revision = RevisionStamp.Initial(lease.Key.DeviceId, now);
					current.SongFiles.Add(recovery);
					currentFiles.Add(recoveryId, recovery);
					sources.Add(recoveryId, asset);
					count++;
				}
			}

			if (count > 0)
			{
				current.Revision = new()
				{
					Revision = current.Revision.Revision + 1, DeviceId = lease.Key.DeviceId, ModifiedUtc = now,
				};
				await LocalBookSyncApplier.ApplyAsync(
					store,
					location,
					original,
					DatabaseJson.Serialize(current),
					(file, token) => OpenCapturedAsync(journal, pending, sources[file.Id], token),
					Path.Combine(lease.DirectoryPath, "temporary"),
					cancellationToken).ConfigureAwait(false);
			}

			// Keep the bound catalogs, receipts and captured bytes for diagnostics even after the recovery sheets are installed.
			await journal.RetireAsync(pending, cancellationToken).ConfigureAwait(false);
		}

		return count;
	}

	#endregion

	#region Private Methods

	private static Guid RecoveryId(Guid transferId, Guid sourceId)
	{
		const int GuidByteCount = 16;
		byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"retired-transfer:{transferId:D}:{sourceId:D}"));
		return new(bytes.AsSpan(0, GuidByteCount));
	}

	private static Task<Stream> OpenCapturedAsync(
		SyncTransferJournal journal, SyncTransferDocument document, SyncTransferAsset asset, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Stream result = new FileStream(journal.AssetPath(document, asset.FileId), FileMode.Open, FileAccess.Read, FileShare.Read);
		return Task.FromResult(result);
	}

	#endregion
}
