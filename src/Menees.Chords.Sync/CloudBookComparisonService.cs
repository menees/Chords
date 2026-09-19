#region Using Directives

using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Reads only cloud catalog metadata. Never authenticates interactively or reads/writes song assets.</summary>
public static class CloudBookComparisonService
{
	#region Private Data

	private const int MaximumDatabaseCharacters = 32 * 1024 * 1024;
	private const int ReadBufferSize = 8192;

	#endregion

	#region Public Methods

	public static async Task<CloudBookComparison> CompareAsync(
		ChordDatabase local,
		ICloudReplica replica,
		BookMergeBase? baseline,
		SyncDirection direction,
		Guid deviceId,
		DateTimeOffset now,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(local);
		ArgumentNullException.ThrowIfNull(replica);
		DatabaseValidation.ThrowIfInvalid(local);
		cancellationToken.ThrowIfCancellationRequested();
		if (!replica.IsAuthenticated)
		{
			throw new InvalidOperationException("Connect the selected cloud account before comparing books.");
		}

		// Always request a complete listing. Delta tokens require a separate durable, deletion-aware inventory.
		CloudChangeSet listing = await replica.ListOrGetChangesAsync(null, cancellationToken).ConfigureAwait(false);
		Dictionary<string, CloudReplicaItem> items = new(PortableManagedFileName.Comparer);
		foreach (CloudReplicaItem item in listing.Items)
		{
			string name = item.Name.Normalize(NormalizationForm.FormC);
			if (!items.TryAdd(name, item))
			{
				throw new SyncMergeException("The cloud folder contains filenames that collide on another device. Resolve them before syncing.");
			}
		}

		if (!items.TryGetValue("database.json", out CloudReplicaItem? databaseItem))
		{
			throw new SyncMergeException("The selected cloud folder has no ChordBook database. Initialize a new replica before comparing it.");
		}

		ChordDatabase cloud;
		using (Stream stream = await replica.DownloadAsync(databaseItem.Id, cancellationToken).ConfigureAwait(false))
		{
			cloud = DatabaseJson.Deserialize(await ReadDatabaseAsync(stream, cancellationToken).ConfigureAwait(false));
		}

		Dictionary<Guid, CloudReplicaItem> managed = [];
		foreach (SongFile file in cloud.SongFiles)
		{
			if (!items.TryGetValue(file.RelativePath.Normalize(NormalizationForm.FormC), out CloudReplicaItem? item))
			{
				throw new SyncMergeException($"The cloud book is missing a referenced sheet: {file.RelativePath}. Neither copy was changed.");
			}

			managed.Add(file.Id, item);
		}

		cancellationToken.ThrowIfCancellationRequested();
		BookMergeResult merge = BookSyncMerger.Merge(local, cloud, baseline, direction, deviceId, now);
		return new(replica.Identity, databaseItem, cloud, merge, new ReadOnlyDictionary<Guid, CloudReplicaItem>(managed));
	}

	#endregion

	#region Private Methods

	private static async Task<string> ReadDatabaseAsync(Stream stream, CancellationToken cancellationToken)
	{
		using StreamReader reader = new(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
		StringBuilder text = new();
		char[] buffer = new char[ReadBufferSize];
		int count;
		while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
		{
			if (text.Length > MaximumDatabaseCharacters - count)
			{
				throw new SyncMergeException("The cloud database exceeds the 32-million-character comparison limit.");
			}

			text.Append(buffer, 0, count);
		}

		return text.ToString();
	}

	#endregion
}
