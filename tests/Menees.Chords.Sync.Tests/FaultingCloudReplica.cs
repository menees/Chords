#region Using Directives

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Sync.Tests;

/// <summary>A disposable cloud with failures injected after remote persistence, before the caller receives its receipt.</summary>
internal sealed class FaultingCloudReplica : ICloudReplica
{
	#region Private Data

	private readonly InMemoryCloudReplica inner = new(new("Test", "account", "folder"));

	#endregion

	#region Public Properties

	public CloudReplicaIdentity Identity => this.inner.Identity;

	public CloudReplicaCapabilities Capabilities => this.inner.Capabilities;

	public bool IsAuthenticated => this.inner.IsAuthenticated;

	public Action<string>? AfterMutation { get; set; }

	public int ListCount { get; private set; }

	public int AssetDownloads { get; private set; }

	public IList<string> Mutations => this.inner.MutationLog;

	#endregion

	#region Public Methods

	public Task<CloudReplicaItem> SeedAsync(string name, Stream content, CancellationToken cancellationToken)
		=> this.inner.CreateAsync(name, content, cancellationToken);

	public Task AuthenticateAsync(CancellationToken cancellationToken) => this.inner.AuthenticateAsync(cancellationToken);

	public Task DisconnectAsync(CancellationToken cancellationToken) => this.inner.DisconnectAsync(cancellationToken);

	public async Task<CloudChangeSet> ListOrGetChangesAsync(string? changeToken, CancellationToken cancellationToken)
	{
		this.ListCount++;
		return await this.inner.ListOrGetChangesAsync(changeToken, cancellationToken);
	}

	public async Task<Stream> DownloadAsync(ProviderItemId itemId, CancellationToken cancellationToken)
	{
		CloudChangeSet items = await this.inner.ListOrGetChangesAsync(null, cancellationToken);
		if (items.Items.Single(item => item.Id == itemId).Name != "database.json")
		{
			this.AssetDownloads++;
		}

		return await this.inner.DownloadAsync(itemId, cancellationToken);
	}

	public async Task<CloudReplicaItem> CreateAsync(string name, Stream content, CancellationToken cancellationToken)
	{
		CloudChangeSet items = await this.inner.ListOrGetChangesAsync(null, cancellationToken);
		if (items.Items.Any(item => item.Name == name))
		{
			const int Conflict = 409;
			throw new CloudRequestException(Conflict);
		}

		CloudReplicaItem result = await this.inner.CreateAsync(name, content, cancellationToken);
		this.AfterMutation?.Invoke("Create:" + name);
		return result;
	}

	public async Task<CloudReplicaItem> ReplaceAsync(
		ProviderItemId itemId, ProviderItemVersion expectedVersion, Stream content, CancellationToken cancellationToken)
	{
		CloudReplicaItem result = await this.inner.ReplaceAsync(itemId, expectedVersion, content, cancellationToken);
		this.AfterMutation?.Invoke("Replace:" + result.Name);
		return result;
	}

	public Task<CloudReplicaItem> RenameAsync(
		ProviderItemId itemId, ProviderItemVersion expectedVersion, string name, CancellationToken cancellationToken)
		=> this.inner.RenameAsync(itemId, expectedVersion, name, cancellationToken);

	public Task DeleteAsync(ProviderItemId itemId, ProviderItemVersion expectedVersion, CancellationToken cancellationToken)
		=> this.inner.DeleteAsync(itemId, expectedVersion, cancellationToken);

	public async Task<string> ReadCatalogAsync(CancellationToken cancellationToken)
	{
		CloudChangeSet items = await this.inner.ListOrGetChangesAsync(null, cancellationToken);
		CloudReplicaItem item = items.Items.Single(item => item.Name == "database.json");
		await using Stream stream = await this.inner.DownloadAsync(item.Id, cancellationToken);
		using StreamReader reader = new(stream, Encoding.UTF8);
		return await reader.ReadToEndAsync(cancellationToken);
	}

	#endregion
}
