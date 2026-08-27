using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Menees.Chords.Sync;

public interface ICloudReplica
{
	CloudReplicaIdentity Identity { get; }

	CloudReplicaCapabilities Capabilities { get; }

	bool IsAuthenticated { get; }

	Task AuthenticateAsync(CancellationToken cancellationToken);

	Task DisconnectAsync(CancellationToken cancellationToken);

	Task<CloudChangeSet> ListOrGetChangesAsync(string? changeToken, CancellationToken cancellationToken);

	Task<Stream> DownloadAsync(ProviderItemId itemId, CancellationToken cancellationToken);

	Task<CloudReplicaItem> CreateAsync(string name, Stream content, CancellationToken cancellationToken);

	Task<CloudReplicaItem> ReplaceAsync(ProviderItemId itemId, ProviderItemVersion expectedVersion, Stream content, CancellationToken cancellationToken);

	Task<CloudReplicaItem> RenameAsync(ProviderItemId itemId, ProviderItemVersion expectedVersion, string name, CancellationToken cancellationToken);

	Task DeleteAsync(ProviderItemId itemId, ProviderItemVersion expectedVersion, CancellationToken cancellationToken);
}
