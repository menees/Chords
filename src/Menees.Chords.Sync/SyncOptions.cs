namespace Menees.Chords.Sync;

public sealed class SyncOptions
{
	public SyncOptions(SyncDirection direction = SyncDirection.TwoWay, int maximumConcurrencyRetries = 2)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(maximumConcurrencyRetries);

		this.Direction = direction;
		this.MaximumConcurrencyRetries = maximumConcurrencyRetries;
	}

	public SyncDirection Direction { get; }

	public int MaximumConcurrencyRetries { get; }
}
