#region Using Directives

using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Sync.Tests;

[TestClass]
public sealed class ReplicaStateStoreTests
{
	#region Private Data

	private readonly string directory = Path.Combine(Path.GetTempPath(), "ChordReplicaTests-" + Guid.NewGuid().ToString("N"));
	private readonly CloudReplicaKey key = new(Guid.NewGuid(), Guid.NewGuid(), new("OneDrive", "account/../opaque", "folder"));

	#endregion

	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestCleanup]
	public void Cleanup() => Directory.Delete(this.directory, recursive: true);

	[TestMethod]
	public async Task CheckpointAndJournalSurviveReopenWithoutTouchingStateForProgress()
	{
		ReplicaStateStore store = new(this.directory);
		Guid epoch = Guid.NewGuid();
		using (ReplicaStateLease lease = store.Acquire(this.key))
		{
			CloudReplicaState state = new(this.key) { LastLocalRevision = "revision", RecoveryEpoch = epoch, MergeBase = "fingerprints" };
			state.TrackedItems.Add(new("sheet", new("id"), new("etag")));
			await lease.SaveStateAsync(state, this.TestContext.CancellationToken);
			string statePath = Directory.GetFiles(this.directory, "state.json", SearchOption.AllDirectories).Single();

			// A held read handle denies rewrites on Windows; journal progress must not reopen the state for writing.
			using FileStream stateLock = new(statePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			SyncOperationJournal journal = CreateJournal(this.key);
			await lease.SaveJournalAsync(journal, this.TestContext.CancellationToken);
			string journalPath = Directory.GetFiles(this.directory, "journal.json", SearchOption.AllDirectories).Single();
			using FileStream planLock = new(journalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
			journal.MarkCompleted("asset");
			await lease.MarkCompletedAsync(journal, "asset", this.TestContext.CancellationToken);
		}

		using ReplicaStateLease reopened = store.Acquire(this.key);
		CloudReplicaState restored = await reopened.LoadStateAsync(this.TestContext.CancellationToken);
		restored.LastLocalRevision.ShouldBe("revision");
		restored.RecoveryEpoch.ShouldBe(epoch);
		restored.TrackedItems.Single().Version.Value.ShouldBe("etag");
		restored.PendingJournal.ShouldNotBeNull();
		restored.PendingJournal.PendingOperations.Single().Id.ShouldBe("database");
		restored.NeedsFullComparison(Guid.NewGuid()).ShouldBeTrue();
		reopened.ClearJournal();
		(await reopened.LoadJournalAsync(this.TestContext.CancellationToken)).ShouldBeNull();
	}

	[TestMethod]
	public void ReplicaLeaseIsExclusiveAndOtherAccountsAreIndependent()
	{
		ReplicaStateStore store = new(this.directory);
		using ReplicaStateLease first = store.Acquire(this.key);
		Should.Throw<IOException>(() => store.Acquire(this.key));
		using ReplicaStateLease other = store.Acquire(new(this.key.BookId, this.key.DeviceId, new("OneDrive", "other", "folder")));
		Directory.GetDirectories(this.directory).Length.ShouldBe(2);
	}

	[TestMethod]
	public async Task CancellationAndOrphanTemporaryDoNotDestroyCheckpoint()
	{
		ReplicaStateStore store = new(this.directory);
		using ReplicaStateLease lease = store.Acquire(this.key);
		CloudReplicaState state = new(this.key) { LastLocalRevision = "before" };
		await lease.SaveStateAsync(state, this.TestContext.CancellationToken);
		state.LastLocalRevision = "after";
		await Should.ThrowAsync<OperationCanceledException>(() => lease.SaveStateAsync(state, new(canceled: true)));
		string statePath = Directory.GetFiles(this.directory, "state.json", SearchOption.AllDirectories).Single();
		await File.WriteAllTextAsync(statePath + ".tmp", "interrupted write", this.TestContext.CancellationToken);
		(await lease.LoadStateAsync(this.TestContext.CancellationToken)).LastLocalRevision.ShouldBe("before");
		await lease.SaveStateAsync(state, this.TestContext.CancellationToken);
		File.Exists(statePath + ".tmp").ShouldBeFalse();
		(await lease.LoadStateAsync(this.TestContext.CancellationToken)).LastLocalRevision.ShouldBe("after");
	}

	[TestMethod]
	public async Task CorruptAndForeignStateAreRejectedWithoutResettingIt()
	{
		using ReplicaStateLease lease = new ReplicaStateStore(this.directory).Acquire(this.key);
		await lease.SaveStateAsync(new(this.key), this.TestContext.CancellationToken);
		string path = Directory.GetFiles(this.directory, "state.json", SearchOption.AllDirectories).Single();
		JsonObject document = JsonNode.Parse(await File.ReadAllTextAsync(path, this.TestContext.CancellationToken))!.AsObject();
		document["BookId"] = Guid.NewGuid();
		string foreign = document.ToJsonString();
		await File.WriteAllTextAsync(path, foreign, this.TestContext.CancellationToken);
		await Should.ThrowAsync<InvalidDataException>(() => lease.LoadStateAsync(this.TestContext.CancellationToken));
		(await File.ReadAllTextAsync(path, this.TestContext.CancellationToken)).ShouldBe(foreign);
		await File.WriteAllTextAsync(path, "{broken", this.TestContext.CancellationToken);
		await Should.ThrowAsync<InvalidDataException>(() => lease.LoadStateAsync(this.TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task UnsafeJournalAndDifferentTargetAreRejected()
	{
		using ReplicaStateLease lease = new ReplicaStateStore(this.directory).Acquire(this.key);
		SyncOperationJournal unsafeJournal = CreateJournal(this.key, "../outside.txt");
		await Should.ThrowAsync<InvalidDataException>(() => lease.SaveJournalAsync(unsafeJournal, this.TestContext.CancellationToken));
		CloudReplicaKey other = new(this.key.BookId, this.key.DeviceId, new("OneDrive", "other", "folder"));
		await Should.ThrowAsync<ArgumentException>(() => lease.SaveJournalAsync(CreateJournal(other), this.TestContext.CancellationToken));
		(await lease.LoadJournalAsync(this.TestContext.CancellationToken)).ShouldBeNull();
	}

	[TestMethod]
	public void UnknownCompletionAndDuplicateOperationIdsCannotProduceCompleteJournal()
	{
		Directory.CreateDirectory(this.directory);
		SyncPlan plan = CreateJournal(this.key).ConfirmedPlan;
		Should.Throw<ArgumentException>(() => new SyncOperationJournal(plan, ["unknown"]));
		SyncPlan duplicate = new(plan.Target, plan.Options, [plan.Operations[0], plan.Operations[0]]);
		Should.Throw<ArgumentException>(() => new SyncOperationJournal(duplicate));
	}

	[TestMethod]
	public async Task PartialProgressRecordIsIgnoredAndNextAppendRepairsTail()
	{
		ReplicaStateStore store = new(this.directory);
		using (ReplicaStateLease lease = store.Acquire(this.key))
		{
			SyncOperationJournal journal = CreateJournal(this.key);
			await lease.SaveJournalAsync(journal, this.TestContext.CancellationToken);
			await lease.MarkCompletedAsync(journal, "asset", this.TestContext.CancellationToken);
		}

		string progress = Directory.GetFiles(this.directory, "*.progress", SearchOption.AllDirectories).Single();
		await using (FileStream append = new(progress, FileMode.Append))
		{
			await append.WriteAsync(new byte[] { 1, 2, 3 }, this.TestContext.CancellationToken);
		}

		using ReplicaStateLease reopened = store.Acquire(this.key);
		SyncOperationJournal restored = (await reopened.LoadJournalAsync(this.TestContext.CancellationToken))!;
		restored.IsComplete.ShouldBeFalse();
		await reopened.MarkCompletedAsync(restored, "database", this.TestContext.CancellationToken);
		restored.IsComplete.ShouldBeTrue();
		new FileInfo(progress).Length.ShouldBe(2 * System.Security.Cryptography.SHA256.HashSizeInBytes);
		await reopened.MarkCompletedAsync(restored, "database", this.TestContext.CancellationToken);
		new FileInfo(progress).Length.ShouldBe(2 * System.Security.Cryptography.SHA256.HashSizeInBytes);
	}

	#endregion

	#region Private Methods

	private static SyncOperationJournal CreateJournal(CloudReplicaKey key, string path = "sheet.txt") => new(new(
		new(key.ProviderKind, key.AccountId, key.RemoteFolderId),
		new(),
		[new("asset", SyncOperationKind.UploadAsset, path, 42), new("database", SyncOperationKind.CommitCloudDatabase, "database.json")]));

	#endregion
}
