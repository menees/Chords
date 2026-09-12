#region Using Directives

using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class SetlistEntrySettingsTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task RepeatedEntriesHaveIndependentProjectionAndStaleSettingsAreRejected()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Entries", device, token);
		BookApplicationSession session = new();
		await session.ActivateAsync(store, location, token);
		Guid song = await session.CreateSongAsync("Song", [], [], "{key: C}\r\n[C]Words", device, token);
		Guid other = await session.CreateSongAsync("Other", [], [], "[G]Other", device, token);
		Guid file = session.GetSongFiles(song).Single().Id;
		Guid foreignFile = session.GetSongFiles(other).Single().Id;
		Guid list = await session.CreateSetlistAsync("Set", device, token);
		Guid first = await session.AddSongToSetlistAsync(list, song, device, token);
		Guid second = await session.AddSongToSetlistAsync(list, song, device, token);
		SetlistEntrySettings original = session.GetSetlistEntrySettings(list, second);
		SongCatalogItem catalog = session.Search("title:song").Single();
		await session.SaveSetlistEntrySettingsAsync(original, file, 2, device, token);
		session.Search("title:song").Single().ShouldBeSameAs(catalog);
		BookSongPresentation normal = await session.GetPresentationAsync(song, list, first, token);
		BookSongPresentation shifted = await session.GetPresentationAsync(song, list, second, token);
		normal.Html!.ShouldContain(">C</span>");
		shifted.Html!.ShouldContain(">D</span>");
		session.GetSetlistEntries(list)[1].DisplayText.ShouldContain("Transpose +2");
		await Should.ThrowAsync<InvalidOperationException>(() => session.SaveSetlistEntrySettingsAsync(original, null, 0, device, token));
		SetlistEntrySettings current = session.GetSetlistEntrySettings(list, second);
		await Should.ThrowAsync<ArgumentException>(() => session.SaveSetlistEntrySettingsAsync(current, foreignFile, 0, device, token));
		await session.ReloadAsync(token);
		session.GetSetlistEntrySettings(list, second).TransposeSemitones.ShouldBe(2);
		(await session.GetSongEditAsync(song, token)).Text.ShouldBe("{key: C}\r\n[C]Words");
	}

	#endregion
}
