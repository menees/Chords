using System.Reflection;
using Menees.Chords.Book.Maui;
using Menees.Chords.Book.Application;
using Menees.Chords.Db;
using static SetlistSmoke.SmokeMauiApp;

namespace SetlistSmoke;

internal static class NativeImportReviewSmoke
{
	internal static async Task RunAsync(MainPage owner, BookSession session)
	{
		Program.Trace("Verify mixed import review retains good files and names per-file failures");
		string root = Path.Combine(AppContext.BaseDirectory, "ReviewedFiles");
		Directory.CreateDirectory(root);
		string first = Path.Combine(root, "Before bad file.cho");
		string last = Path.Combine(root, "After bad file.cho");
		string bad = Path.Combine(root, "How Do You Like Me Now.cho");
		string version = Path.Combine(root, "Possible incoming version.cho");
		const string Text = "{title: Before bad file}\n{artist: Synthetic Artist}\n[C]Before";
		File.WriteAllText(first, Text);
		File.WriteAllText(last, "{title: After bad file}\n[D]After");
		File.WriteAllText(bad, "{title: Bad}\r\n[C]Oops\u0001");
		File.WriteAllText(version, Text + "\nDifferent text");
		string catalog = Path.Combine(session.DirectoryPath!, "database.json");
		string before = await File.ReadAllTextAsync(catalog);
		ImportReviewPage page = new(session, new[] { first, bad, last, first, version });
		await owner.Navigation.PushAsync(page, false);
		Picker filter = page.FindByName<Picker>("filter");
		CollectionView files = page.FindByName<CollectionView>("files");
		await WaitAsync(() => filter.SelectedIndex == 4 && page.FindByName<FluentIconButton>("apply").IsEnabled);
		Check(filter.Items[4] == "Could not process", "Failed-file category missing");
		Check(page.FindByName<Label>("summary").Text.Contains("5 selected") && page.FindByName<Label>("summary").Text.Contains("1 could not process"),
			"Mixed import counts lost failed files");
		ImportReviewRow failure = ((IReadOnlyList<ImportReviewRow>)files.ItemsSource).Single();
		Check(failure.Failure?.SourcePath == bad && failure.Title == Path.GetFileName(bad)
			&& failure.Description.Contains("U+0001 at line 2, column 8"), "Failed file name/path/error is missing");
		files.SelectedItem = failure;
		Check(!page.FindByName<Picker>("action").IsVisible && !page.FindByName<Picker>("candidate").IsVisible
			&& !page.FindByName<Button>("compare").IsEnabled, "Failed file offered import/compare actions");
		await CaptureAsync(page, "import-could-not-process.png");
		int[] counts = [1, 2, 1, 5, 1];
		for (int index = 0; index < counts.Length; index++)
		{
			filter.SelectedIndex = index;
			Check(((IReadOnlyList<ImportReviewRow>)files.ItemsSource).Count == counts[index], "Wrong import category count " + index);
		}
		filter.SelectedIndex = 3;
		await CaptureAsync(page, "import-all-files-with-failure.png");
		Check(await File.ReadAllTextAsync(catalog) == before, "Review mutated catalog");
		Invoke(page, "HandleBack");
		await page.Completion;
		Check(await File.ReadAllTextAsync(catalog) == before, "Cancel review mutated catalog");

		ImportReviewPage failed = new(session, new[] { bad, Path.Combine(root, "Missing file.cho") });
		await owner.Navigation.PushAsync(failed, false);
		await WaitAsync(() => failed.FindByName<Picker>("filter").SelectedIndex == 4);
		Check(!failed.FindByName<FluentIconButton>("apply").IsEnabled, "All-failed review enabled Apply");
		Check(((IReadOnlyList<ImportReviewRow>)failed.FindByName<CollectionView>("files").ItemsSource).Count == 2, "All-failed review omitted errors");
		Invoke(failed, "HandleBack");
		await failed.Completion;
		ImportReviewPage applied = new(session, new[] { first, bad, last });
		await owner.Navigation.PushAsync(applied, false);
		await WaitAsync(() => applied.FindByName<FluentIconButton>("apply").IsEnabled);
		Invoke(applied, "HandleApply");
		string result = (await applied.Completion.WaitAsync(TimeSpan.FromSeconds(15)))!;
		Check(applied.ImportedFiles.Count == 2 && result.Contains("1 could not process"), "Mixed import did not apply valid files with explicit failure count");
		Check(session.Database!.Songs.Any(song => song.Title == "Before bad file") && session.Database.Songs.Any(song => song.Title == "After bad file")
			&& !session.Database.Songs.Any(song => song.Title == "Bad"), "Mixed import lost valid files or imported the failed one");
		await VerifyDefaultComparisonAsync(owner, session, version);
		Program.Trace("Native import failures/default category/all categories/read-only errors/Cancel/all-failed/valid Apply and default-sheet comparison roles passed");
	}

	private static async Task VerifyDefaultComparisonAsync(MainPage owner, BookSession session, string incomingPath)
	{
		Guid songId = session.Database!.Songs.Single(song => song.Title == "Before bad file").Id;
		SongEditDocument edit = await session.GetSongEditAsync(songId);
		Guid copyId = await session.DuplicateSongFileAsync(songId, edit.FileId!.Value);
		await session.SetSongFileArchivedAsync(songId, copyId, true);
		await session.SaveSongEditAsync(edit, edit.Title, edit.Artists, edit.Tags, edit.Text + "\nCurrent edited sheet");
		Guid expected = session.GetSongFiles(songId).Single(sheet => sheet.IsDefault).Id;
		Check(session.GetSongFiles(songId).Count >= 2, "Default comparison fixture needs multiple sheets");
		string catalog = Path.Combine(session.DirectoryPath!, "database.json");
		string before = await File.ReadAllTextAsync(catalog);
		ImportReviewPage review = new(session, new[] { incomingPath });
		await owner.Navigation.PushAsync(review, false);
		CollectionView files = review.FindByName<CollectionView>("files");
		await WaitAsync(() => files.ItemsSource is IReadOnlyList<ImportReviewRow> { Count: 1 });
		ImportReviewRow row = ((IReadOnlyList<ImportReviewRow>)files.ItemsSource).Single();
		files.SelectedItem = row;
		Picker candidates = review.FindByName<Picker>("candidate");
		Check(candidates.SelectedIndex >= 0 && row.Item!.Candidates[candidates.SelectedIndex].SheetId == expected
			&& row.Decision.Action == BookImportAction.Skip && review.FindByName<Button>("compare").IsEnabled,
			"Multiple sheets did not preselect the active default for comparison while retaining Skip");
		await CaptureAsync(review, "import-default-sheet-selected.png");
		Invoke(review, "HandleCompare");
		await WaitAsync(() => owner.Navigation.NavigationStack.Last().GetType().Name == "SongVersionComparisonPage");
		ContentPage comparison = (ContentPage)owner.Navigation.NavigationStack.Last();
		await WaitAsync(() => comparison.Handler is not null);
		Grid layout = (Grid)comparison.Content!;
		Label[] labels = ((Grid)layout.Children[1]).Children.OfType<Label>().ToArray();
		Check(labels[0].Text.Contains("Existing sheet in ChordBook") && labels[1].Text.Contains("File being imported"),
			"Import comparison side roles are not explicit");
		SongVersionComparison model = (SongVersionComparison)comparison.GetType().GetField("comparison", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(comparison)!;
		Check(model.Left.Id == expected && !model.Left.IsIncoming && model.Right.IsIncoming, "Compared the wrong default sheet or source role");
		await WaitAsync(() => (bool)comparison.GetType().GetField("loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(comparison)!
			&& comparison.GetType().GetField("operation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(comparison) is null);
		await CaptureAsync(comparison, "import-comparison-source-roles.png");
		Program.Trace("Import comparison loaded/captured; close it");
		comparison.GetType().GetMethod("HandleClose", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(comparison, [null, EventArgs.Empty]);
		await WaitAsync(() => owner.Navigation.NavigationStack.Last() == review);
		await WaitAsync(() => review.GetType().GetField("operation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(review) is null);
		Program.Trace("Returned from import comparison; review ready; select archived manual sheet");
		int other = row.Item!.Candidates.ToList().FindIndex(item => item.SheetId != expected);
		candidates.SelectedIndex = other;
		Program.Trace("Archived manual sheet selected; deselect import row");
		files.SelectedItem = null;
		Program.Trace("Reselect import row");
		files.SelectedItem = row;
		Program.Trace("Import row reselected");
		Check(candidates.SelectedIndex == other, "Revisiting a file discarded its explicit comparison sheet selection");
		Program.Trace("Close reviewed import after comparison");
		Invoke(review, "HandleBack");
		await review.Completion.WaitAsync(TimeSpan.FromSeconds(10));
		Program.Trace("Reviewed import closed after comparison");
		Check(await File.ReadAllTextAsync(catalog) == before, "Default selection/comparison changed the book");
	}

	private static void Invoke(ImportReviewPage page, string method)
		=> typeof(ImportReviewPage).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, [null, EventArgs.Empty]);
}
