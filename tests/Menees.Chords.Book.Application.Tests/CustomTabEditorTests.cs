#region Using Directives

using System.IO;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class CustomTabEditorTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public void NewUsesCurrentSearchAndEditUsesTheSavedDefinitionWithoutChangingIt()
	{
		CustomTabEdit added = CustomTabEdit.Create("artist:Eagles");
		added.Id.ShouldBeNull();
		added.Name.ShouldBeEmpty();
		added.Search.ShouldBe("artist:Eagles");
		added.GroupBy.ShouldBeNull();
		added.SortBy.ShouldBe("title");
		added.SortDescending.ShouldBeFalse();
		(added with { Name = "Eagles", SortBy = "future-field" }).IsValid.ShouldBeFalse();
		added.IsValid.ShouldBeFalse();
		(added with { Name = "Eagles" }).IsValid.ShouldBeTrue();
		(added with { Name = "   " }).IsValid.ShouldBeFalse();
		(added with { Name = "Eagles", GroupBy = "future-field" }).IsValid.ShouldBeFalse();
		CustomTabCatalogItem existing = new(Guid.NewGuid(), "Favorites", "tag:favorite", "genre", true, "tempo", true);
		CustomTabEdit edited = CustomTabEdit.Create("artist:Eagles", existing);
		edited.ShouldBe(new(existing.Id, existing.Name, existing.Search, existing.GroupBy, "tempo", true));
		edited.IsValid.ShouldBeTrue();
		CustomTabEdit ungrouped = edited with { Name = "Eagles", Search = added.Search, GroupBy = null };
		ungrouped.IsValid.ShouldBeTrue();
		existing.Search.ShouldBe("tag:favorite");
		existing.GroupBy.ShouldBe("genre");
		Should.Throw<InvalidOperationException>(() => CustomTabEdit.Create(added.Search, existing with { IsSupported = false }));
	}

	[TestMethod]
	public async Task EditingNameFilterAndOptionalGroupingRetainsIdentityAndDoesNotOpenAssets()
	{
		var token = this.TestContext.CancellationToken;
		string directory = Path.Combine(Path.GetTempPath(), "ChordTabEditor-" + Guid.NewGuid().ToString("N"));
		try
		{
			Guid device = Guid.NewGuid();
			using FileSystemBookStore store = new(directory);
			BookLocation location = await store.CreateBookAsync("Tab editor", device, token);
			BookApplicationSession session = new();
			await session.ActivateAsync(store, location, token);
			Guid eagles = await session.CreateSongAsync("First", ["Eagles"], [], "[C]First", device, token);
			Guid other = await session.CreateSongAsync("Second", ["Other"], [], "[D]Second", device, token);
			SongFile firstFile = session.Database!.SongFiles.Single(file => file.SongId == eagles);
			SongFile secondFile = session.Database.SongFiles.Single(file => file.SongId == other);
			string firstPath = Path.Combine(store.GetDirectory(location), firstFile.RelativePath);
			using FileStream firstLock = new(firstPath, FileMode.Open, FileAccess.Read, FileShare.None);
			string secondPath = Path.Combine(store.GetDirectory(location), secondFile.RelativePath);
			using FileStream secondLock = new(secondPath, FileMode.Open, FileAccess.Read, FileShare.None);
			CustomTabEdit draft = CustomTabEdit.Create("artist:Eagles") with { Name = "Eagles", SortBy = "tempo", SortDescending = true };
			Guid id = await session.SaveCustomTabAsync(draft.Id, draft.Name, draft.Search, draft.GroupBy, draft.SortBy, draft.SortDescending, device, token);
			await session.ReloadAsync(token);
			CustomTabCatalogItem saved = session.GetCustomTabs().Single();
			saved.GroupBy.ShouldBeNull();
			saved.SortBy.ShouldBe("tempo");
			saved.SortDescending.ShouldBeTrue();
			saved.IsSupported.ShouldBeTrue();
			session.Search(saved.Search).Single().Id.ShouldBe(eagles);
			ChordDatabase identity = session.Database!;
			CustomTabEdit edit = CustomTabEdit.Create("temporary search", saved) with { Name = "Others", Search = "artist:Other", GroupBy = "tempo" };
			(await session.SaveCustomTabAsync(edit.Id, edit.Name, edit.Search, edit.GroupBy, edit.SortBy, edit.SortDescending, device, token)).ShouldBe(id);
			session.Database.ShouldBeSameAs(identity);
			await session.ReloadAsync(token);
			saved = session.GetCustomTabs().Single();
			saved.ShouldBe(new(id, "Others", "artist:Other", "tempo", true, "tempo", true));
			session.Search(saved.Search).Single().Id.ShouldBe(other);
			edit = CustomTabEdit.Create(string.Empty, saved) with { Name = "All songs", Search = string.Empty, GroupBy = null };
			await session.SaveCustomTabAsync(edit.Id, edit.Name, edit.Search, edit.GroupBy, edit.SortBy, edit.SortDescending, device, token);
			await session.ReloadAsync(token);
			saved = session.GetCustomTabs().Single();
			saved.GroupBy.ShouldBeNull();
			saved.SortBy.ShouldBe("tempo");
			saved.SortDescending.ShouldBeTrue();
			saved.IsSupported.ShouldBeTrue();
			session.Search(saved.Search).Count.ShouldBe(2);
			session.SortSongs([eagles, other], saved.SortBy, saved.SortDescending).ShouldBe([eagles, other]);
			Song untouched = session.Database!.Songs.Single(song => song.Id == other);
			string beforeInvalid = await store.ReadDatabaseJsonAsync(location, token);
			await Should.ThrowAsync<ArgumentException>(() => session.SaveCustomTabAsync(
				id, "Invalid", string.Empty, null, "future-field", false, device, token));
			(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(beforeInvalid);
			session.Database.Songs.Single(song => song.Id == other).ShouldBeSameAs(untouched);
			session.Database!.SongFiles.Count.ShouldBe(2);
		}
		finally
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}
	}

	#endregion
}