namespace Menees.Chords.Sync;

public sealed class SyncReport
{
	public SyncReport(SyncPlan plan, int completedOperationCount, Exception? failure = null)
	{
		this.Plan = plan;
		this.CompletedOperationCount = completedOperationCount;
		this.Failure = failure;
	}

	public SyncPlan Plan { get; }

	public int CompletedOperationCount { get; }

	public Exception? Failure { get; }

	public bool Succeeded => this.Failure is null && this.CompletedOperationCount == this.Plan.Operations.Count;
}
