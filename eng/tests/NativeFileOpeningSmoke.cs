using System.Diagnostics;
using System.Reflection;
using System.Text;
using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui;
using Menees.Chords.Book.Maui.Platforms.Windows;
using Menees.Chords.Db;
using Microsoft.Win32;
using static SetlistSmoke.SmokeMauiApp;

namespace SetlistSmoke;

internal static class NativeFileOpeningSmoke
{
	public static async Task RunAsync(MainPage page, BookSession session, BookApplicationSession app, FileSystemBookStore store, BookLocation book)
	{
		Program.Trace("Verify Windows file activation and protected UI opening");
		Check(SmokeWindowsApp.StartupFiles.Count == 1 && Path.GetFileName(SmokeWindowsApp.StartupFiles[0]) == "Startup Ω sheet.cho",
			"Cold-start file activation did not preserve the Unicode/spaced path: " + string.Join(" | ", SmokeWindowsApp.StartupFiles));
		string root = Path.Combine(AppContext.BaseDirectory, "ExternalFiles");
		Directory.CreateDirectory(root);
		string path = Path.Combine(root, "Open Ω from Explorer.cho");
		const string Text = "{title: Opened From Explorer}\n{artist: Synthetic Artist}\n[C]Synthetic external file";
		File.WriteAllText(path, Text, new UTF8Encoding(false));
		Check(WindowsFileActivation.ParseCommandLine($"\"{Environment.ProcessPath}\" --open \"{path}\"").SequenceEqual(new[] { path }),
			"Quoted command-line parsing changed the file path");
		VerifyRegistration();
		using (Process child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
		{
			UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden, ArgumentList = { "--open", path },
		})!)
		{
			var received = await SmokeWindowsApp.Redirected.Task.WaitAsync(TimeSpan.FromSeconds(15));
			Check(received.SequenceEqual(new[] { path }), "Redirected activation lost or split the file path: " + string.Join(" | ", received));
			await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
			Check(child.ExitCode == 0, "Redirecting process failed");
		}

		Guid bookId = app.Database!.Id;
		try
		{
			string before = await store.ReadDatabaseJsonAsync(book);
			var input = new Entry { Text = "Unsaved synthetic editor input" };
			var child = new ContentPage { Content = input };
			NavigationPage.SetHasNavigationBar(child, false);
			await page.Navigation.PushAsync(child, false);
			page.QueueSongFiles([path]);
			await WaitAsync(() => ((Queue<string>)Field(page, "pendingFiles")!).Count == 1);
			Check((bool)Field(page, "processingFiles")! == false && input.Text == "Unsaved synthetic editor input",
				"File activation interrupted an open editor");
			Check(await store.ReadDatabaseJsonAsync(book) == before, "Queued file imported while editor was open");
			await page.Navigation.PopAsync(false);
			await WaitAsync(() => app.Database!.Songs.Any(song => song.Title == "Opened From Explorer"));
			Guid imported = app.Database!.Songs.Single(song => song.Title == "Opened From Explorer").Id;
			await WaitAsync(() => CurrentSong(page) == imported && (bool)Field(page, "viewerReady")! && !(bool)Field(page, "processingFiles")!);
			Check(app.Database.SongFiles.Count(file => file.SongId == imported) == 1, "Automatic file import added unexpected sheets");
			string displayed = await page.FindByName<WebView>("SongViewer").EvaluateJavaScriptAsync("document.body.innerText");
			Check(displayed.Contains("Synthetic external file", StringComparison.Ordinal), "Imported chart text did not reach WebView2: " + displayed);
			await CaptureAsync(page, "opened-explorer-file.png");
			before = await store.ReadDatabaseJsonAsync(book);
			int generation = (int)Field(page, "viewerGeneration")!;
			page.QueueSongFiles([path]);
			await WaitAsync(() => (int)Field(page, "viewerGeneration")! > generation);
			await WaitAsync(() => CurrentSong(page) == imported && (bool)Field(page, "viewerReady")! && !(bool)Field(page, "processingFiles")!);
			Check(await store.ReadDatabaseJsonAsync(book) == before, "Repeated file opening imported duplicate content");

			string different = Path.Combine(root, "Possible version.cho");
			File.WriteAllText(different, Text + "\nDifferent bytes");
			page.QueueSongFiles([different]);
			await WaitAsync(() => page.Navigation.NavigationStack.LastOrDefault() is ImportReviewPage { Handler: not null });
			ImportReviewPage review = (ImportReviewPage)page.Navigation.NavigationStack.Last();
			await WaitAsync(() => review.FindByName<CollectionView>("files").ItemsSource is IReadOnlyList<ImportReviewRow> { Count: 1 });
			ImportReviewRow row = ((IReadOnlyList<ImportReviewRow>)review.FindByName<CollectionView>("files").ItemsSource)[0];
			Check(row.Decision.Action == BookImportAction.Skip && row.Item!.Conflict == BookImportConflict.PossibleVersion,
				"Title/artist match did not default to review/Skip");
			await CaptureAsync(review, "opened-file-review.png");
			typeof(ImportReviewPage).GetMethod("HandleBack", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(review, [null, EventArgs.Empty]);
			await WaitAsync(() => page.Navigation.NavigationStack.Count == 1 && !(bool)Field(page, "processingFiles")!);
			Check(await store.ReadDatabaseJsonAsync(book) == before, "Canceling file review changed the book");
			Call(page, "ExitPerformanceMode");
			session.FlushRecentHistory();
		}
		finally
		{
			session.FlushRecentHistory();
			Preferences.Default.Remove("ChordBook.RecentSongs." + bookId.ToString("D"));
			Preferences.Default.Remove("ChordBook.SongAccessTimes." + bookId.ToString("D"));
		}
		Program.Trace("Cold/redirection activation, scoped registration, protected queue, import/display, repeated match and version-review cancel passed");
	}

	private static Guid? CurrentSong(MainPage page)
	{
		var songs = (IReadOnlyList<SongRow>)Field(page, "performanceSongs")!;
		int index = (int)Field(page, "currentSongIndex")!;
		return index >= 0 && index < songs.Count ? songs[index].Id : null;
	}

	private static void VerifyRegistration()
	{
		string keyName = "Software\\ChordBookAssociationTest-" + Guid.NewGuid().ToString("N");
		try
		{
			using RegistryKey root = Registry.CurrentUser.CreateSubKey(keyName);
			using (RegistryKey extension = root.CreateSubKey(@"Software\Classes\.cho")) extension.SetValue("", "Existing.App");
			WindowsFileAssociations.Register(root, Environment.ProcessPath!);
			WindowsFileAssociations.Register(root, Environment.ProcessPath!);
			using RegistryKey association = root.OpenSubKey(@"Software\Classes\Menees.ChordBook.Song\shell\open\command")!;
			Check(association.GetValue("") as string == $"\"{Environment.ProcessPath}\" --open \"%1\"", "Association command is not safely quoted");
			using RegistryKey original = root.OpenSubKey(@"Software\Classes\.cho")!;
			Check(original.GetValue("") as string == "Existing.App", "Registration changed the current default owner");
			using RegistryKey capabilities = root.OpenSubKey(@"Software\Menees\ChordBook\Capabilities\FileAssociations")!;
			Check(capabilities.GetValue(".cho") as string == "Menees.ChordBook.Song", "Default-app capabilities missing");
		}
		finally { Registry.CurrentUser.DeleteSubKeyTree(keyName, false); }
	}
}
