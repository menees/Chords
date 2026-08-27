namespace Menees.Chords.Sync;

public sealed class SyncOperationJournal
{
	private readonly HashSet<string> completedOperationIds;

	public SyncOperationJournal(SyncPlan confirmedPlan, IEnumerable<string>? completedOperationIds = null)
	{
		this.ConfirmedPlan = confirmedPlan;
		this.completedOperationIds = new(completedOperationIds ?? [], StringComparer.Ordinal);
	}

	public SyncPlan ConfirmedPlan { get; }

	public IReadOnlyCollection<string> CompletedOperationIds => this.completedOperationIds;

	public IEnumerable<SyncOperation> PendingOperations => this.ConfirmedPlan.Operations.Where(o => !this.completedOperationIds.Contains(o.Id));

	public bool IsComplete => this.completedOperationIds.Count == this.ConfirmedPlan.Operations.Count;

	public void MarkCompleted(string operationId)
	{
		if (!this.ConfirmedPlan.Operations.Any(o => StringComparer.Ordinal.Equals(o.Id, operationId)))
		{
			throw new ArgumentException("The operation is not in the confirmed plan.", nameof(operationId));
		}

		this.completedOperationIds.Add(operationId);
	}
}
