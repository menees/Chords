namespace Menees.Chords.Sync;

public sealed class TrackedProviderItem
{
	public TrackedProviderItem(string logicalId, ProviderItemId itemId, ProviderItemVersion version)
	{
		this.LogicalId = logicalId;
		this.ItemId = itemId;
		this.Version = version;
	}

	public string LogicalId { get; }

	public ProviderItemId ItemId { get; }

	public ProviderItemVersion Version { get; }
}
