namespace Menees.Chords.Sync;

public sealed class CloudReplicaState
{
	public CloudReplicaState(CloudReplicaKey key) => this.Key = key;

	public CloudReplicaKey Key { get; }

	public DateTimeOffset? LastSuccessfulSyncUtc { get; set; }

	public string? LastLocalRevision { get; set; }

	public string? ChangeToken { get; set; }

	public string? MergeBase { get; set; }

	public IList<TrackedProviderItem> TrackedItems { get; } = [];

	public SyncOperationJournal? PendingJournal { get; set; }
}
