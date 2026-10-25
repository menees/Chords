#region Using Directives

using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Reads only cloud catalog metadata. Never authenticates interactively or reads/writes song assets.</summary>
/// <remarks>Application clients should use <see cref="BookSyncCoordinator"/> to review and execute a durable sync.</remarks>
public static class CloudBookComparisonService
{
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
		CloudBookSnapshot snapshot = await CloudBookReader.ReadAsync(replica, cancellationToken).ConfigureAwait(false)
			?? throw new SyncMergeException("The selected cloud folder has no ChordBook database. Initialize a new replica before comparing it.");
		ChordDatabase cloud = snapshot.Database;
		BookMergeResult merge = BookSyncMerger.Merge(local, cloud, baseline, direction, deviceId, now);
		return new(replica.Identity, snapshot.DatabaseItem, cloud, merge, snapshot.ManagedItems);
	}

	#endregion
}
