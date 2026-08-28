namespace Menees.Chords.Sync;

public sealed class CloudReplicaIdentity
{
	public CloudReplicaIdentity(string providerKind, string accountId, string remoteFolderId)
	{
		this.ProviderKind = Require(providerKind, nameof(providerKind));
		this.AccountId = Require(accountId, nameof(accountId));
		this.RemoteFolderId = Require(remoteFolderId, nameof(remoteFolderId));
	}

	public string ProviderKind { get; }

	public string AccountId { get; }

	public string RemoteFolderId { get; }

	private static string Require(string value, string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
		return value;
	}
}
