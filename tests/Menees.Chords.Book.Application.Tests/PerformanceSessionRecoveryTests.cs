#region Using Directives

using System.Text.Json;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class PerformanceSessionRecoveryTests
{
	#region Public Methods

	[TestMethod]
	public void TenThousandSongRecoveryKeepsTheFinalCursorAndSerializedContextWithinTheReadLimit()
	{
		ChordDatabase database = new() { Id = Guid.NewGuid() };
		List<Guid> ids = new(PerformanceSessionRecovery.MaximumSongs);
		for (int index = 0; index < PerformanceSessionRecovery.MaximumSongs; index++)
		{
			Guid id = Guid.NewGuid();
			ids.Add(id);
			database.Songs.Add(new() { Id = id });
			database.SongFiles.Add(new() { Id = Guid.NewGuid(), SongId = id });
		}

		PerformanceSessionContext context = new(Guid.NewGuid(), database.Id, null, ids, null);
		PerformanceSessionCursor cursor = new(context.Id, ids[^1], null);
		PerformanceResume resume = PerformanceSessionRecovery.Resolve(database, context, cursor)!;
		resume.SongIds.Count.ShouldBe(PerformanceSessionRecovery.MaximumSongs);
		resume.Index.ShouldBe(PerformanceSessionRecovery.MaximumSongs - 1);
		JsonSerializer.SerializeToUtf8Bytes(context).Length.ShouldBeLessThan(PerformanceSessionRecovery.MaximumJsonCharacters);
	}

	[TestMethod]
	public void LibraryResumePreservesSavedOrderAndFiltersUnavailableSongsWithoutChoosingAnotherSong()
	{
		var (database, first, second, third) = Create();
		PerformanceSessionContext context = new(Guid.NewGuid(), database.Id, null, [third, second, first, third], "Saved search");
		PerformanceSessionCursor cursor = new(context.Id, second, null);
		PerformanceResume resume = PerformanceSessionRecovery.Resolve(database, context, cursor)!;
		resume.SongIds.ShouldBe([third, second, first]);
		resume.Index.ShouldBe(1);
		database.Songs.Single(song => song.Id == third).IsArchived = true;
		resume = PerformanceSessionRecovery.Resolve(database, context, cursor)!;
		resume.SongIds.ShouldBe([second, first]);
		resume.Index.ShouldBe(0);
		database.SongFiles.Single(file => file.SongId == second).IsArchived = true;
		PerformanceSessionRecovery.Resolve(database, context, cursor).ShouldBeNull();
	}

	[TestMethod]
	public void SetlistResumeRetainsExactRepeatedEntryAfterReorderAndUsesCurrentName()
	{
		var (database, first, second, _) = Create();
		SetlistEntry repeat = new() { Id = Guid.NewGuid(), SongId = first };
		Setlist setlist = new()
		{
			Id = Guid.NewGuid(), Name = "Renamed set",
			Entries = [new() { Id = Guid.NewGuid(), SongId = first }, new() { Id = Guid.NewGuid(), SongId = second }, repeat],
		};
		database.Setlists.Add(setlist);
		PerformanceSessionContext context = new(Guid.NewGuid(), database.Id, setlist.Id, [first, second, first], "Old name");
		PerformanceSessionCursor cursor = new(context.Id, first, repeat.Id);
		PerformanceResume resume = PerformanceSessionRecovery.Resolve(database, context, cursor)!;
		resume.Index.ShouldBe(2);
		resume.Name.ShouldBe("Renamed set");
		setlist.Entries.Remove(repeat);
		setlist.Entries.Insert(0, repeat);
		resume = PerformanceSessionRecovery.Resolve(database, context, cursor)!;
		resume.Index.ShouldBe(0);
		resume.EntryIds[0].ShouldBe(repeat.Id);
		setlist.Entries.Remove(repeat);
		PerformanceSessionRecovery.Resolve(database, context, cursor).ShouldBeNull();
	}

	[TestMethod]
	public void InterruptedWritesForeignBooksAndArchivedSetlistsCannotResume()
	{
		var (database, first, _, _) = Create();
		PerformanceSessionContext context = new(Guid.NewGuid(), database.Id, null, [first], null);
		PerformanceSessionCursor cursor = new(context.Id, first, null);
		PerformanceSessionRecovery.Resolve(database, context, cursor with { ContextId = Guid.NewGuid() }).ShouldBeNull();
		PerformanceSessionRecovery.Resolve(database, context with { BookId = Guid.NewGuid() }, cursor).ShouldBeNull();
		PerformanceSessionRecovery.Resolve(database, context with { SongIds = null! }, cursor).ShouldBeNull();
		PerformanceSessionRecovery.Resolve(database, context with { SongIds = new Guid[PerformanceSessionRecovery.MaximumSongs + 1] }, cursor).ShouldBeNull();
		SetlistEntry entry = new() { Id = Guid.NewGuid(), SongId = first };
		Setlist setlist = new() { Id = Guid.NewGuid(), IsArchived = true, Entries = [entry] };
		database.Setlists.Add(setlist);
		PerformanceSessionRecovery.Resolve(database, context with { SetlistId = setlist.Id }, cursor with { EntryId = entry.Id }).ShouldBeNull();
	}

	[TestMethod]
	public void JsonRoundTripKeepsTheCursorSeparateFromTheLargeContext()
	{
		var (database, first, second, third) = Create();
		PerformanceSessionContext context = new(Guid.NewGuid(), database.Id, null, [third, second, first], null);
		PerformanceSessionCursor cursor = new(context.Id, second, null);
		string contextJson = JsonSerializer.Serialize(context);
		string cursorJson = JsonSerializer.Serialize(cursor);
		PerformanceResume resume = PerformanceSessionRecovery.Resolve(
			database, JsonSerializer.Deserialize<PerformanceSessionContext>(contextJson), JsonSerializer.Deserialize<PerformanceSessionCursor>(cursorJson))!;
		resume.Index.ShouldBe(1);
		cursorJson.ShouldNotContain("SongIds");
		cursorJson.Length.ShouldBeLessThan(200);
	}

	#endregion

	#region Private Methods

	private static (ChordDatabase Database, Guid First, Guid Second, Guid Third) Create()
	{
		ChordDatabase database = new() { Id = Guid.NewGuid() };
		Guid[] ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
		foreach (Guid id in ids)
		{
			database.Songs.Add(new() { Id = id });
			database.SongFiles.Add(new() { Id = Guid.NewGuid(), SongId = id });
		}

		return (database, ids[0], ids[1], ids[2]);
	}

	#endregion
}
