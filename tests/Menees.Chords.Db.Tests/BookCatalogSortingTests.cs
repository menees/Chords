#region Using Directives

using System.Diagnostics;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class BookCatalogSortingTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	[DataRow("tempo")]
	[DataRow("year")]
	[DataRow("capo")]
	public void NumericSortUsesEffectiveValuesAndKeepsMissingLastInBothDirections(string field)
	{
		ChordDatabase database = ChordDatabase.Create("Sorting", Guid.NewGuid());
		Song first = new() { Id = Guid.NewGuid(), Title = "Zulu" };
		Song second = new() { Id = Guid.NewGuid(), Title = "Alpha" };
		Song third = new() { Id = Guid.NewGuid(), Title = "Third" };
		Song missing = new() { Id = Guid.NewGuid(), Title = "Missing" };
		first.SourceMetadata[field] = [new() { Value = "999" }];
		first.MetadataOverrides[field] = ["100", "002.00", "2"];
		second.MetadataOverrides[field] = ["20"];
		third.MetadataOverrides[field] = ["3"];
		missing.MetadataOverrides[field] = [];
		database.Songs.AddRange([first, second, third, missing]);
		BookGroupingIndex index = new(database);
		Guid[] ids = [missing.Id, second.Id, first.Id, third.Id, first.Id];
		index.Sort(ids, field).ShouldBe([first.Id, third.Id, second.Id, missing.Id]);
		index.Sort(ids, field, descending: true).ShouldBe([second.Id, third.Id, first.Id, missing.Id]);
		index.Group(index.Sort(ids, field, descending: true), field).Single(group => group.Name == "2").SongIds.ShouldBe([first.Id]);
		first.MetadataOverrides[field] = ["30"];
		index.Sort(ids, field)[0].ShouldBe(first.Id);
		index.RefreshSong(first);
		index.Sort(ids, field).ShouldBe([third.Id, second.Id, first.Id, missing.Id]);
		index.Sort([first.Id, second.Id], field).ShouldBe([second.Id, first.Id]);
	}

	[TestMethod]
	public void TextSortUsesLowestNormalizedValueStableTitleAndIdentityTiesAndDistinctSelection()
	{
		ChordDatabase database = ChordDatabase.Create("Text sorting", Guid.NewGuid());
		Song first = new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Title = "Alpha", Artists = ["Zulu", " Éagles "] };
		Song second = new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Title = "Alpha", Artists = ["eagles"] };
		Song third = new() { Id = Guid.NewGuid(), Title = "Third", Artists = ["Other"] };
		Song missing = new() { Id = Guid.NewGuid(), Title = "Missing", Artists = [" "] };
		database.Songs.AddRange([first, second, third, missing]);
		BookGroupingIndex index = new(database);
		Guid[] ids = [missing.Id, third.Id, second.Id, first.Id, first.Id];
		index.Sort(ids, "artist").ShouldBe([first.Id, second.Id, third.Id, missing.Id]);
		index.Sort(ids, "artist", descending: true).ShouldBe([third.Id, first.Id, second.Id, missing.Id]);
		index.Sort(ids, "title", descending: true).ShouldBe([third.Id, missing.Id, first.Id, second.Id]);
		index.Sort([], "title").ShouldBeEmpty();
		Should.Throw<ArgumentException>(() => index.Sort(ids, "future-field"));
	}

	[TestMethod]
	public void TenThousandSongsReuseUnchangedFieldValuesAndKeepGroupedMemberOrder()
	{
		ChordDatabase database = ChordDatabase.Create("Sort scale", Guid.NewGuid());
		const int Count = 10000;
		for (int number = Count - 1; number >= 0; number--)
		{
			Song song = new() { Id = Guid.NewGuid(), Title = $"Song {number:D5}", Tags = ["Practice", "Favorites"] };
			song.MetadataOverrides["tempo"] = [((number % 300) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)];
			database.Songs.Add(song);
		}

		BookGroupingIndex index = new(database);
		Guid[] ids = [.. database.Songs.Select(song => song.Id)];
		Stopwatch timer = Stopwatch.StartNew();
		IReadOnlyList<Guid> cold = index.Sort(ids, "tempo");
		long coldTime = timer.ElapsedMilliseconds;
		long allocated = GC.GetAllocatedBytesForCurrentThread();
		timer.Restart();
		IReadOnlyList<Guid> warm = index.Sort(ids, "tempo", descending: true);
		long warmTime = timer.ElapsedMilliseconds;
		allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
		cold.Count.ShouldBe(Count);
		warm.Count.ShouldBe(Count);
		index.Group(warm, "tag").ShouldAllBe(group => group.SongIds.SequenceEqual(warm));

		// Deliberate unannounced edits expose any unnecessary re-reading of cached values.
		database.Songs[0].MetadataOverrides["tempo"] = ["0"];
		index.Sort(ids, "tempo").ShouldBe(cold);
		index.RefreshSong(database.Songs[0]);
		index.Sort(ids, "tempo")[0].ShouldBe(ids[0]);
		this.TestContext.WriteLine($"10,000-song sort: {coldTime} ms cold / {warmTime} ms warm; {allocated:N0} warm-thread bytes.");
	}

	#endregion
}
