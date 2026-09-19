#region Using Directives

using System.Diagnostics;
using System.Text.Json;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync.Tests;

[TestClass]
public sealed class BookSyncMergerTests
{
	#region Private Data

	private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
	private static readonly Guid Device = Guid.NewGuid();

	#endregion

	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public void IndependentEditsCombineWithoutMutatingInputs()
	{
		ChordDatabase local = CreateBook();
		ChordDatabase cloud = Copy(local);
		BookMergeBase baseline = BookSyncMerger.CreateBase(local);
		local.Songs[0].Title = "Local title";
		cloud.Songs[1].Title = "Cloud title";
		string beforeLocal = JsonSerializer.Serialize(local), beforeCloud = JsonSerializer.Serialize(cloud);
		BookMergeResult result = Merge(local, cloud, baseline);
		result.Database.Songs.Single(song => song.Id == local.Songs[0].Id).Title.ShouldBe("Local title");
		result.Database.Songs.Single(song => song.Id == local.Songs[1].Id).Title.ShouldBe("Cloud title");
		result.Conflicts.ShouldBeEmpty();
		JsonSerializer.Serialize(local).ShouldBe(beforeLocal);
		JsonSerializer.Serialize(cloud).ShouldBe(beforeCloud);
		result.Database.Songs[0].Title = "Detached";
		JsonSerializer.Serialize(local).ShouldBe(beforeLocal);
	}

	[TestMethod]
	public void SetlistConflictSelectsTheWholeOrderedCollection()
	{
		ChordDatabase local = CreateBook();
		local.Setlists.Add(new()
		{
			Id = Guid.NewGuid(),
			Name = "Show",
			Entries = [.. local.Songs.Select(song => new SetlistEntry { Id = Guid.NewGuid(), SongId = song.Id })],
		});
		ChordDatabase cloud = Copy(local);
		BookMergeBase baseline = BookSyncMerger.CreateBase(local);
		local.Setlists[0].Entries.Reverse();
		local.Setlists[0].Revision.ModifiedUtc = Now.AddMinutes(-10);
		cloud.Setlists[0].Entries.RemoveAt(1);
		cloud.Setlists[0].Revision.ModifiedUtc = Now;
		BookMergeResult result = Merge(local, cloud, baseline);
		result.Database.Setlists[0].Entries.Select(entry => entry.Id).ShouldBe(cloud.Setlists[0].Entries.Select(entry => entry.Id));
		result.Conflicts.Single().Unit.ShouldBe(SyncConflictUnit.WholeOrderedSetlist);
		result.Conflicts.Single().Winner.ShouldBe(SyncSide.Cloud);
	}

	[TestMethod]
	public void AmbiguousClocksChooseTheSameEntityWhenSidesAreReversed()
	{
		ChordDatabase local = CreateBook(), cloud = Copy(local);
		BookMergeBase baseline = BookSyncMerger.CreateBase(local);
		local.Songs[0].Title = "Left";
		cloud.Songs[0].Title = "Right";
		local.Songs[0].Revision.ModifiedUtc = Now.AddDays(10);
		cloud.Songs[0].Revision.ModifiedUtc = Now;
		Merge(local, cloud, baseline).Database.Songs.Select(song => song.Title)
			.ShouldBe(Merge(cloud, local, baseline).Database.Songs.Select(song => song.Title));
	}

	[TestMethod]
	public void DivergentContentPreservesLoserAsArchivedRecoveryWithStableIdentity()
	{
		ChordDatabase local = CreateBook(), cloud = Copy(local);
		BookMergeBase baseline = BookSyncMerger.CreateBase(local);
		local.SongFiles[0].ContentHash = new string('A', 64);
		cloud.SongFiles[0].ContentHash = new string('B', 64);
		local.SongFiles[0].Revision.ModifiedUtc = Now;
		cloud.SongFiles[0].Revision.ModifiedUtc = Now.AddMinutes(-10);
		BookMergeResult result = Merge(local, cloud, baseline);
		SongFile recovery = result.Database.SongFiles.Single(file => file.IsArchived);
		recovery.ContentHash.ShouldBe(cloud.SongFiles[0].ContentHash);
		recovery.SongId.ShouldBe(local.Songs[0].Id);
		recovery.RecoveryVersion!.WinningFileId.ShouldBe(local.SongFiles[0].Id);
		result.Recoveries.Single().Side.ShouldBe(SyncSide.Cloud);
		result.Database.SongFiles.Single(file => !file.IsArchived).Id.ShouldBe(local.SongFiles[0].Id);
		Merge(cloud, local, baseline).Database.SongFiles.Single(file => file.IsArchived).Id.ShouldBe(recovery.Id);
		DatabaseValidation.Validate(result.Database).ShouldBeEmpty();
	}

	[TestMethod]
	public void UnilateralContentChangeDoesNotCreateARecoveryOfTheUnchangedBase()
	{
		ChordDatabase local = CreateBook(), cloud = Copy(local);
		BookMergeBase baseline = BookSyncMerger.CreateBase(local);
		cloud.SongFiles[0].ContentHash = new string('C', 64);
		BookMergeResult result = Merge(local, cloud, baseline);
		result.Recoveries.ShouldBeEmpty();
		result.Database.SongFiles.Single().ContentHash.ShouldBe(cloud.SongFiles[0].ContentHash);
	}

	[TestMethod]
	[DataRow(SyncDirection.UpdateCloud, SyncSide.Local)]
	[DataRow(SyncDirection.UpdateThisDevice, SyncSide.Cloud)]
	public void DirectionalReplacementPreservesDivergentContentAndReportsMetadata(SyncDirection direction, SyncSide winner)
	{
		ChordDatabase local = CreateBook(), cloud = Copy(local);
		BookMergeBase baseline = BookSyncMerger.CreateBase(local);
		local.SongFiles[0].ContentHash = new string('A', 64);
		cloud.SongFiles[0].ContentHash = new string('B', 64);
		local.Songs[0].Title = "Left";
		cloud.Songs[0].Title = "Right";
		BookMergeResult result = BookSyncMerger.Merge(local, cloud, baseline, direction, Device, Now);
		result.Conflicts.Count.ShouldBe(2);
		result.Conflicts.All(conflict => conflict.Winner == winner).ShouldBeTrue();
		result.Recoveries.Count.ShouldBe(1);
	}

	[TestMethod]
	public void TombstoneRemovesAnUnchangedEntityAndIsRetained()
	{
		ChordDatabase local = CreateBook(), cloud = Copy(local);
		BookMergeBase baseline = BookSyncMerger.CreateBase(local);
		DeleteSheet(cloud);
		BookMergeResult result = Merge(local, cloud, baseline);
		result.Database.SongFiles.ShouldBeEmpty();
		result.Database.Tombstones.Count.ShouldBe(1);
	}

	[TestMethod]
	public void DeletionCannotSilentlyDiscardAnEditOrAnUnknownBase()
	{
		ChordDatabase local = CreateBook(), cloud = Copy(local);
		BookMergeBase baseline = BookSyncMerger.CreateBase(local);
		DeleteSheet(cloud);
		local.SongFiles[0].ContentHash = new string('E', 64);
		Should.Throw<SyncMergeException>(() => Merge(local, cloud, baseline));
		Should.Throw<SyncMergeException>(() => Merge(local, cloud, null));
	}

	[TestMethod]
	public void DisappearanceWithoutATombstoneAndCrossBookBasesAreRejected()
	{
		ChordDatabase local = CreateBook(), cloud = Copy(local);
		BookMergeBase baseline = BookSyncMerger.CreateBase(local);
		cloud.SongFiles.Clear();
		Should.Throw<SyncMergeException>(() => Merge(local, cloud, baseline));
		baseline.BookId = Guid.NewGuid();
		Should.Throw<SyncMergeException>(() => Merge(local, cloud, baseline));
	}

	[TestMethod]
	public void DeviceObservationsAndRevisionOnlyChangesDoNotCreateConflicts()
	{
		ChordDatabase local = CreateBook(), cloud = Copy(local);
		BookMergeBase baseline = BookSyncMerger.CreateBase(local);
		cloud.SongFiles[0].ObservedWriteUtc = Now;
		cloud.SongFiles[0].ObservedLength = 200;
		cloud.SongFiles[0].ContentRevision++;
		cloud.SongFiles[0].AnalysisVersion++;
		cloud.SongFiles[0].Revision.ModifiedUtc = Now;
		BookSyncMerger.CreateBase(cloud).Entities.ShouldBe(baseline.Entities);
		Merge(local, cloud, baseline).Conflicts.ShouldBeEmpty();
	}

	[TestMethod]
	public void HexadecimalHashCasingDoesNotCauseAContentConflict()
	{
		ChordDatabase local = CreateBook();
		local.SongFiles[0].ContentHash = new string('a', 64);
		ChordDatabase cloud = Copy(local);
		BookMergeBase baseline = BookSyncMerger.CreateBase(local);
		cloud.SongFiles[0].ContentHash = cloud.SongFiles[0].ContentHash.ToUpperInvariant();
		BookMergeResult result = Merge(local, cloud, baseline);
		result.Conflicts.ShouldBeEmpty();
		result.Recoveries.ShouldBeEmpty();
	}

	[TestMethod]
	public void LargeCatalogComparisonDoesNotRequireAssetAccess()
	{
		ChordDatabase local = CreateBook();
		for (int index = 0; index < 10000; index++)
		{
			local.Songs.Add(new() { Id = Guid.NewGuid(), Title = "Song " + index });
		}

		ChordDatabase cloud = Copy(local);
		BookMergeBase baseline = BookSyncMerger.CreateBase(local);
		cloud.Songs[^1].Title = "Updated";
		Stopwatch timer = Stopwatch.StartNew();
		BookMergeResult result = Merge(local, cloud, baseline);
		this.TestContext.WriteLine($"Compared {local.Songs.Count:N0} songs in {timer.ElapsedMilliseconds:N0} ms without an asset store.");
		result.Database.Songs.Single(song => song.Id == cloud.Songs[^1].Id).Title.ShouldBe("Updated");
		result.Conflicts.ShouldBeEmpty();
	}

	#endregion

	#region Private Methods

	private static BookMergeResult Merge(ChordDatabase local, ChordDatabase cloud, BookMergeBase? baseline)
		=> BookSyncMerger.Merge(local, cloud, baseline, SyncDirection.TwoWay, Device, Now);

	private static ChordDatabase Copy(ChordDatabase value)
		=> JsonSerializer.Deserialize<ChordDatabase>(JsonSerializer.Serialize(value))!;

	private static ChordDatabase CreateBook()
	{
		ChordDatabase result = ChordDatabase.Create("Test", Device, Now);
		result.Songs = [new() { Id = Guid.NewGuid(), Title = "One" }, new() { Id = Guid.NewGuid(), Title = "Two" }];
		Guid id = Guid.NewGuid();
		result.SongFiles.Add(new()
		{
			Id = id,
			SongId = result.Songs[0].Id,
			RelativePath = PortableManagedFileName.Create("One", id, ".txt"),
			ContentHash = new string('0', 64),
			MediaKind = MediaKind.Text,
		});
		return result;
	}

	private static void DeleteSheet(ChordDatabase database)
	{
		database.Tombstones.Add(new() { EntityId = database.SongFiles[0].Id, EntityType = nameof(SongFile) });
		database.SongFiles.Clear();
	}

	#endregion
}
