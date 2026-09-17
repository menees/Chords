namespace Menees.Chords.Sync;

public sealed class CloudReplicaState
{
	public CloudReplicaState(CloudReplicaKey key) => this.Key = key;

	public CloudReplicaKey Key { get; }

	public DateTimeOffset? LastSuccessfulSyncUtc { get; set; }

	/// <summary>Gets or sets the recovery epoch acknowledged by the last successful full comparison.</summary>
	public Guid RecoveryEpoch { get; set; }

	public string? LastLocalRevision { get; set; }

	public string? ChangeToken { get; set; }

	public string? MergeBase { get; set; }

	public IList<TrackedProviderItem> TrackedItems { get; } = [];

	public SyncOperationJournal? PendingJournal { get; set; }

	/// <summary>Rejects incremental comparison after local recovery without discarding replica configuration.</summary>
	public bool NeedsFullComparison(Guid currentRecoveryEpoch)
		=> this.LastLocalRevision is null || this.RecoveryEpoch != currentRecoveryEpoch;
}
