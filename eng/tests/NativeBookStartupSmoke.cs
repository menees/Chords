using System.Reflection;
using System.Text;
using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui;
using Menees.Chords.Db;
using static SetlistSmoke.SmokeMauiApp;

namespace SetlistSmoke;

internal static class NativeBookStartupSmoke
{
	internal static async Task RunAsync(Microsoft.Maui.Controls.Window window)
	{
		Program.Trace("Verify book opening and recent books survive rejected legacy metadata");
		string[] keys = ["ChordBook.CurrentBookPath", "ChordBook.RecentBooks"];
		string?[] saved = [.. keys.Select(key => Preferences.Default.Get<string?>(key, null))];
		string root = Path.Combine(AppContext.BaseDirectory, "LegacyStartupBooks");
		Guid device = Guid.NewGuid();
		using FileSystemBookStore store = new(root);
		BookLocation location = await store.CreateBookAsync("Legacy startup acceptance", device);
		BookImportResult bad = await BookImportService.CreateSongAsync(store, location, "Legacy sheet", [], [], "[C]Legacy", device);
		_ = await BookImportService.CreateSongAsync(store, location, "Healthy sheet", [], [], "{tag: Practice}\n[G]Healthy", device);
		string before = await store.ReadDatabaseJsonAsync(location);
		ChordDatabase database = DatabaseJson.Deserialize(before);
		foreach (SongFile file in database.SongFiles) file.AnalysisVersion = SongFileAnalyzer.CurrentAnalysisVersion - 1;
		string directory = store.GetDirectory(location);
		string path = Path.Combine(directory, database.SongFiles.Single(file => file.Id == bad.SongFileId).RelativePath);
		byte[] bytes = Encoding.UTF8.GetBytes("[C]Legacy text \u0001\u0099");
		SongFile legacy = database.SongFiles.Single(file => file.Id == bad.SongFileId);
		legacy.ContentHash = SongFileAnalyzer.Hash(bytes);
		legacy.ObservedLength = bytes.Length;
		await using (IStagedBookWrite write = await store.StageWriteAsync(location))
		{
			await write.WriteManagedAssetAsync(legacy.Id, legacy.RelativePath, new MemoryStream(bytes, false));
			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database));
			await write.CommitAsync();
		}
		using BookSession session = new(new BookApplicationSession(), device);
		using SilentMetronome metronome = new();
		try
		{
			await session.OpenAsync(directory);
			MainPage page = new(session, new NoPicker(), metronome);
			NavigationPage.SetHasNavigationBar(page, false);
			window.Page = new NavigationPage(page);
			await WaitAsync(() => page.FindByName<Label>("Status").Text.StartsWith("Ready."));
			Check(page.FindByName<Label>("BookName").Text == "Legacy startup acceptance", "Rejected source hid the book header");
			Check(session.Database!.Songs.Count == 2, "Rejected source hid catalog songs");
			Check(session.LastMetadataRefresh!.Problems.Count == 1, "Rejected source was not diagnosed");
			Check(page.FindByName<Label>("Status").Text.Contains("skipped 1 sheet"), "Refresh warning missing");
			Check(BookSession.GetRecentBooks().Any(book => book.Path == directory), "Opened book missing from recent books");
			Check(page.FindByName<OpenBookSplitButton>("OpenBookButton").RecentBooks.Any(book => book.Path == directory), "Recent-books menu not populated");
			Check((await File.ReadAllBytesAsync(path)).SequenceEqual(bytes), "Opening rewrote rejected source bytes");
			await CaptureAsync(page, "legacy-book-startup.png");
			string checkedJson = await store.ReadDatabaseJsonAsync(location);
			List<FileStream> locks = [];
			try
			{
				foreach (SongFile file in session.Database.SongFiles)
					locks.Add(new FileStream(Path.Combine(directory, file.RelativePath), FileMode.Open, FileAccess.ReadWrite, FileShare.None));
				using BookSession reopened = new(new BookApplicationSession(), device);
				await reopened.OpenAsync(directory);
				Check(reopened.LastMetadataRefresh!.AnalyzedFileCount == 0, "Repeat opening reanalyzed sheets");
				Check(reopened.LastMetadataRefresh.Problems.Single().Message.Contains("U+0001"), "Repeat open read the locked rejected file");
				Check(await store.ReadDatabaseJsonAsync(location) == checkedJson, "Repeat opening rewrote catalog metadata");
			}
			finally { foreach (var locked in locks) locked.Dispose(); }
			Program.Trace("Repeat book opening with every sheet exclusively locked passed");
			before = await store.ReadDatabaseJsonAsync(location);
			BookIntegrityPage integrity = new(session);
			await page.Navigation.PushAsync(integrity, false);
			Editor output = (Editor)typeof(BookIntegrityPage).GetField("report", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(integrity)!;
			await WaitAsync(() => output.Text?.Contains("Invalid sheet content") == true);
			Check(output.Text.Contains("Song: Legacy sheet") && output.Text.Contains(path), "Skipped-sheet identity/path missing from validation report");
			Check(output.Text.Contains("U+0001") && output.Text.Contains("line 1") && output.Text.Contains("Review Folder Changes"), "Report lacks location/repair steps");
			Check(output.IsReadOnly, "Integrity report must be read-only");
			Check(await store.ReadDatabaseJsonAsync(location) == before, "Report wrote the catalog");
			Check((await File.ReadAllBytesAsync(path)).SequenceEqual(bytes), "Report rewrote source bytes");
			Check(!Elements(integrity).OfType<FluentIconButton>().Any(button => button.Icon == "Save"), "Report Save button remains");
			await CaptureAsync(integrity, "skipped-metadata-report.png");
			SongEditDocument edit = await session.GetSongEditAsync(bad.SongId);
			await session.SaveSongEditAsync(edit, edit.Title, edit.Artists, edit.Tags, "[C]Repaired text");
			CallScan(integrity);
			await WaitAsync(() => output.Text?.Contains("Sheet type: Previous save") == true);
			Check(!output.Text.Contains("Sheet type: Current sheet"), "Repaired current sheet still reported invalid");
			Guid previous = session.Database!.SongFiles.Single(file => file.SavedVersion is not null).Id;
			await session.DeleteArchivedSongFileAsync(bad.SongId, previous);
			CallScan(integrity);
			await WaitAsync(() => output.Text?.Contains("No integrity problems found.") == true);
			Check(!output.Text.Contains("U+0001") && !output.Text.Contains("last book open"), "Deleted previous-save warning remained");
			await CaptureAsync(integrity, "current-validation-clean.png");
			typeof(BookIntegrityPage).GetMethod("HandleClose", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(integrity, [null, EventArgs.Empty]);
			await WaitAsync(() => page.Navigation.NavigationStack.Count == 1);
		}
		finally
		{
			for (int index = 0; index < keys.Length; index++)
			{
				if (saved[index] is string value) Preferences.Default.Set(keys[index], value);
				else Preferences.Default.Remove(keys[index]);
			}
		}
		Program.Trace("Legacy-source book opening, retained songs, warning and recent-books menu passed");
	}
	private static void CallScan(BookIntegrityPage page)
		=> typeof(BookIntegrityPage).GetMethod("HandleScan", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [null, EventArgs.Empty]);

	private static IEnumerable<Microsoft.Maui.IVisualTreeElement> Elements(Microsoft.Maui.IVisualTreeElement root)
	{
		yield return root;
		foreach (var child in root.GetVisualChildren())
			foreach (var descendant in Elements(child)) yield return descendant;
	}

}
