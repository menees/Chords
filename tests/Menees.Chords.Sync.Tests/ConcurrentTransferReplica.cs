#region Using Directives

using System.IO;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Sync.Tests;

/// <summary>Overlaps transfer entry points while serializing the disposable in-memory provider's storage.</summary>
internal sealed class ConcurrentTransferReplica : ICloudReplica, IDisposable
{
	#region Private Data

	private readonly FaultingCloudReplica inner;
	private readonly SemaphoreSlim storage = new(1);
	private int active;
	private int maximumActive;

	#endregion

	#region Constructors

	public ConcurrentTransferReplica(FaultingCloudReplica inner) => this.inner = inner;

	#endregion

	#region Public Properties

	public CloudReplicaIdentity Identity => this.inner.Identity;

	public CloudReplicaCapabilities Capabilities => this.inner.Capabilities;

	public bool IsAuthenticated => this.inner.IsAuthenticated;

	public Func<string, CancellationToken, Task>? BeforeTransfer { get; set; }

	public int MaximumActive => this.maximumActive;

	public int Active => this.active;

	#endregion

	#region Public Methods

	public void Dispose() => this.storage.Dispose();

	public Task AuthenticateAsync(CancellationToken cancellationToken) => this.inner.AuthenticateAsync(cancellationToken);

	public Task DisconnectAsync(CancellationToken cancellationToken) => this.inner.DisconnectAsync(cancellationToken);

	public Task<CloudChangeSet> ListOrGetChangesAsync(string? changeToken, CancellationToken cancellationToken)
		=> this.StoredAsync(() => this.inner.ListOrGetChangesAsync(changeToken, cancellationToken), cancellationToken);

	public async Task<Stream> DownloadAsync(ProviderItemId itemId, CancellationToken cancellationToken)
	{
		CloudChangeSet items = await this.ListOrGetChangesAsync(null, cancellationToken);
		string name = items.Items.Single(item => item.Id == itemId).Name;
		return await this.TransferAsync(
			name,
			() => this.StoredAsync(() => this.inner.DownloadAsync(itemId, cancellationToken), cancellationToken),
			cancellationToken);
	}

	public Task<CloudReplicaItem> CreateAsync(string name, Stream content, CancellationToken cancellationToken)
		=> this.TransferAsync(
			name,
			() => this.StoredAsync(() => this.inner.CreateAsync(name, content, cancellationToken), cancellationToken),
			cancellationToken);

	public Task<CloudReplicaItem> ReplaceAsync(
		ProviderItemId itemId, ProviderItemVersion expectedVersion, Stream content, CancellationToken cancellationToken)
		=> this.StoredAsync(() => this.inner.ReplaceAsync(itemId, expectedVersion, content, cancellationToken), cancellationToken);

	public Task<CloudReplicaItem> RenameAsync(
		ProviderItemId itemId, ProviderItemVersion expectedVersion, string name, CancellationToken cancellationToken)
		=> this.StoredAsync(() => this.inner.RenameAsync(itemId, expectedVersion, name, cancellationToken), cancellationToken);

	public Task DeleteAsync(ProviderItemId itemId, ProviderItemVersion expectedVersion, CancellationToken cancellationToken)
		=> this.StoredAsync(
			async () =>
		{
			await this.inner.DeleteAsync(itemId, expectedVersion, cancellationToken);
			return true;
		},
			cancellationToken);

	#endregion

	#region Private Methods

	private async Task<T> TransferAsync<T>(string name, Func<Task<T>> operation, CancellationToken token)
	{
		T result;
		if (name == "database.json")
		{
			this.Active.ShouldBe(0, "The catalog must wait for all sheet transfers.");
			result = await operation();
		}
		else
		{
			int count = Interlocked.Increment(ref this.active);
			int previous;
			do
			{
				previous = this.maximumActive;
			}
			while (count > previous && Interlocked.CompareExchange(ref this.maximumActive, count, previous) != previous);
			try
			{
				if (this.BeforeTransfer is not null)
				{
					await this.BeforeTransfer(name, token);
				}

				result = await operation();
			}
			finally
			{
				Interlocked.Decrement(ref this.active);
			}
		}

		return result;
	}

	private async Task<T> StoredAsync<T>(Func<Task<T>> operation, CancellationToken token)
	{
		await this.storage.WaitAsync(token);
		try
		{
			return await operation();
		}
		finally
		{
			this.storage.Release();
		}
	}

	#endregion
}
