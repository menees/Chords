namespace Menees.Chords.Sync;

public sealed class CloudReplicaItem
{
	public CloudReplicaItem(ProviderItemId id, string name, ProviderItemVersion version, long length)
	{
		this.Id = id;
		this.Name = name;
		this.Version = version;
		this.Length = length;
	}

	public ProviderItemId Id { get; }

	public string Name { get; }

	public ProviderItemVersion Version { get; }

	public long Length { get; }
}
