#region Using Directives

using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class CatalogViewTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public void RecentHistoryIsBoundedDeduplicatedAndOnlyDirtyWhenItsOrderChanges()
	{
		Guid first = Guid.NewGuid();
		Guid second = Guid.NewGuid();
		RecentSongHistory history = new([Guid.Empty, first, second, first]);
		history.SongIds.ShouldBe([first, second]);
		history.Record(first);
		history.IsDirty.ShouldBeFalse();
		history.Record(second);
		history.SongIds.ShouldBe([second, first]);
		history.IsDirty.ShouldBeTrue();
		history.MarkSaved();
		history.IsDirty.ShouldBeFalse();
		const int Visits = 125;
		for (int index = 0; index < Visits; index++)
		{
			history.Record(Guid.NewGuid());
		}

		history.SongIds.Count.ShouldBe(100);
		history.SongIds.ShouldNotContain(first);
		new RecentSongHistory(history.SongIds).SongIds.ShouldBe(history.SongIds);
	}

	[TestMethod]
	public async Task SavedAlbumAndTimeSearchSurvivesCatalogReload()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Metadata views", device, token);
		BookApplicationSession session = new();
		await session.ActivateAsync(store, location, token);
		Guid song = await session.CreateSongAsync(
			"Song", [], [], "{album: Live Sessions}\n{time: 6/8}\n[C]Song", device, token);
		const string Query = "album:\"live sessions\" time:6/8";
		await session.SaveCustomTabAsync(null, "Live six-eight", Query, "title", device, token);
		await session.ReloadAsync(token);
		CustomTabCatalogItem saved = session.GetCustomTabs().Single();
		saved.Search.ShouldBe(Query);
		saved.IsSupported.ShouldBeTrue();
		session.Search(saved.Search).Single().Id.ShouldBe(song);
		session.Search("time:6").ShouldBeEmpty();
	}

	[TestMethod]
	[DataRow("filter")]
	[DataRow("sort")]
	[DataRow("group")]
	public async Task UnsupportedSavedViewUpdatePreservesDefinition(string unsupported)
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Views", device, token);
		string original = await store.ReadDatabaseJsonAsync(location, token);
		ChordDatabase database = DatabaseJson.Deserialize(original);
		CustomTab tab = new() { Id = Guid.NewGuid(), Name = "Advanced", Filter = new() { Operator = "search", Value = "Song" } };
		if (unsupported == "filter")
		{
			tab.Filter = new() { Operator = "and", Children = [new() { Operator = "eq", Field = "key", Value = "G" }] };
		}
		else if (unsupported == "sort")
		{
			tab.Sort = [new() { Field = "future-field", Descending = true }];
		}
		else
		{
			tab.GroupBy = "unsupported-future-field";
		}

		database.CustomTabs.Add(tab);
		await store.CommitMetadataAsync(location, original, database, token);
		BookApplicationSession session = new();
		await session.ActivateAsync(store, location, token);
		string before = await store.ReadDatabaseJsonAsync(location, token);
		session.GetCustomTabs().Single().IsSupported.ShouldBeFalse();
		await Should.ThrowAsync<InvalidOperationException>(() => session.SaveCustomTabAsync(tab.Id, "Rename", string.Empty, "title", device, token));
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(before);
		DatabaseJson.Serialize(session.Database!).ShouldBe(before);
	}

	[TestMethod]
	public async Task SavedViewsRoundTripAndDeletionRetainsSongsAndRecordsTombstone()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Views", device, token);
		BookApplicationSession session = new();
		await session.ActivateAsync(store, location, token);
		Guid song = await session.CreateSongAsync("Song", ["Artist"], [], "[C]Song", device, token);
		ChordDatabase identity = session.Database!;
		Guid view = await session.SaveCustomTabAsync(null, "Practice", "Song", "artist", device, token);
		session.Database.ShouldBeSameAs(identity);
		await session.ReloadAsync(token);
		CustomTabCatalogItem saved = session.GetCustomTabs().Single();
		saved.ShouldBe(new(view, "Practice", "Song", "artist", true));
		CustomTab definition = session.Database!.CustomTabs.Single();
		definition.Sort[0].Descending = true;
		session.GetCustomTabs().Single().IsSupported.ShouldBeTrue();
		session.GetCustomTabs().Single().SortDescending.ShouldBeTrue();
		definition.Sort.Add(new() { Field = "artist" });
		session.GetCustomTabs().Single().IsSupported.ShouldBeFalse();
		definition.Sort.RemoveAt(1);
		definition.Sort[0].Descending = false;
		definition.Filter!.Children.Add(new() { Operator = "or" });
		session.GetCustomTabs().Single().IsSupported.ShouldBeFalse();
		definition.Filter.Children.Clear();
		await session.SaveCustomTabAsync(view, "Gig", "Artist", "title", device, token);
		session.GetCustomTabs().Single().Name.ShouldBe("Gig");
		await session.DeleteCustomTabAsync(view, device, token);
		await session.ReloadAsync(token);
		session.GetCustomTabs().ShouldBeEmpty();
		session.Database!.Songs.Single().Id.ShouldBe(song);
		session.Database.Tombstones.Single().EntityId.ShouldBe(view);
		DatabaseValidation.Validate(session.Database).ShouldBeEmpty();
	}
	#endregion
}
