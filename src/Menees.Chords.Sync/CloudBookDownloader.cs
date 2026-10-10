#region Using Directives

using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Creates an independent local replica, preserving the cloud book identity and exact sheet bytes.</summary>
public static class CloudBookDownloader
{
	#region Public Methods

	public static async Task<BookLocation> DownloadAsync(
		IBookStore store,
		ICloudReplica replica,
		Guid deviceId,
		ReplicaStateStore states,
		string stagingDirectory,
		IProgress<string>? progress = null,
		Guid? expectedBookId = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(states);
		CloudBookSnapshot snapshot = await CloudBookReader.ReadAsync(replica, cancellationToken).ConfigureAwait(false)
			?? throw new SyncMergeException("This cloud book has not been uploaded yet.");
		ChordDatabase database = snapshot.Database;
		if (expectedBookId.HasValue && database.Id != expectedBookId.Value)
		{
			throw new SyncMergeException("The selected cloud book identity changed. Select the book again before downloading.");
		}

		using ReplicaStateLease lease = states.Acquire(new(database.Id, deviceId, replica.Identity));
		CloudReplicaState previous = await lease.LoadStateAsync(cancellationToken).ConfigureAwait(false);
		SyncTransferJournal journal = new(lease);
		if (previous.PendingJournal is not null || await journal.LoadAsync(cancellationToken).ConfigureAwait(false) is not null)
		{
			throw new SyncMergeException("This device has a pending transfer for that book. Open its local copy and recover or resume it first.");
		}

		BookLocation location = await store.CreateReplicaBookAsync(database.Name, database.Id, deviceId, cancellationToken).ConfigureAwait(false);
		try
		{
			string emptyJson = await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
			progress?.Report("Downloading and verifying the book’s sheets…");
			await LocalBookSyncApplier.ApplyAsync(
				store,
				location,
				emptyJson,
				snapshot.DatabaseJson,
				(file, token) => replica.DownloadAsync(snapshot.ManagedItems[file.Id].Id, token),
				stagingDirectory,
				cancellationToken).ConfigureAwait(false);
			CloudReplicaState state = new(lease.Key)
			{
				LastSuccessfulSyncUtc = DateTimeOffset.UtcNow,
				LastLocalRevision = database.Revision.Revision.ToString(),
				MergeBase = JsonSerializer.Serialize(BookSyncMerger.CreateBase(database)),
			};
			await lease.SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
		}
#pragma warning disable CA1031 // Preserve the original download failure if cleanup of this newly created replica also fails.
		catch
		{
			try
			{
				await store.DeleteBookAsync(location, CancellationToken.None).ConfigureAwait(false);
			}
			catch
			{
			}

			throw;
		}
#pragma warning restore CA1031

		return location;
	}

	#endregion
}
