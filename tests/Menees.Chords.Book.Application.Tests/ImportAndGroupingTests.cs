#region Using Directives

using System.IO;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class ImportAndGroupingTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task EveryKnownGroupingSurvivesSaveReloadAndMetadataEditInvalidatesOnlyItsSong()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Views", device, token);
		BookApplicationSession session = new();
		await session.ActivateAsync(store, location, token);
		Guid first = await session.CreateSongAsync("First", ["Artist"], ["Practice", "Favorites"], "{key: C}\n[C]First", device, token);
		Guid second = await session.CreateSongAsync("Second", [], ["Practice"], "[C]Second", device, token);
		foreach ((string field, string caption) in BookGroupingIndex.Fields)
		{
			await session.SaveCustomTabAsync(null, caption, "tag:practice", field, device, token);
		}

		await session.ReloadAsync(token);
		session.GetCustomTabs().Count.ShouldBe(BookGroupingIndex.Fields.Count);
		session.GetCustomTabs().ShouldAllBe(tab => tab.IsSupported);
		IReadOnlyList<Guid> ids = [.. session.Search("tag:practice").Select(song => song.Id)];
		ids.ShouldBe([first, second]);
		session.GroupSongs(ids, "tag").Single(group => group.Name == "Practice").SongIds.ShouldBe(ids);
		session.GroupSongs([first], "key").Single().Name.ShouldBe("C");
		SongCatalogItem unrelated = session.Search("title:Second").Single();
		SongEditDocument edit = await session.GetSongEditAsync(first, token);
		SongEditMetadata metadata = new("First", ["Artist"], ["Changed"])
		{
			Scalars = new Dictionary<string, IReadOnlyList<string>>(edit.Metadata) { ["key"] = ["Db"] },
		};
		await session.SaveSongEditAsync(edit, metadata, edit.Text, device, token);
		session.Search("title:Second").Single().ShouldBeSameAs(unrelated);
		session.GroupSongs([first], "tag").Single().Name.ShouldBe("Changed");
		session.GroupSongs([first], "key").Single().Name.ShouldBe("Db");
		session.GroupSongs([second], "tag").Single().Name.ShouldBe("Practice");
	}

	[TestMethod]
	public async Task IncomingComparisonIsReadOnlyEncodesSourceAndApplyRetainsTwoComparableSheets()
	{
		var token = this.TestContext.CancellationToken;
		string directory = Path.Combine(Path.GetTempPath(), "ChordImportComparison-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		try
		{
			Guid device = Guid.NewGuid();
			InMemoryBookStore store = new();
			BookLocation location = await store.CreateBookAsync("Comparison", device, token);
			BookApplicationSession session = new();
			await session.ActivateAsync(store, location, token);
			BookImportResult existing = await BookImportService.CreateSongAsync(
				store, location, "Song", ["Artist"], [], "{title: Song}\n{artist: Artist}\n[C]Old", device, token);
			Guid song = existing.SongId;
			session.Search(null).ShouldBeEmpty(); // The review must use its captured catalog, even before the session reloads external additions.
			string path = Path.Combine(directory, "changed.cho");
			const string Incoming = "{title: Song}\n{artist: Artist}\n[D]<script>new</script>";
			await File.WriteAllTextAsync(path, Incoming, token);
			using BookImportReview review = await session.ReviewImportAsync([path], token);
			BookImportItem item = review.Items.Single();
			string before = await store.ReadDatabaseJsonAsync(location, token);
			SongVersionComparison comparison = await session.CompareImportAsync(review, item.Id, item.Candidates.Single().SheetId, token);
			comparison.CanResolve.ShouldBeFalse();
			comparison.Left.Text!.ShouldContain("[C]Old");
			comparison.Right.Text.ShouldBe(Incoming);
			string html = SongVersionComparisonHtml.Create(comparison, token);
			html.ShouldContain("&lt;script&gt;");
			html.ShouldNotContain("<script>");
			await Should.ThrowAsync<ArgumentException>(() => session.ResolveSongVersionsAsync(comparison, SongVersionChoice.KeepRight, device, token));
			(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(before);
			IReadOnlyList<BookImportResult> added = await session.ApplyImportAsync(review, [new(item.Id, BookImportAction.AddSheet, song)], device, token);
			session.Search(null).Count.ShouldBe(1);
			session.GetSongFiles(song).Count.ShouldBe(2);
			SongVersionComparison retained = await session.CompareSongVersionsAsync(song, comparison.Left.Id, added.Single().SongFileId, token);
			retained.Right.Text.ShouldBe(Incoming);
			retained.CanResolve.ShouldBeTrue();
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	#endregion
}