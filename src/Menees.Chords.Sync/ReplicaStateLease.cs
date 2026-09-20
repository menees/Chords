#region Using Directives

using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Exclusive replica access. Dispose only after outstanding operations finish; do not use concurrently.</summary>
public sealed class ReplicaStateLease : IDisposable
{
	#region Private Data

	private const long MaximumFileSize = 32 * 1024 * 1024;
	private readonly CloudReplicaKey key;
	private readonly string directory;
	private readonly FileStream lease;
	private bool disposed;
	private Guid journalId;
	private SyncOperationJournal? currentJournal;
	private HashSet<string> persistedCompleted = new(StringComparer.Ordinal);

	#endregion

	#region Constructors

	internal ReplicaStateLease(CloudReplicaKey key, string directory, FileStream lease)
	{
		this.key = key;
		this.directory = directory;
		this.lease = lease;
	}

	#endregion

	#region Public Methods

	public async Task<CloudReplicaState> LoadStateAsync(CancellationToken cancellationToken = default)
	{
		ReplicaStateDocument? document = await this.ReadAsync("state.json", cancellationToken).ConfigureAwait(false);
		CloudReplicaState state = new(this.key);
		if (document is not null)
		{
			state.LastSuccessfulSyncUtc = document.LastSuccessfulSyncUtc;
			state.RecoveryEpoch = document.RecoveryEpoch;
			state.LastLocalRevision = document.LastLocalRevision;
			state.ChangeToken = document.ChangeToken;
			state.MergeBase = document.MergeBase;
			ValidateItems(document.Items);
			foreach (ReplicaTrackedItem item in document.Items)
			{
				state.TrackedItems.Add(new(item.LogicalId, new(item.ItemId), new(item.Version)));
			}
		}

		state.PendingJournal = await this.LoadJournalAsync(cancellationToken).ConfigureAwait(false);
		return state;
	}

	/// <summary>Writes only the checkpoint. Journal progress is persisted separately to avoid rewriting the catalog per asset.</summary>
	public Task SaveStateAsync(CloudReplicaState state, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(state);
		if (!this.key.Equals(state.Key))
		{
			throw new ArgumentException("The state belongs to another replica.", nameof(state));
		}

		ReplicaStateDocument document = ReplicaStateDocument.Create(this.key);
		document.DocumentKind = "state.json";
		document.LastSuccessfulSyncUtc = state.LastSuccessfulSyncUtc;
		document.RecoveryEpoch = state.RecoveryEpoch;
		document.LastLocalRevision = state.LastLocalRevision;
		document.ChangeToken = state.ChangeToken;
		document.MergeBase = state.MergeBase;
		document.Items = [.. state.TrackedItems.Select(item => new ReplicaTrackedItem(item.LogicalId, item.ItemId.Value, item.Version.Value))];
		ValidateItems(document.Items);
		return this.WriteAsync("state.json", document, cancellationToken);
	}

	public async Task<SyncOperationJournal?> LoadJournalAsync(CancellationToken cancellationToken = default)
	{
		ReplicaStateDocument? document = await this.ReadAsync("journal.json", cancellationToken).ConfigureAwait(false);
		SyncOperationJournal? result = null;
		if (document is not null)
		{
			try
			{
				ValidateJournal(document);
				SyncPlan plan = new(document.Target!, document.Options!, document.Operations, document.Conflicts);
				result = new(plan, document.CompletedOperationIds);
				await ReplicaProgressLog.ReadAsync(this.ProgressPath(document.JournalId), result, cancellationToken).ConfigureAwait(false);
				this.journalId = document.JournalId;
			}
			catch (ArgumentException exception)
			{
				throw new InvalidDataException("Invalid persisted synchronization journal.", exception);
			}
		}

		this.currentJournal = result;
		this.persistedCompleted = new(result?.CompletedOperationIds ?? [], StringComparer.Ordinal);
		return result;
	}

	public async Task SaveJournalAsync(SyncOperationJournal journal, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(journal);
		ReplicaStateDocument document = ReplicaStateDocument.Create(this.key);
		document.Target = journal.ConfirmedPlan.Target;
		if (!document.Matches(this.key))
		{
			throw new ArgumentException("The journal belongs to another replica.", nameof(journal));
		}

		document.DocumentKind = "journal.json";
		document.JournalId = Guid.NewGuid();
		document.Options = journal.ConfirmedPlan.Options;
		document.Operations = [.. journal.ConfirmedPlan.Operations];
		document.Conflicts = [.. journal.ConfirmedPlan.Conflicts];
		document.CompletedOperationIds = [.. journal.CompletedOperationIds];
		ValidateJournal(document);
		await this.WriteAsync("journal.json", document, cancellationToken).ConfigureAwait(false);
		this.journalId = document.JournalId;
		this.currentJournal = journal;
		this.persistedCompleted = new(document.CompletedOperationIds, StringComparer.Ordinal);
	}

	/// <summary>Durably acknowledges one completed operation without rewriting the plan or replica checkpoint.</summary>
	public async Task MarkCompletedAsync(SyncOperationJournal journal, string operationId, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(this.disposed, this);
		cancellationToken.ThrowIfCancellationRequested();
		if (!ReferenceEquals(journal, this.currentJournal) || this.journalId == Guid.Empty || !journal.ContainsOperation(operationId))
		{
			throw new ArgumentException("Load or save this journal through the lease before acknowledging an operation.", nameof(journal));
		}

		if (!this.persistedCompleted.Contains(operationId))
		{
			await ReplicaProgressLog.AppendAsync(this.ProgressPath(this.journalId), operationId, cancellationToken).ConfigureAwait(false);
			this.persistedCompleted.Add(operationId);
			journal.MarkCompleted(operationId);
		}
	}

	/// <summary>Call only after successful state checkpointing; a leftover completed journal can be reconciled after a crash.</summary>
	public void ClearJournal()
	{
		ObjectDisposedException.ThrowIf(this.disposed, this);
		File.Delete(Path.Combine(this.directory, "journal.json"));
		if (this.journalId != Guid.Empty)
		{
			File.Delete(this.ProgressPath(this.journalId));
		}

		this.currentJournal = null;
		this.persistedCompleted.Clear();
		this.journalId = Guid.Empty;
	}

	public void Dispose()
	{
		this.disposed = true;
		this.lease.Dispose();
	}

	#endregion

	#region Private Methods

	private static void ValidateItems(ReplicaTrackedItem[] items)
	{
		HashSet<string> logicalIds = new(StringComparer.Ordinal);
		foreach (ReplicaTrackedItem item in items)
		{
			if (item is null || string.IsNullOrWhiteSpace(item.LogicalId) || string.IsNullOrWhiteSpace(item.ItemId)
				|| string.IsNullOrWhiteSpace(item.Version) || !logicalIds.Add(item.LogicalId))
			{
				throw new InvalidDataException("Invalid or duplicate tracked replica item.");
			}
		}
	}

	private static void ValidateJournal(ReplicaStateDocument document)
	{
		if (document.JournalId == Guid.Empty || document.Options is null || !Enum.IsDefined(document.Options.Direction))
		{
			throw new InvalidDataException("Invalid journal direction.");
		}

		foreach (SyncOperation operation in document.Operations)
		{
			if (operation is null || !Enum.IsDefined(operation.Kind) || operation.EstimatedBytes < 0
				|| PortableManagedFileName.Validate(operation.RelativePath).Count != 0)
			{
				throw new InvalidDataException("Invalid journal operation or unsafe asset path.");
			}
		}
	}

	private string ProgressPath(Guid id) => Path.Combine(this.directory, id.ToString("N") + ".progress");

	private async Task<ReplicaStateDocument?> ReadAsync(string name, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(this.disposed, this);
		cancellationToken.ThrowIfCancellationRequested();
		string path = Path.Combine(this.directory, name);
		ReplicaStateDocument? result = null;
		if (File.Exists(path))
		{
			try
			{
				await using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
				if (input.Length > MaximumFileSize)
				{
					throw new InvalidDataException("The replica checkpoint exceeds its size limit.");
				}

				result = await JsonSerializer.DeserializeAsync<ReplicaStateDocument>(
					input, cancellationToken: cancellationToken).ConfigureAwait(false);
				if (result is null || !result.Matches(this.key) || result.DocumentKind != name || result.Items is null || result.Operations is null
					|| result.Conflicts is null || result.CompletedOperationIds is null)
				{
					throw new InvalidDataException("Invalid, unsupported, or mismatched replica checkpoint. Existing state was retained.");
				}
			}
			catch (Exception exception) when (exception is JsonException or ArgumentException)
			{
				throw new InvalidDataException("The replica checkpoint is corrupt. Existing state was retained.", exception);
			}
		}

		return result;
	}

	private async Task WriteAsync(string name, ReplicaStateDocument document, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(this.disposed, this);
		cancellationToken.ThrowIfCancellationRequested();
		string target = Path.Combine(this.directory, name);
		string temporary = target + ".tmp";
		try
		{
			await using (FileStream output = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				await JsonSerializer.SerializeAsync(output, document, cancellationToken: cancellationToken).ConfigureAwait(false);
				if (output.Length > MaximumFileSize)
				{
					throw new InvalidDataException("The replica checkpoint exceeds its size limit.");
				}

				output.Flush(flushToDisk: true);
			}

			cancellationToken.ThrowIfCancellationRequested();
			File.Move(temporary, target, overwrite: true);
		}
		finally
		{
			File.Delete(temporary);
		}
	}

	#endregion
}
