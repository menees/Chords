#region Using Directives

using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Creates a local replica, preserving the cloud book identity and exact sheet bytes.</summary>
public static class CloudBookDownloader
{
	#region Private Constants

	private const int MaximumTransferConcurrency = 16;

	#endregion

	#region Public Methods

	public static Task<BookLocation> DownloadAsync(
		IBookStore store,
		ICloudReplica replica,
		Guid deviceId,
		ReplicaStateStore states,
		string stagingDirectory,
		IProgress<string>? progress = null,
		Guid? expectedBookId = null,
		CancellationToken cancellationToken = default)
		=> DownloadCoreAsync(store, replica, deviceId, states, stagingDirectory, progress, expectedBookId, 1, null, null, cancellationToken);

	/// <summary>Downloads with counted progress and bounded transfers, optionally adopting an unchanged empty filesystem book.</summary>
	public static Task<BookLocation> DownloadWithProgressAsync(
		IBookStore store,
		ICloudReplica replica,
		Guid deviceId,
		ReplicaStateStore states,
		string stagingDirectory,
		IProgress<BookSyncProgress> progress,
		Guid? expectedBookId = null,
		int maximumConcurrentTransfers = 1,
		BookLocation? emptyBook = null,
		string? expectedEmptyJson = null,
		CancellationToken cancellationToken = default)
		=> DownloadCoreAsync(
			store,
			replica,
			deviceId,
			states,
			stagingDirectory,
			new BookSyncProgressAdapter(progress),
			expectedBookId,
			maximumConcurrentTransfers,
			emptyBook,
			expectedEmptyJson,
			cancellationToken);

	#endregion

	#region Private Methods

	private static async Task<BookLocation> DownloadCoreAsync(
		IBookStore store,
		ICloudReplica replica,
		Guid deviceId,
		ReplicaStateStore states,
		string stagingDirectory,
		IProgress<string>? progress,
		Guid? expectedBookId,
		int concurrency,
		BookLocation? emptyBook,
		string? expectedEmptyJson,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(replica);
		ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
		ArgumentNullException.ThrowIfNull(states);
		ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(concurrency, MaximumTransferConcurrency);
		progress?.Report("Reading the cloud book…");
		CloudBookSnapshot snapshot = await CloudBookReader.ReadAsync(replica, cancellationToken).ConfigureAwait(false)
			?? throw new SyncMergeException("This cloud book has not been uploaded yet.");
		ChordDatabase database = snapshot.Database;
		if (expectedBookId.HasValue && database.Id != expectedBookId.Value)
		{
			throw new SyncMergeException("The selected cloud book identity changed. Select the book again before downloading.");
		}

		using ReplicaStateLease lease = states.Acquire(new(database.Id, deviceId, replica.Identity));
		CloudReplicaState previous = await lease.LoadStateAsync(cancellationToken).ConfigureAwait(false);
		if (previous.PendingJournal is not null || await new SyncTransferJournal(lease).LoadAsync(cancellationToken).ConfigureAwait(false) is not null)
		{
			throw new SyncMergeException("This device has a pending transfer for that book. Open its local copy and recover or resume it first.");
		}

		BookLocation location;
		if (emptyBook is not null)
		{
			if (store is not FileSystemBookStore fileStore || expectedEmptyJson is null)
			{
				throw new ArgumentException("Empty-book adoption requires a filesystem book and its exact catalog baseline.", nameof(emptyBook));
			}

			if (!EmptyBookAdoption.IsEligible(DatabaseJson.Deserialize(expectedEmptyJson)))
			{
				throw new SyncMergeException("The current book is no longer an empty placeholder.");
			}

			await using VerifiedCloudDownloads downloads = new(replica, snapshot, stagingDirectory, progress, concurrency, cancellationToken);
			location = await fileStore.PopulateEmptyReplicaAsync(
				emptyBook, expectedEmptyJson, snapshot.DatabaseJson, downloads.OpenAsync, cancellationToken).ConfigureAwait(false);
		}
		else
		{
			location = await store.CreateReplicaBookAsync(database.Name, database.Id, deviceId, cancellationToken).ConfigureAwait(false);
			try
			{
				string emptyJson = await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
				await using VerifiedCloudDownloads downloads = new(replica, snapshot, stagingDirectory, progress, concurrency, cancellationToken);
				await LocalBookSyncApplier.ApplyVerifiedAsync(
					store, location, emptyJson, snapshot.DatabaseJson, downloads.OpenAsync, stagingDirectory, cancellationToken).ConfigureAwait(false);
			}
#pragma warning disable CA1031 // Cleanup only a newly created replica, never the user's existing empty book.
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
		}

		progress?.Report("Saving cloud sync state…");
		await lease.SaveStateAsync(
			new(lease.Key)
			{
				LastSuccessfulSyncUtc = DateTimeOffset.UtcNow,
				LastLocalRevision = database.Revision.Revision.ToString(),
				MergeBase = JsonSerializer.Serialize(BookSyncMerger.CreateBase(database)),
			},
			CancellationToken.None).ConfigureAwait(false);
		progress?.Report("Book download completed.");
		return location;
	}

	#endregion
}