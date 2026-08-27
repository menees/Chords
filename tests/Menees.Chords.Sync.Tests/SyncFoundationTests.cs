#region Using Directives

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Sync.Tests;

[TestClass]
public sealed class SyncFoundationTests
{
	#region Private Data

	private static readonly CloudReplicaIdentity Target = new("Test", "account", "folder");
	private static readonly string[] ExpectedPendingOperationIds = ["database", "delete"];
	private static readonly string[] ExpectedPlanOperationIds = ["asset", "database", "delete"];

	#endregion

	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public void PlanOrdersAssetsBeforeDatabaseAndDeletesAfterDatabase()
	{
		SyncPlan plan = new(
			Target,
			new SyncOptions(),
			[
				new SyncOperation("delete", SyncOperationKind.DeleteCloudAsset, "old.txt"),
				new SyncOperation("database", SyncOperationKind.CommitCloudDatabase, "database.json"),
				new SyncOperation("asset", SyncOperationKind.UploadAsset, "song.txt"),
			]);

		plan.Operations.Select(o => o.Id).ShouldBe(ExpectedPlanOperationIds);
	}

	[TestMethod]
	public void ReplicaKeysAreIndependentByEveryIdentityComponent()
	{
		Guid book = Guid.NewGuid();
		Guid device = Guid.NewGuid();
		CloudReplicaKey oneDrive = new(book, device, new CloudReplicaIdentity("OneDrive", "a", "folder"));
		CloudReplicaKey googleDrive = new(book, device, new CloudReplicaIdentity("GoogleDrive", "a", "folder"));
		CloudReplicaKey otherAccount = new(book, device, new CloudReplicaIdentity("OneDrive", "b", "folder"));

		oneDrive.ShouldNotBe(googleDrive);
		oneDrive.ShouldNotBe(otherAccount);
		oneDrive.ShouldBe(new CloudReplicaKey(book, device, new CloudReplicaIdentity("OneDrive", "a", "folder")));
	}

	[TestMethod]
	public void JournalResumesOnlyUnfinishedOperations()
	{
		SyncPlan plan = new(
			Target,
			new SyncOptions(),
			[
				new SyncOperation("asset", SyncOperationKind.UploadAsset, "song.txt"),
				new SyncOperation("database", SyncOperationKind.CommitCloudDatabase, "database.json"),
				new SyncOperation("delete", SyncOperationKind.DeleteCloudAsset, "old.txt"),
			]);
		SyncOperationJournal interrupted = new(plan);
		interrupted.MarkCompleted("asset");

		SyncOperationJournal resumed = new(plan, interrupted.CompletedOperationIds);

		resumed.PendingOperations.Select(o => o.Id).ShouldBe(ExpectedPendingOperationIds);
		resumed.IsComplete.ShouldBeFalse();
		resumed.MarkCompleted("database");
		resumed.MarkCompleted("delete");
		resumed.IsComplete.ShouldBeTrue();
	}

	[TestMethod]
	public void DirectionPolicyControlsTransfersAndConflictWinner()
	{
		SyncDirectionPolicy.AllowsUpload(SyncDirection.TwoWay).ShouldBeTrue();
		SyncDirectionPolicy.AllowsDownload(SyncDirection.TwoWay).ShouldBeTrue();
		SyncDirectionPolicy.AllowsUpload(SyncDirection.UpdateThisDevice).ShouldBeFalse();
		SyncDirectionPolicy.AllowsDownload(SyncDirection.UpdateThisDevice).ShouldBeTrue();
		SyncDirectionPolicy.AllowsUpload(SyncDirection.UpdateCloud).ShouldBeTrue();
		SyncDirectionPolicy.AllowsDownload(SyncDirection.UpdateCloud).ShouldBeFalse();

		DateTimeOffset local = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
		DateTimeOffset cloud = local.AddDays(-1);
		SyncDirectionPolicy.ChooseWinner(SyncDirection.UpdateThisDevice, local, cloud).ShouldBe(SyncSide.Cloud);
		SyncDirectionPolicy.ChooseWinner(SyncDirection.UpdateCloud, cloud, local).ShouldBe(SyncSide.Local);
		SyncDirectionPolicy.ChooseWinner(SyncDirection.TwoWay, local, cloud).ShouldBe(SyncSide.Local);
	}

	[TestMethod]
	public void OrderedSetlistIsRepresentedAsOneConflictUnit()
	{
		SyncConflict conflict = new("setlist-id", SyncConflictUnit.WholeOrderedSetlist, SyncSide.Cloud, SyncSide.Local);
		SyncPlan plan = new(Target, new SyncOptions(), [], [conflict]);

		plan.Conflicts.Single().Unit.ShouldBe(SyncConflictUnit.WholeOrderedSetlist);
		plan.Conflicts.Single().Discarded.ShouldBe(SyncSide.Local);
	}

	[TestMethod]
	public async Task InMemoryReplicaUsesOpaqueIdsVersionsAndDeterministicChanges()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		InMemoryCloudReplica replica = new(Target);
		await replica.AuthenticateAsync(cancellationToken);
		using (MemoryStream content = new(Encoding.UTF8.GetBytes("first")))
		{
			CloudReplicaItem created = await replica.CreateAsync("song.txt", content, cancellationToken);
			created.Id.ToString().ShouldBe("item-0001");
			created.Version.ToString().ShouldBe("v1");
		}

		CloudChangeSet initial = await replica.ListOrGetChangesAsync(null, cancellationToken);
		initial.Items.Count.ShouldBe(1);
		CloudChangeSet unchanged = await replica.ListOrGetChangesAsync(initial.NextChangeToken, cancellationToken);
		unchanged.Items.ShouldBeEmpty();
	}

	#endregion
}
