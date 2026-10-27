namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class RecentImportHistoryTests
{
	[TestMethod]
	public void CompletedBatchPromotesExistingSongsDeduplicatesSheetsAndLeavesUnrelatedOrderIntact()
	{
		Guid previous = Guid.NewGuid();
		Guid existing = Guid.NewGuid();
		Guid older = Guid.NewGuid();
		Guid imported = Guid.NewGuid();
		RecentSongHistory history = new([previous, existing, older]);

		history.Record([existing, imported, existing, Guid.Empty]);

		history.SongIds.ShouldBe([existing, imported, previous, older]);
		history.IsDirty.ShouldBeTrue();
		new RecentSongHistory(history.SongIds).SongIds.ShouldBe(history.SongIds);
		history.MarkSaved();
		history.Record([existing, existing]);
		history.IsDirty.ShouldBeFalse();
		history.Record((IEnumerable<Guid>)[]);
		history.IsDirty.ShouldBeFalse();
		history.SongIds.ShouldBe([existing, imported, previous, older]);
	}

	[TestMethod]
	public void LargeImportBatchKeepsOnlyTheMostRecentHundredSongs()
	{
		Guid previous = Guid.NewGuid();
		RecentSongHistory history = new([previous]);
		Guid[] imported = [.. Enumerable.Range(0, 125).Select(_ => Guid.NewGuid())];

		history.Record(imported);

		history.SongIds.ShouldBe(imported.Reverse().Take(100));
		history.SongIds.ShouldNotContain(previous);
		history.IsDirty.ShouldBeTrue();
	}
}
