namespace Menees.Chords.Sync;

public sealed class CloudReplicaKey : IEquatable<CloudReplicaKey>
{
	private const int HashMultiplier = 397;

	public CloudReplicaKey(Guid bookId, Guid deviceId, CloudReplicaIdentity identity)
	{
		this.BookId = bookId;
		this.DeviceId = deviceId;
		this.ProviderKind = identity.ProviderKind;
		this.AccountId = identity.AccountId;
		this.RemoteFolderId = identity.RemoteFolderId;
	}

	public Guid BookId { get; }

	public Guid DeviceId { get; }

	public string ProviderKind { get; }

	public string AccountId { get; }

	public string RemoteFolderId { get; }

	public bool Equals(CloudReplicaKey? other)
	{
		return other is not null
			&& this.BookId == other.BookId
			&& this.DeviceId == other.DeviceId
			&& StringComparer.Ordinal.Equals(this.ProviderKind, other.ProviderKind)
			&& StringComparer.Ordinal.Equals(this.AccountId, other.AccountId)
			&& StringComparer.Ordinal.Equals(this.RemoteFolderId, other.RemoteFolderId);
	}

	public override bool Equals(object? obj) => this.Equals(obj as CloudReplicaKey);

	public override int GetHashCode()
	{
		unchecked
		{
			int hash = this.BookId.GetHashCode();
			hash = (hash * HashMultiplier) ^ this.DeviceId.GetHashCode();
			hash = (hash * HashMultiplier) ^ StringComparer.Ordinal.GetHashCode(this.ProviderKind);
			hash = (hash * HashMultiplier) ^ StringComparer.Ordinal.GetHashCode(this.AccountId);
			return (hash * HashMultiplier) ^ StringComparer.Ordinal.GetHashCode(this.RemoteFolderId);
		}
	}
}
