namespace Menees.Chords.Sync;

public sealed class SyncOperationJournal
{
	private readonly HashSet<string> completedOperationIds;
	private readonly HashSet<string> operationIds;

	public SyncOperationJournal(SyncPlan confirmedPlan, IEnumerable<string>? completedOperationIds = null)
	{
		ArgumentNullException.ThrowIfNull(confirmedPlan);
		this.ConfirmedPlan = confirmedPlan;
		this.operationIds = new(confirmedPlan.Operations.Select(operation => operation.Id), StringComparer.Ordinal);
		if (this.operationIds.Count != confirmedPlan.Operations.Count)
		{
			throw new ArgumentException("Operation IDs must be unique.", nameof(confirmedPlan));
		}

		this.completedOperationIds = new(completedOperationIds ?? [], StringComparer.Ordinal);
		if (!this.completedOperationIds.IsSubsetOf(this.operationIds))
		{
			throw new ArgumentException("Completed operations must belong to the confirmed plan.", nameof(completedOperationIds));
		}
	}

	public SyncPlan ConfirmedPlan { get; }

	public IReadOnlyCollection<string> CompletedOperationIds => this.completedOperationIds;

	public IEnumerable<SyncOperation> PendingOperations => this.ConfirmedPlan.Operations.Where(o => !this.completedOperationIds.Contains(o.Id));

	public bool IsComplete => this.completedOperationIds.Count == this.ConfirmedPlan.Operations.Count;

	public void MarkCompleted(string operationId)
	{
		if (!this.operationIds.Contains(operationId))
		{
			throw new ArgumentException("The operation is not in the confirmed plan.", nameof(operationId));
		}

		this.completedOperationIds.Add(operationId);
	}

	internal bool ContainsOperation(string operationId) => this.operationIds.Contains(operationId);
}
