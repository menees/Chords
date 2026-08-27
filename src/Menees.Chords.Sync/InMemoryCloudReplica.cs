namespace Menees.Chords.Sync;

#region Using Directives

using System.IO;
using System.Threading;
using System.Threading.Tasks;

#endregion

public sealed class InMemoryCloudReplica : ICloudReplica
{
	#region Private Data Members

	private const int StreamCopyBufferSize = 81920;
	private readonly Dictionary<ProviderItemId, StoredItem> items = [];
	private int nextId = 1;
	private int changeNumber;

	#endregion

	#region Constructors

	public InMemoryCloudReplica(CloudReplicaIdentity identity) => this.Identity = identity;

	#endregion

	#region Public Properties

	public CloudReplicaIdentity Identity { get; }

	public CloudReplicaCapabilities Capabilities => CloudReplicaCapabilities.ChangeTokens
		| CloudReplicaCapabilities.Rename
		| CloudReplicaCapabilities.ConditionalMutation;

	public bool IsAuthenticated { get; private set; }

	public IList<string> MutationLog { get; } = [];

	#endregion

	#region Public Methods

	public Task AuthenticateAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		this.IsAuthenticated = true;
		return Task.CompletedTask;
	}

	public Task DisconnectAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		this.IsAuthenticated = false;
		return Task.CompletedTask;
	}

	public Task<CloudChangeSet> ListOrGetChangesAsync(string? changeToken, CancellationToken cancellationToken)
	{
		this.RequireAuthentication(cancellationToken);
		IReadOnlyList<CloudReplicaItem> result = changeToken == this.changeNumber.ToString()
			? []
			: this.items.Values.Select(i => i.Item).OrderBy(i => i.Name, StringComparer.Ordinal).ToArray();
		return Task.FromResult(new CloudChangeSet(result, this.changeNumber.ToString()));
	}

	public Task<Stream> DownloadAsync(ProviderItemId itemId, CancellationToken cancellationToken)
	{
		this.RequireAuthentication(cancellationToken);
		Stream result = new MemoryStream(this.items[itemId].Content, false);
		return Task.FromResult(result);
	}

	public async Task<CloudReplicaItem> CreateAsync(string name, Stream content, CancellationToken cancellationToken)
	{
		this.RequireAuthentication(cancellationToken);
		ProviderItemId id = new($"item-{this.nextId++:D4}");
		byte[] bytes = await ReadAsync(content, cancellationToken).ConfigureAwait(false);
		CloudReplicaItem item = new(id, name, this.NextVersion(), bytes.LongLength);
		this.items.Add(id, new(item, bytes));
		this.MutationLog.Add($"Create:{name}");
		return item;
	}

	public async Task<CloudReplicaItem> ReplaceAsync(
		ProviderItemId itemId,
		ProviderItemVersion expectedVersion,
		Stream content,
		CancellationToken cancellationToken)
	{
		this.RequireAuthentication(cancellationToken);
		StoredItem stored = this.GetExpected(itemId, expectedVersion);
		byte[] bytes = await ReadAsync(content, cancellationToken).ConfigureAwait(false);
		stored.Item = new(itemId, stored.Item.Name, this.NextVersion(), bytes.LongLength);
		stored.Content = bytes;
		this.MutationLog.Add($"Replace:{stored.Item.Name}");
		return stored.Item;
	}

	public Task<CloudReplicaItem> RenameAsync(
		ProviderItemId itemId,
		ProviderItemVersion expectedVersion,
		string name,
		CancellationToken cancellationToken)
	{
		this.RequireAuthentication(cancellationToken);
		StoredItem stored = this.GetExpected(itemId, expectedVersion);
		stored.Item = new(itemId, name, this.NextVersion(), stored.Item.Length);
		this.MutationLog.Add($"Rename:{name}");
		return Task.FromResult(stored.Item);
	}

	public Task DeleteAsync(ProviderItemId itemId, ProviderItemVersion expectedVersion, CancellationToken cancellationToken)
	{
		this.RequireAuthentication(cancellationToken);
		StoredItem stored = this.GetExpected(itemId, expectedVersion);
		this.items.Remove(itemId);
		this.changeNumber++;
		this.MutationLog.Add($"Delete:{stored.Item.Name}");
		return Task.CompletedTask;
	}

	#endregion

	#region Private Methods

	private static async Task<byte[]> ReadAsync(Stream content, CancellationToken cancellationToken)
	{
		using (MemoryStream copy = new())
		{
			await content.CopyToAsync(copy, StreamCopyBufferSize, cancellationToken).ConfigureAwait(false);
			return copy.ToArray();
		}
	}

	private void RequireAuthentication(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (!this.IsAuthenticated)
		{
			throw new InvalidOperationException("Authenticate before using the replica.");
		}
	}

	private ProviderItemVersion NextVersion() => new($"v{++this.changeNumber}");

	private StoredItem GetExpected(ProviderItemId id, ProviderItemVersion version)
	{
		StoredItem item = this.items[id];
		if (!item.Item.Version.Equals(version))
		{
			throw new InvalidOperationException("The expected provider item version is stale.");
		}

		return item;
	}

	#endregion

	#region Private Types

	private sealed class StoredItem
	{
		#region Constructors

		public StoredItem(CloudReplicaItem item, byte[] content)
		{
			this.Item = item;
			this.Content = content;
		}

		#endregion

		#region Public Properties

		public CloudReplicaItem Item { get; set; }

		public byte[] Content { get; set; }

		#endregion
	}

	#endregion
}
