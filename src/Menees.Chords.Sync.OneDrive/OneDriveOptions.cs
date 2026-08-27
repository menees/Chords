namespace Menees.Chords.Sync.OneDrive;

public sealed class OneDriveOptions
{
	public OneDriveOptions(string clientId, string accountId, string remoteFolderId)
	{
		this.ClientId = clientId;
		this.AccountId = accountId;
		this.RemoteFolderId = remoteFolderId;
	}

	public string ClientId { get; }

	public string AccountId { get; }

	public string RemoteFolderId { get; }
}
