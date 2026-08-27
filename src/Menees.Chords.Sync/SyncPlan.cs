namespace Menees.Chords.Sync;

public sealed class SyncPlan
{
	public SyncPlan(CloudReplicaIdentity target, SyncOptions options, IEnumerable<SyncOperation> operations, IEnumerable<SyncConflict>? conflicts = null)
	{
		this.Target = target;
		this.Options = options;
		this.Operations = [.. operations.OrderBy(o => o.SafeOrder)];
		this.Conflicts = conflicts?.ToArray() ?? [];
	}

	public CloudReplicaIdentity Target { get; }

	public SyncOptions Options { get; }

	public IReadOnlyList<SyncOperation> Operations { get; }

	public IReadOnlyList<SyncConflict> Conflicts { get; }

	public long EstimatedBytes => this.Operations.Sum(o => o.EstimatedBytes);
}
