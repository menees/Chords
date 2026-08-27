using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Menees.Chords.Sync.OneDrive;

public sealed class OneDriveCloudReplica : ICloudReplica
{
	private readonly IOneDriveTransport? transport;

	public OneDriveCloudReplica(OneDriveOptions options, IOneDriveTransport? transport = null)
	{
		this.Options = options;
		this.transport = transport;
		this.Identity = new CloudReplicaIdentity("OneDrive", options.AccountId, options.RemoteFolderId);
	}

	public OneDriveOptions Options { get; }

	public CloudReplicaIdentity Identity { get; }

	public CloudReplicaCapabilities Capabilities => CloudReplicaCapabilities.ChangeTokens
		| CloudReplicaCapabilities.Rename
		| CloudReplicaCapabilities.ConditionalMutation;

	public bool IsAuthenticated => this.transport?.IsAuthenticated ?? false;

	private IOneDriveTransport Transport => this.transport ?? throw new CloudTransportNotConfiguredException("OneDrive");

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
