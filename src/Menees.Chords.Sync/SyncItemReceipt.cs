namespace Menees.Chords.Sync;

internal sealed record SyncItemReceipt(string Id, string Name, string Version, long Length)
{
	public static SyncItemReceipt From(CloudReplicaItem item) => new(item.Id.Value, item.Name, item.Version.Value, item.Length);

	public CloudReplicaItem ToItem() => new(new(this.Id), this.Name, new(this.Version), this.Length);
}
