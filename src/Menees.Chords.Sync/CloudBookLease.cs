#region Using Directives

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Serializes cloud-book access by cooperating clients, including downloads.</summary>
public sealed class CloudBookLease : ICloudReplica, IAsyncDisposable
{
	#region Private Data

	private const string LockName = "chordbook-sync-lock.json";
	private static readonly TimeSpan TakeoverAge = TimeSpan.FromMinutes(10);
	private static readonly TimeSpan RenewalInterval = TimeSpan.FromMinutes(1);
	private static readonly TimeSpan RequestLimit = TimeSpan.FromMinutes(4);
	private readonly ICloudReplica replica;
	private readonly TimeProvider clock;
	private readonly CancellationTokenSource lifetime = new();
	private readonly Task renewal;
	private readonly LockRecord owner;
	private CloudReplicaItem item;
	private long lastRenewed;
	private int dirty;
	private bool completed;

	#endregion

	#region Constructors

	private CloudBookLease(ICloudReplica replica, CloudReplicaItem item, LockRecord owner, TimeProvider clock, long started, bool recovering)
	{
		this.replica = replica;
		this.item = item;
		this.owner = owner;
		this.clock = clock;
		this.lastRenewed = started;
		this.dirty = recovering ? 1 : 0;
		this.renewal = this.RenewAsync();
	}

	#endregion

	#region Public Properties

	public CloudReplicaIdentity Identity => this.replica.Identity;

	public CloudReplicaCapabilities Capabilities => this.replica.Capabilities;

	public bool IsAuthenticated => this.replica.IsAuthenticated;

	#endregion

	#region Internal Properties

	internal Guid Token => this.owner.Token;

	internal Guid DeviceId => this.owner.DeviceId;

	#endregion

	#region Public Methods

	/// <summary>Acquires exclusive book access, or resumes this device's interrupted lease.</summary>
	public static Task<CloudBookLease> AcquireAsync(
		ICloudReplica replica, Guid device, string deviceName, bool takeOver = false, CancellationToken cancellationToken = default)
		=> AcquireAsync(replica, device, deviceName, takeOver, TimeProvider.System, cancellationToken);

	/// <summary>Marks the transfer complete so the lock can be released.</summary>
	public void Complete() => this.completed = true;

	public Task AuthenticateAsync(CancellationToken cancellationToken) => this.replica.AuthenticateAsync(cancellationToken);

	public Task DisconnectAsync(CancellationToken cancellationToken) => this.replica.DisconnectAsync(cancellationToken);

	public Task<CloudChangeSet> ListOrGetChangesAsync(string? changeToken, CancellationToken cancellationToken)
		=> this.RunAsync(token => this.replica.ListOrGetChangesAsync(changeToken, token), false, cancellationToken);

	public Task<Stream> DownloadAsync(ProviderItemId itemId, CancellationToken cancellationToken)
		=> this.RunAsync(token => this.replica.DownloadAsync(itemId, token), false, cancellationToken);

	public Task<CloudReplicaItem> CreateAsync(string name, Stream content, CancellationToken cancellationToken)
		=> this.RunAsync(token => this.replica.CreateAsync(name, content, token), true, cancellationToken);

	public Task<CloudReplicaItem> ReplaceAsync(
		ProviderItemId itemId, ProviderItemVersion expectedVersion, Stream content, CancellationToken cancellationToken)
		=> this.RunAsync(token => this.replica.ReplaceAsync(itemId, expectedVersion, content, token), true, cancellationToken);

	public Task<CloudReplicaItem> RenameAsync(
		ProviderItemId itemId, ProviderItemVersion expectedVersion, string name, CancellationToken cancellationToken)
		=> this.RunAsync(token => this.replica.RenameAsync(itemId, expectedVersion, name, token), true, cancellationToken);

	public Task DeleteAsync(ProviderItemId itemId, ProviderItemVersion expectedVersion, CancellationToken cancellationToken)
		=> this.RunAsync(
			async token =>
			{
				await this.replica.DeleteAsync(itemId, expectedVersion, token).ConfigureAwait(false);
				return true;
			},
			true,
			cancellationToken);

	public async ValueTask DisposeAsync()
	{
		await this.lifetime.CancelAsync().ConfigureAwait(false);
		await this.renewal.ConfigureAwait(false);
		try
		{
			if (this.completed || Volatile.Read(ref this.dirty) == 0)
			{
				using CancellationTokenSource timeout = new(RequestLimit);
				await this.replica.DeleteAsync(this.item.Id, this.item.Version, timeout.Token).ConfigureAwait(false);
			}
		}
		catch (CloudRequestException exception) when (exception.IsConcurrencyFailure || exception.StatusCode == (int)HttpStatusCode.NotFound)
		{
			// The lock was already released or replaced.
		}
		finally
		{
			this.lifetime.Dispose();
		}
	}

	#endregion

	#region Internal Methods

	internal static async Task<CloudBookLease> AcquireAsync(
		ICloudReplica replica, Guid device, string deviceName, bool takeOver, TimeProvider clock, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(replica);
		if (device == Guid.Empty || !replica.Capabilities.HasFlag(CloudReplicaCapabilities.ConditionalMutation))
		{
			throw new ArgumentException("Cloud book locking requires a device identity and conditional writes.");
		}

		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(RequestLimit);
		CancellationToken token = timeout.Token;
		var listing = await replica.ListOrGetChangesAsync(null, token).ConfigureAwait(false);
		CloudReplicaItem? existing = CloudBookReader.IndexItems(listing).GetValueOrDefault(LockName);
		long started = clock.GetTimestamp();
		LockRecord owner = new(device, deviceName, Guid.NewGuid(), clock.GetUtcNow());
		CloudReplicaItem claimed;
		if (existing is null)
		{
			using MemoryStream content = Encode(owner);
			claimed = await replica.CreateAsync(LockName, content, token).ConfigureAwait(false);
		}
		else
		{
			LockRecord previous = await ReadAsync(replica, existing, token).ConfigureAwait(false);
			bool expired = clock.GetUtcNow() - previous.RenewedUtc >= TakeoverAge;
			if (previous.DeviceId != device && (!takeOver || !expired))
			{
				throw new CloudBookLockedException(previous.DeviceName, previous.RenewedUtc, expired);
			}

			owner = owner with { Token = previous.DeviceId == device ? previous.Token : owner.Token };
			using MemoryStream content = Encode(owner);
			claimed = await replica.ReplaceAsync(existing.Id, existing.Version, content, token).ConfigureAwait(false);
		}

		return new(replica, claimed, owner, clock, started, existing is not null);
	}

	#endregion

	#region Private Methods

	private static MemoryStream Encode(LockRecord record) => new(JsonSerializer.SerializeToUtf8Bytes(record), writable: false);

	private static async Task<LockRecord> ReadAsync(ICloudReplica replica, CloudReplicaItem item, CancellationToken cancellationToken)
	{
		const int MaximumLockBytes = 4096;
		if (item.Length is < 1 or > MaximumLockBytes)
		{
			throw new InvalidDataException("The cloud sync lock is invalid.");
		}

		await using Stream input = await replica.DownloadAsync(item.Id, cancellationToken).ConfigureAwait(false);
		LockRecord? record = await JsonSerializer.DeserializeAsync<LockRecord>(input, cancellationToken: cancellationToken).ConfigureAwait(false);
		return record is not null && record.Token != Guid.Empty && record.DeviceId != Guid.Empty && record.RenewedUtc != default
			? record : throw new InvalidDataException("The cloud sync lock is invalid.");
	}

	private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, bool writing, CancellationToken cancellationToken)
	{
		if (this.lifetime.IsCancellationRequested
			|| this.clock.GetElapsedTime(Interlocked.Read(ref this.lastRenewed)) >= TakeoverAge - RequestLimit - RenewalInterval)
		{
			throw new SyncMergeException("The cloud lock could not be renewed. Use Sync Now to recover.");
		}

		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.lifetime.Token);
		timeout.CancelAfter(RequestLimit);
		if (writing)
		{
			Interlocked.Exchange(ref this.dirty, 1);
		}

		T result;
		try
		{
			result = await operation(timeout.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			await this.lifetime.CancelAsync().ConfigureAwait(false);
			throw new SyncMergeException("A cloud request timed out or the sync lock was lost. Use Sync Now to recover.");
		}

		return result;
	}

	private async Task RenewAsync()
	{
		CancellationToken token = this.lifetime.Token;
		try
		{
			using PeriodicTimer timer = new(RenewalInterval, this.clock);
			while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
			{
				using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
				timeout.CancelAfter(RenewalInterval);
				long started = this.clock.GetTimestamp();
				using MemoryStream content = Encode(this.owner with { RenewedUtc = this.clock.GetUtcNow() });
				this.item = await this.replica.ReplaceAsync(this.item.Id, this.item.Version, content, timeout.Token).ConfigureAwait(false);
				Interlocked.Exchange(ref this.lastRenewed, started);
			}
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
		}
		catch (Exception exception) when (exception is CloudRequestException or IOException or HttpRequestException or OperationCanceledException)
		{
			await this.lifetime.CancelAsync().ConfigureAwait(false);
		}
	}

	#endregion

	#region Private Types

	private sealed record LockRecord(Guid DeviceId, string DeviceName, Guid Token, DateTimeOffset RenewedUtc);
	#endregion
}
