#region Using Directives

using System.IO;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class SongViewDateTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public void AccessDatesSurviveReloadRepeatedVisitsAndRecentListEvictionAndArePrunedToBook()
	{
		DateTimeOffset now = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
		Guid first = Guid.NewGuid();
		RecentSongHistory history = new([first]);
		history.Record(first, now);
		history.MarkSaved();
		history.Record(first, now.AddMinutes(1));
		history.IsDirty.ShouldBeTrue();
		history.AccessedUtc[first].ShouldBe(now.AddMinutes(1));
		history.Record(Enumerable.Range(0, 125).Select(_ => Guid.NewGuid()));
		history.SongIds.ShouldNotContain(first);
		history.AccessedUtc.ShouldContainKey(first);
		RecentSongHistory restored = new(history.SongIds, history.AccessedUtc);
		restored.AccessedUtc[first].ShouldBe(now.AddMinutes(1));
		restored.IsDirty.ShouldBeFalse();
		restored.PruneAccessTimes([first]);
		restored.AccessedUtc.Keys.ShouldBe([first]);
		restored.IsDirty.ShouldBeTrue();
		new SongViewSort("created", true).IsValid.ShouldBeTrue();
		new SongViewSort("modified").IsValid.ShouldBeTrue();
		new SongViewSort("accessed").IsValid.ShouldBeTrue();
		new SongViewSort().IsValid.ShouldBeTrue();
		new SongViewSort("unknown").IsValid.ShouldBeFalse();
	}

	[TestMethod]
	[DataRow("created")]
	[DataRow("modified")]
	[DataRow("accessed")]
	public async Task SavedDateSortRoundTripsAndSortingDoesNotOpenAssetsOrWriteCatalog(string field)
	{
		var token = this.TestContext.CancellationToken;
		string root = Path.Combine(Path.GetTempPath(), "ChordViewDates-" + Guid.NewGuid().ToString("N"));
		try
		{
			Guid device = Guid.NewGuid();
			using FileSystemBookStore store = new(root);
			BookLocation book = await store.CreateBookAsync("Date sorting", device, token);
			BookApplicationSession app = new();
			await app.ActivateAsync(store, book, token);
			Guid song = await app.CreateSongAsync("Song", [], [], "[C]Song", device, token);
			DateTimeOffset? created = SongDates.CreatedUtc(song);
			SongEditDocument edit = await app.GetSongEditAsync(song, token);
			await app.SaveSongEditAsync(edit, new(edit.Title, edit.Artists, edit.Tags), edit.Text + " changed", device, token);
			SongDates.CreatedUtc(song).ShouldBe(created);
			await app.SaveCustomTabAsync(null, "Dates", string.Empty, null, field, true, device, token);
			await app.ReloadAsync(token);
			CustomTabCatalogItem tab = app.GetCustomTabs().Single();
			tab.SortBy.ShouldBe(field);
			tab.SortDescending.ShouldBeTrue();
			tab.IsSupported.ShouldBeTrue();
			string before = await store.ReadDatabaseJsonAsync(book, token);
			List<FileStream> locked = [];
			try
			{
				foreach (SongFile file in app.Database!.SongFiles)
				{
					locked.Add(File.Open(Path.Combine(store.GetDirectory(book), file.RelativePath), FileMode.Open, FileAccess.Read, FileShare.None));
				}

				app.SortSongs([song], field, true, new Dictionary<Guid, DateTimeOffset> { [song] = DateTimeOffset.UtcNow }).ShouldBe([song]);
				(await store.ReadDatabaseJsonAsync(book, token)).ShouldBe(before);
			}
			finally
			{
				foreach (FileStream file in locked)
				{
					file.Dispose();
				}
			}
		}
		finally
		{
			if (Directory.Exists(root))
			{
				Directory.Delete(root, recursive: true);
			}
		}
	}
	#endregion
}
