#region Using Directives

using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Reads a bounded cloud catalog without opening sheet assets or signing in.</summary>
public static class CloudBookReader
{
	#region Public Methods

	public static async Task<CloudBookSnapshot?> ReadAsync(ICloudReplica replica, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(replica);
		if (!replica.IsAuthenticated)
		{
			throw new InvalidOperationException("Connect the selected cloud account before reading books.");
		}

		CloudChangeSet listing = await replica.ListOrGetChangesAsync(null, cancellationToken).ConfigureAwait(false);
		Dictionary<string, CloudReplicaItem> items = IndexItems(listing);
		CloudBookSnapshot? result = null;
		if (items.TryGetValue("database.json", out CloudReplicaItem? databaseItem))
		{
			string json = await ReadJsonAsync(replica, databaseItem, cancellationToken).ConfigureAwait(false);
			ChordDatabase database = DatabaseJson.Deserialize(json);
			Dictionary<Guid, CloudReplicaItem> managed = [];
			foreach (SongFile file in database.SongFiles)
			{
				if (!items.TryGetValue(file.RelativePath.Normalize(NormalizationForm.FormC), out CloudReplicaItem? item))
				{
					throw new SyncMergeException($"The cloud book is missing a referenced sheet: {file.RelativePath}. Neither copy was changed.");
				}

				managed.Add(file.Id, item);
			}

			result = new(
				json,
				databaseItem,
				new ReadOnlyDictionary<Guid, CloudReplicaItem>(managed),
				new ReadOnlyDictionary<string, CloudReplicaItem>(items),
				database);
		}

		return result;
	}

	#endregion

	#region Internal Methods

	internal static Dictionary<string, CloudReplicaItem> IndexItems(CloudChangeSet listing)
	{
		Dictionary<string, CloudReplicaItem> items = new(PortableManagedFileName.Comparer);
		foreach (CloudReplicaItem item in listing.Items)
		{
			if (!items.TryAdd(item.Name.Normalize(NormalizationForm.FormC), item))
			{
				throw new SyncMergeException("The cloud folder contains filenames that collide on another device. Resolve them before syncing.");
			}
		}

		return items;
	}

	internal static async Task<string> ReadJsonAsync(ICloudReplica replica, CloudReplicaItem item, CancellationToken cancellationToken)
	{
		const int MaximumCharacters = 32 * 1024 * 1024;
		await using Stream stream = await replica.DownloadAsync(item.Id, cancellationToken).ConfigureAwait(false);
		using StreamReader reader = new(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
		StringBuilder text = new();
		const int BufferSize = 8192;
		char[] buffer = new char[BufferSize];
		int count;
		while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
		{
			if (text.Length > MaximumCharacters - count)
			{
				throw new SyncMergeException("The cloud database exceeds the 32-million-character comparison limit.");
			}

			text.Append(buffer, 0, count);
		}

		return text.ToString();
	}

	#endregion
}
