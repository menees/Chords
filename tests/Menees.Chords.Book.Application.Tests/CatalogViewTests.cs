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
		session.GetCustomTabs().Single().IsSupported.ShouldBeFalse();
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
