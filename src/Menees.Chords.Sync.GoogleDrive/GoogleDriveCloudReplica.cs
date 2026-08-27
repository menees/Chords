using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Menees.Chords.Sync.GoogleDrive;

public sealed class GoogleDriveCloudReplica : ICloudReplica
{
	private readonly IGoogleDriveTransport? transport;

	public GoogleDriveCloudReplica(GoogleDriveOptions options, IGoogleDriveTransport? transport = null)
	{
		this.Options = options;
		this.transport = transport;
		this.Identity = new CloudReplicaIdentity("GoogleDrive", options.AccountId, options.RemoteFolderId);
	}

	public GoogleDriveOptions Options { get; }

	public CloudReplicaIdentity Identity { get; }

	public CloudReplicaCapabilities Capabilities => CloudReplicaCapabilities.ChangeTokens
		| CloudReplicaCapabilities.Rename
		| CloudReplicaCapabilities.ConditionalMutation;

	public bool IsAuthenticated => this.transport?.IsAuthenticated ?? false;

	private IGoogleDriveTransport Transport => this.transport ?? throw new CloudTransportNotConfiguredException("Google Drive");

	public Task AuthenticateAsync(CancellationToken cancellationToken) => this.Transport.AuthenticateAsync(cancellationToken);

	public Task DisconnectAsync(CancellationToken cancellationToken) => this.Transport.DisconnectAsync(cancellationToken);

	public Task<CloudChangeSet> ListOrGetChangesAsync(string? changeToken, CancellationToken cancellationToken)
		=> this.Transport.ListOrGetChangesAsync(changeToken, cancellationToken);

	public Task<Stream> DownloadAsync(ProviderItemId itemId, CancellationToken cancellationToken) => this.Transport.DownloadAsync(itemId, cancellationToken);

	public Task<CloudReplicaItem> CreateAsync(string name, Stream content, CancellationToken cancellationToken)
		=> this.Transport.CreateAsync(name, content, cancellationToken);

	public Task<CloudReplicaItem> ReplaceAsync(ProviderItemId itemId, ProviderItemVersion expectedVersion, Stream content, CancellationToken cancellationToken)
		=> this.Transport.ReplaceAsync(itemId, expectedVersion, content, cancellationToken);

	public Task<CloudReplicaItem> RenameAsync(ProviderItemId itemId, ProviderItemVersion expectedVersion, string name, CancellationToken cancellationToken)
		=> this.Transport.RenameAsync(itemId, expectedVersion, name, cancellationToken);

	public Task DeleteAsync(ProviderItemId itemId, ProviderItemVersion expectedVersion, CancellationToken cancellationToken)
		=> this.Transport.DeleteAsync(itemId, expectedVersion, cancellationToken);
}
