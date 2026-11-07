#region Using Directives

using System.Diagnostics;
using System.Globalization;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class SongDateSortingTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public void CreationUsesStableVersionSevenTimestampAndLeavesUnknownDatesMissing()
	{
		DateTimeOffset created = new(2020, 3, 4, 8, 9, 10, TimeSpan.FromHours(-6));
		Guid id = Guid.CreateVersion7(created);
		SongDates.CreatedUtc(id).ShouldBe(created.ToUniversalTime());
		SongDates.CreatedUtc(Guid.NewGuid()).ShouldBeNull();
		SongDates.CreatedUtc(Guid.Empty).ShouldBeNull();
		SongDates.CreatedUtc(Guid.Parse("ffffffff-ffff-7fff-bfff-ffffffffffff")).ShouldBeNull();
		BookGroupingIndex.SortFields.Keys.ShouldContain("created");
		BookGroupingIndex.Fields.Keys.ShouldNotContain("created");
	}

	[TestMethod]
	[DataRow("created")]
	[DataRow("modified")]
	[DataRow("accessed")]
	public void DatesSortChronologicallyWithMissingLastAndStableTitleTies(string field)
	{
		DateTimeOffset older = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
		DateTimeOffset newer = older.AddDays(1);
		Song zulu = new() { Id = Guid.CreateVersion7(older), Title = "Zulu", LastAccessedUtc = older, Revision = new() { ModifiedUtc = older } };
		Song alpha = new() { Id = Guid.CreateVersion7(newer), Title = "Alpha", LastAccessedUtc = newer, Revision = new() { ModifiedUtc = newer } };
		Song tie = new() { Id = Guid.CreateVersion7(older), Title = "Beta", LastAccessedUtc = older, Revision = new() { ModifiedUtc = older } };
		Song missing = new() { Id = Guid.NewGuid(), Title = "A missing" };
		ChordDatabase database = ChordDatabase.Create("Dates", Guid.NewGuid());
		database.Songs.AddRange([zulu, missing, alpha, tie]);
		BookGroupingIndex index = new(database);
		Guid[] ids = [zulu.Id, missing.Id, alpha.Id, tie.Id, zulu.Id];
		index.Sort(ids, field).ShouldBe([tie.Id, zulu.Id, alpha.Id, missing.Id]);
		index.Sort(ids, field, descending: true).ShouldBe([alpha.Id, tie.Id, zulu.Id, missing.Id]);
		index.Sort([alpha.Id, zulu.Id], field).ShouldBe([zulu.Id, alpha.Id]);
	}

	[TestMethod]
	public void DateOrderingIgnoresLocalOffsetsAndReadsCurrentStampsAndAccessOverrides()
	{
		DateTimeOffset now = new(2020, 2, 1, 0, 0, 0, TimeSpan.Zero);
		Song first = new() { Id = Guid.NewGuid(), Title = "First", LastAccessedUtc = now, Revision = new() { ModifiedUtc = now } };
		Song second = new() { Id = Guid.NewGuid(), Title = "Second", LastAccessedUtc = now.AddHours(1).ToOffset(TimeSpan.FromHours(-6)) };
		ChordDatabase database = ChordDatabase.Create("Offsets", Guid.NewGuid());
		database.Songs.AddRange([first, second]);
		BookGroupingIndex index = new(database);
		Guid[] ids = [first.Id, second.Id];
		index.Sort(ids, "accessed").ShouldBe(ids);
		Dictionary<Guid, DateTimeOffset> local = new() { [first.Id] = now.AddDays(1) };
		index.Sort(ids, "accessed", lastAccessedUtc: local).ShouldBe([second.Id, first.Id]);
		local[second.Id] = now.AddDays(2);
		index.Sort(ids, "accessed", lastAccessedUtc: local).ShouldBe(ids);
		second.Revision.ModifiedUtc = now.AddDays(-1);
		index.Sort(ids, "modified").ShouldBe([second.Id, first.Id]);
		first.Revision.ModifiedUtc = now.AddDays(-2);
		index.Sort(ids, "modified").ShouldBe(ids);
	}

	[TestMethod]
	public void TenThousandDateSortsUseCatalogOnlyAndRetainAllIdentities()
	{
		const int Count = 10000;
		DateTimeOffset start = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
		ChordDatabase database = ChordDatabase.Create("Date scale", Guid.NewGuid());
		for (int i = 0; i < Count; i++)
		{
			database.Songs.Add(new() { Id = Guid.CreateVersion7(start.AddMinutes(i)), Title = i.ToString(CultureInfo.InvariantCulture) });
		}

		BookGroupingIndex index = new(database);
		Guid[] ids = [.. database.Songs.Select(song => song.Id).Reverse()];
		Stopwatch timer = Stopwatch.StartNew();
		IReadOnlyList<Guid> sorted = index.Sort(ids, "created");
		sorted.ShouldBe(database.Songs.Select(song => song.Id));
		this.TestContext.WriteLine($"10,000-song created-date sort: {timer.ElapsedMilliseconds} ms, no filesystem access.");
	}
	#endregion
}
