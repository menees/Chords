namespace Menees.Chords.Sync;

public sealed class SyncConflict
{
	public SyncConflict(string entityId, SyncConflictUnit unit, SyncSide winner, SyncSide discarded)
	{
		this.EntityId = entityId;
		this.Unit = unit;
		this.Winner = winner;
		this.Discarded = discarded;
	}

	public string EntityId { get; }

	public SyncConflictUnit Unit { get; }

	public SyncSide Winner { get; }

	public SyncSide Discarded { get; }
}
