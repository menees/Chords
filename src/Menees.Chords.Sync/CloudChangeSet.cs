namespace Menees.Chords.Sync;

public sealed class CloudChangeSet
{
	public CloudChangeSet(IReadOnlyList<CloudReplicaItem> items, string? nextChangeToken)
	{
		this.Items = items;
		this.NextChangeToken = nextChangeToken;
	}

	public IReadOnlyList<CloudReplicaItem> Items { get; }

	public string? NextChangeToken { get; }
}
