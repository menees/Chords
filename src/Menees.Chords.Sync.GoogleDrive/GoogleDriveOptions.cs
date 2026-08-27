namespace Menees.Chords.Sync.GoogleDrive;

public sealed class GoogleDriveOptions
{
	public GoogleDriveOptions(string clientId, string accountId, string remoteFolderId)
	{
		this.ClientId = clientId;
		this.AccountId = accountId;
		this.RemoteFolderId = remoteFolderId;
	}

	public string ClientId { get; }

	public string AccountId { get; }

	public string RemoteFolderId { get; }
}
