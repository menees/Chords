namespace Menees.Chords.Sync;

/// <summary>Explicit versioned storage schema, independent of public model constructor signatures.</summary>
internal sealed class ReplicaStateDocument
{
	public int SchemaVersion { get; set; }

	public Guid BookId { get; set; }

	public Guid DeviceId { get; set; }

	public CloudReplicaIdentity? Target { get; set; }

	public DateTimeOffset? LastSuccessfulSyncUtc { get; set; }

	public Guid RecoveryEpoch { get; set; }

	public string? LastLocalRevision { get; set; }

	public string? ChangeToken { get; set; }

	public string? MergeBase { get; set; }

	public ReplicaTrackedItem[] Items { get; set; } = [];

	public Guid JournalId { get; set; }

	public string? DocumentKind { get; set; }

	public SyncOptions? Options { get; set; }

	public SyncOperation[] Operations { get; set; } = [];

	public SyncConflict[] Conflicts { get; set; } = [];

	public string[] CompletedOperationIds { get; set; } = [];

	public static ReplicaStateDocument Create(CloudReplicaKey key) => new()
	{
		SchemaVersion = 1, BookId = key.BookId, DeviceId = key.DeviceId,
		Target = new(key.ProviderKind, key.AccountId, key.RemoteFolderId),
	};

	public bool Matches(CloudReplicaKey key) => this.SchemaVersion == 1 && this.Target is not null
		&& new CloudReplicaKey(this.BookId, this.DeviceId, this.Target).Equals(key);
}
