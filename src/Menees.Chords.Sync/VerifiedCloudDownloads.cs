#region Using Directives

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Prefetches a bounded ordered window of verified disk streams; store writes remain serial.</summary>
internal sealed class VerifiedCloudDownloads : IAsyncDisposable
{
	#region Private Data

	private readonly int concurrency;
	private readonly ICloudReplica replica;
	private readonly CloudBookSnapshot snapshot;
	private readonly string stagingDirectory;
	private readonly IProgress<string>? progress;
	private readonly CancellationTokenSource lifetime;
	private readonly Queue<(Guid Id, Task<Stream> Stream)> pending = new();
	private readonly Lock progressLock = new();
	private int next;
	private int completed;

	#endregion

	#region Constructors

	public VerifiedCloudDownloads(
		ICloudReplica replica,
		CloudBookSnapshot snapshot,
		string stagingDirectory,
		IProgress<string>? progress,
		int concurrency,
		CancellationToken cancellationToken)
	{
		this.concurrency = concurrency;
		this.replica = replica;
		this.snapshot = snapshot;
		this.stagingDirectory = stagingDirectory;
		this.progress = progress;
		this.lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		this.Report(0);
	}

	#endregion

	#region Public Methods

	public async Task<Stream> OpenAsync(SongFile file, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (this.next == 0)
		{
			for (int index = 0; index < this.concurrency && this.next < this.snapshot.Database.SongFiles.Count; index++)
			{
				this.Enqueue();
			}
		}

		var queued = this.pending.Dequeue();
		if (queued.Id != file.Id)
		{
			throw new InvalidOperationException("Replica sheets must be staged in catalog order.");
		}

		Stream result = await queued.Stream.ConfigureAwait(false);
		if (this.next < this.snapshot.Database.SongFiles.Count)
		{
			this.Enqueue();
		}

		return result;
	}

	public async ValueTask DisposeAsync()
	{
		await this.lifetime.CancelAsync().ConfigureAwait(false);
		while (this.pending.TryDequeue(out var queued))
		{
			Stream? stream = null;
			try
			{
				stream = await queued.Stream.ConfigureAwait(false);
			}
#pragma warning disable CA1031 // Observe every failed worker while preserving the original transfer/commit exception.
			catch
			{
			}
#pragma warning restore CA1031
			if (stream is not null)
			{
				await stream.DisposeAsync().ConfigureAwait(false);
			}
		}

		this.lifetime.Dispose();
	}

	#endregion

	#region Private Methods

	private void Enqueue()
	{
		SongFile file = this.snapshot.Database.SongFiles[this.next++];
		this.pending.Enqueue((file.Id, this.DownloadAsync(file)));
	}

	private async Task<Stream> DownloadAsync(SongFile file)
	{
		CancellationToken token = this.lifetime.Token;
		Stream? staged = null;
		try
		{
			await using Stream source = await this.replica.DownloadAsync(this.snapshot.ManagedItems[file.Id].Id, token).ConfigureAwait(false);
			staged = await VerifiedAssetStage.CreateAsync(
				source,
				file.ContentHash,
				file.ObservedLength ?? this.snapshot.ManagedItems[file.Id].Length,
				this.stagingDirectory,
				token).ConfigureAwait(false);
			lock (this.progressLock)
			{
				this.Report(++this.completed);
			}

			return staged;
		}
		catch
		{
			if (staged is not null)
			{
				await staged.DisposeAsync().ConfigureAwait(false);
			}

			throw;
		}
	}

	private void Report(int count)
	{
		int total = this.snapshot.Database.SongFiles.Count;
		BookSyncProgressAdapter.Report(this.progress, new($"Downloading and verifying sheets: {count} of {total} completed.", count, total));
	}

	#endregion
}
