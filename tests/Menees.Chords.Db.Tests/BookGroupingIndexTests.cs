#region Using Directives

using System.Diagnostics;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class BookGroupingIndexTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public void IndividualValuesAreNormalizedDeduplicatedFilteredAndExplicitlyInvalidated()
	{
		ChordDatabase database = ChordDatabase.Create("Grouping", Guid.NewGuid());
		Song first = new() { Id = Guid.NewGuid(), Title = "First", Tags = [" Practice ", "Favorites", "practice"] };
		Song second = new() { Id = Guid.NewGuid(), Title = "Second", Tags = ["Favorites"] };
		Song empty = new() { Id = Guid.NewGuid(), Title = "Empty", Tags = ["  "] };
		database.Songs.AddRange([first, second, empty]);
		first.SourceMetadata["key"] = [new() { Value = "C" }];
		first.MetadataOverrides["key"] = ["Db"];
		first.SourceMetadata["genre"] = [new() { Value = "Folk" }, new() { Value = " folk " }];
		first.SourceMetadata["time"] = [new() { Value = "6/8" }];
		BookGroupingIndex index = new(database);
		IReadOnlyList<BookSongGroup> tags = index.Group([first.Id, second.Id, first.Id, empty.Id], "tag");
		tags.Select(group => group.Name).ShouldBe(["Favorites", "Practice", "Unspecified"]);
		tags[0].SongIds.ShouldBe([first.Id, second.Id]);
		tags[1].SongIds.ShouldBe([first.Id]);
		tags[2].SongIds.ShouldBe([empty.Id]);
		index.Group([second.Id], "tag").Single().SongIds.ShouldBe([second.Id]);
		index.Group([first.Id], "key").Single().Name.ShouldBe("Db");
		index.Group([first.Id], "genre").Single().SongIds.ShouldBe([first.Id]);
		index.Group([first.Id], "time").Single().Name.ShouldBe("6/8");
		first.Tags = ["Changed"];
		index.Group([first.Id], "tag").Count.ShouldBe(2);
		index.RefreshSong(first);
		index.Group([first.Id], "tag").Single().Name.ShouldBe("Changed");
		first.MetadataOverrides["key"] = [];
		index.RefreshSong(first);
		index.Group([first.Id], "key").Single().Name.ShouldBe("Unspecified");
	}

	[TestMethod]
	[DataRow("tempo")]
	[DataRow("year")]
	[DataRow("capo")]
	public void NumericValuesSortNumericallyAndBlankValuesFollow(string field)
	{
		ChordDatabase database = ChordDatabase.Create("Numbers", Guid.NewGuid());
		foreach (string value in new[] { "100", "20", "3", string.Empty, "003", "003.00" })
		{
			Song song = new() { Id = Guid.NewGuid(), Title = value };
			song.MetadataOverrides[field] = [value];
			database.Songs.Add(song);
		}

		IReadOnlyList<BookSongGroup> groups = new BookGroupingIndex(database).Group([.. database.Songs.Select(song => song.Id)], field);
		groups.Select(group => group.Name).ShouldBe(["3", "20", "100", "Unspecified"]);
		groups[0].SongIds.Count.ShouldBe(3);
	}

	[TestMethod]
	public void TenThousandSongGroupingRetainsOneMembershipPerValueOnCachedQueries()
	{
		ChordDatabase database = ChordDatabase.Create("Scale", Guid.NewGuid());
		const int Count = 10000;
		for (int index = 0; index < Count; index++)
		{
			database.Songs.Add(new() { Id = Guid.NewGuid(), Title = $"Song {index}", Tags = ["Practice", "Favorites", "practice"] });
		}

		BookGroupingIndex grouping = new(database);
		Guid[] ids = [.. database.Songs.Select(song => song.Id)];
		Stopwatch timer = Stopwatch.StartNew();
		grouping.Group(ids, "tag").Sum(group => group.SongIds.Count).ShouldBe(Count * 2);
		long cold = timer.ElapsedMilliseconds;
		timer.Restart();
		IReadOnlyList<BookSongGroup> cached = grouping.Group(ids, "tag");
		cached.Count.ShouldBe(2);
		cached.ShouldAllBe(group => group.SongIds.Count == Count);
		this.TestContext.WriteLine($"10,000-song tag grouping: {cold} ms cold, {timer.ElapsedMilliseconds} ms cached; no asset access contract.");
	}
	#endregion
}