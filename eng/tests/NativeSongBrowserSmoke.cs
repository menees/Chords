using System.Reflection;
using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui;
using Menees.Chords.Db;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static SetlistSmoke.SmokeMauiApp;
using MauiCollection = Microsoft.Maui.Controls.CollectionView;
using MauiGrid = Microsoft.Maui.Controls.Grid;

namespace SetlistSmoke;

internal static class NativeSongBrowserSmoke
{
	internal static async Task RunAsync(Microsoft.Maui.Controls.Window window)
	{
		Program.Trace("Verify browser viewport across source edits and performance Back");
		string[] preferenceKeys = ["ChordBook.CurrentBookPath", "ChordBook.RecentBooks"];
		string?[] preferences = [.. preferenceKeys.Select(key => Preferences.Default.Get<string?>(key, null))];
		Guid device = Guid.NewGuid();
		using FileSystemBookStore store = new(Path.Combine(AppContext.BaseDirectory, "BrowserBooks"));
		BookLocation book = await store.CreateBookAsync("Browser viewport", device);
		BookApplicationSession application = new();
		await application.ActivateAsync(store, book);
		Guid maybe = await application.CreateSongAsync("Maybe", ["Synthetic Artist"], [], "[C]Maybe original", device);
		string before = await store.ReadDatabaseJsonAsync(book);
		ChordDatabase database = DatabaseJson.Deserialize(before);
		for (int index = 0; index < 720; index++)
		{
			database.Songs.Add(new()
			{
				Id = Guid.CreateVersion7(), Title = $"{(char)('A' + (index % 26))} Scroll fixture {index:D4}",
				Artists = ["Synthetic Artist"], Revision = RevisionStamp.Initial(device),
			});
		}
		await store.CommitMetadataAsync(book, before, DatabaseJson.Serialize(database));
		using BookSession session = new(application, device);
		using SilentMetronome metronome = new();
		try
		{
			await session.OpenAsync(store.GetDirectory(book));
			MainPage page = new(session, new NoPicker(), metronome);
			NavigationPage.SetHasNavigationBar(page, false);
			window.Page = new NavigationPage(page);
			window.Width = 1100;
			window.Height = 800;
			await WaitAsync(() => page.FindByName<Microsoft.Maui.Controls.Label>("Status").Text.StartsWith("Ready."));
			foreach (bool grouped in new[] { true, false })
			{
				session.SaveViewSort("Songs", grouped ? new() : new("title", true));
				Call(page, "ApplyFilter", string.Empty);
				await VerifySelectionAsync(page, session);
				MauiCollection list = page.FindByName<MauiCollection>("SongList");
				Check(list.IsGrouped == grouped, "Scroll fixture did not switch grouping");
				SongRow row = Rows(page).Single(song => song.Id == maybe);
				SongGroup? group = ((IReadOnlyList<SongGroup>)Field(page, "songGroups")!).SingleOrDefault(item => item.Contains(row));
				list.ScrollTo(row, group, ScrollToPosition.Center, animate: false);
				await WaitAsync(() => Scroll(list).VerticalOffset > 1000);
				await Task.Delay(150);
				var anchor = VisibleAnchor(list);

				await OpenAsync(page, list, row);
				Call(page, "TryNavigateBack");
				await ReturnedAsync(page, list, anchor);
				await OpenAsync(page, list, row);
				Task editing = (Task)Call(page, "ManageSongAsync", maybe, "Edit Song")!;

				await WaitAsync(() => page.Navigation.NavigationStack.LastOrDefault() is SongEditorPage { Handler: not null });
				SongEditorPage editor = (SongEditorPage)page.Navigation.NavigationStack.Last();
				await WaitAsync(() => (bool)typeof(SongEditorPage).GetField("ready", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!);
				ISongTextEditor text = (ISongTextEditor)typeof(SongEditorPage).GetField("editor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
				string updated = grouped ? "[G]Maybe edited in grouped view" : "[D]Maybe edited in flat view";
				await text.ReplaceTextAsync(updated);
				typeof(SongEditorPage).GetMethod("HandleSave", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, [null, EventArgs.Empty]);
				await editing.WaitAsync(TimeSpan.FromSeconds(15));
				await WaitAsync(() => (bool)Field(page, "viewerReady")!);
				Check(page.FindByName<MauiGrid>("PerformanceSurface").IsVisible, "Save did not return to the open song");
				Check((await session.GetSongEditAsync(maybe)).Text == updated, "Scroll test did not save actual source edits");
				Call(page, "TryNavigateBack");
				await ReturnedAsync(page, list, anchor);
				Check(page.FindByName<Microsoft.Maui.Controls.SearchBar>("SongSearch").Text is null or "", "Save changed the search");
				Check(list.IsGrouped == grouped, "Save changed view sorting/grouping");
				await CaptureAsync(page, grouped ? "browser-return-grouped.png" : "browser-return-flat.png");
				Program.Trace($"{(grouped ? "Grouped" : "Flat")} browser return after source Save: {anchor.Y:F1} -> {AnchorY(list, anchor.Id):F1} (visible row Y)");
				row = Rows(page).Single(song => song.Id == maybe);
				await OpenAsync(page, list, row);
				Call(page, "TryNavigateBack");
				page.FindByName<Microsoft.Maui.Controls.SearchBar>("SongSearch").Text = "Scroll fixture";
				await WaitAsync(() => Rows(page).Count == 720 && Scroll(list).VerticalOffset < 1);
				await Task.Delay(100);
				Check(Scroll(list).VerticalOffset < 1, "An old restoration overrode a newer search");
				page.FindByName<Microsoft.Maui.Controls.SearchBar>("SongSearch").Text = string.Empty;
			}
			session.FlushRecentHistory();
		}
		finally
		{
			session.FlushRecentHistory();
			foreach (string prefix in new[] { "ChordBook.RecentSongs.", "ChordBook.SongAccessTimes.", "ChordBook.ViewSort." })
				Preferences.Default.Remove(prefix + database.Id.ToString("D") + (prefix.EndsWith("ViewSort.") ? ".Songs" : string.Empty));
			for (int index = 0; index < preferenceKeys.Length; index++)
			{
				if (preferences[index] is string value) Preferences.Default.Set(preferenceKeys[index], value);
				else Preferences.Default.Remove(preferenceKeys[index]);
			}
		}
		Program.Trace("Browser viewport, real source Save/return, grouped/flat and stale-search cancellation passed");
	}

	private static async Task VerifySelectionAsync(MainPage page, BookSession session)
	{
		Program.Trace("Verify Select All / Unselect All in filtered and complete Songs views");
		string path = Path.Combine(session.DirectoryPath!, "database.json");
		string before = await File.ReadAllTextAsync(path);
		MauiCollection list = page.FindByName<MauiCollection>("SongList");
		var select = page.FindByName<FluentIconButton>("SelectAllSongsButton");
		var unselect = page.FindByName<FluentIconButton>("UnselectAllSongsButton");
		Call(page, "ApplyFilter", "title:Maybe");
		Call(page, "BeginSongSelection", new object?[] { null });
		Check(select.IsEnabled && !unselect.IsEnabled, "Initial selection action states incorrect");
		int notifications = 0;
		EventHandler<Microsoft.Maui.Controls.SelectionChangedEventArgs> changed = (_, _) => notifications++;
		list.SelectionChanged += changed;
		((IButtonController)select).SendClicked();
		Check(list.SelectedItems.Count == 1 && Rows(page).Single().IsSelected, "Select All escaped current filter");
		Check(!select.IsEnabled && unselect.IsEnabled, "All-selected action states incorrect");
		((IButtonController)unselect).SendClicked();
		Check(list.SelectedItems.Count == 0 && (bool)Field(page, "selectingSongs")!, "Unselect All left selection mode");
		Check(notifications == 2, "Bulk selection did not batch its notifications");
		list.SelectionChanged -= changed;
		Call(page, "EndSongSelection");
		Call(page, "ApplyFilter", string.Empty);
		Call(page, "BeginSongSelection", new object?[] { null });
		notifications = 0;
		list.SelectionChanged += changed;
		var timer = System.Diagnostics.Stopwatch.StartNew();
		((IButtonController)select).SendClicked();
		timer.Stop();
		Check(list.SelectedItems.Count == 721 && Rows(page).All(row => row.IsSelected), "Select All missed unrealized rows");
		Check(notifications == 1, "Large Select All emitted per-song notifications");
		Program.Trace($"Select {list.SelectedItems.Count} songs in {timer.Elapsed.TotalMilliseconds:F1} ms, {notifications} notification");
		page.Window.Width = 480;
		await Task.Delay(150);
		var toolbar = page.FindByName<Microsoft.Maui.Controls.FlexLayout>("SongSelectionToolbar");
		var actions = page.FindByName<Microsoft.Maui.Controls.HorizontalStackLayout>("SongSelectionActions");
		Check(actions.X >= 0 && actions.X + actions.Width <= toolbar.Width + 1, "Selection actions clipped in narrow window");
		await CaptureAsync(page, $"select-all-{list.IsGrouped}-narrow.png");
		((IButtonController)unselect).SendClicked();
		Check(notifications == 2 && Rows(page).All(row => !row.IsSelected), "Unselect All missed rows or emitted per-song events");
		Check(!page.FindByName<FluentIconButton>("TagSelectedSongsButton").IsEnabled, "Tag action remained enabled without selection");
		list.SelectionChanged -= changed;
		Call(page, "EndSongSelection");
		Call(page, "ApplyFilter", "title:No matching synthetic song");
		Call(page, "BeginSongSelection", new object?[] { null });
		Check(!select.IsEnabled && !unselect.IsEnabled, "Empty view selection actions enabled");
		Call(page, "EndSongSelection");
		Call(page, "ApplyFilter", string.Empty);
		page.Window.Width = 1100;
		await Task.Delay(100);
		Check(await File.ReadAllTextAsync(path) == before, "Selection actions wrote the catalog");
	}

	private static IReadOnlyList<SongRow> Rows(MainPage page) => (IReadOnlyList<SongRow>)Field(page, "visibleSongs")!;
	private static ScrollViewer Scroll(MauiCollection view)
		=> Descendants((FrameworkElement)view.Handler!.PlatformView!).OfType<ScrollViewer>().First();
	private static async Task OpenAsync(MainPage page, MauiCollection list, SongRow row)
	{
		list.SelectedItem = row;
		await WaitAsync(() => page.FindByName<MauiGrid>("PerformanceSurface").IsVisible && (bool)Field(page, "viewerReady")!);
	}
	private static (Guid Id, double Y) VisibleAnchor(MauiCollection list)
		=> NativeRows(list).Where(pair => pair.Y + pair.Height > 0 && pair.Y < Scroll(list).ViewportHeight).OrderBy(pair => pair.Y)
			.Select(pair => (pair.Id, pair.Y)).First();
	private static double AnchorY(MauiCollection list, Guid id) => NativeRows(list).SingleOrDefault(pair => pair.Id == id).Y;
	private static IEnumerable<(Guid Id, double Y, double Height)> NativeRows(MauiCollection list)
		=> Descendants((FrameworkElement)list.Handler!.PlatformView!).OfType<ListViewItem>()
			.Where(item => item.Content is Microsoft.Maui.Controls.Platform.ItemTemplateContext { Item: SongRow })
			.Select(item => (((SongRow)((Microsoft.Maui.Controls.Platform.ItemTemplateContext)item.Content).Item).Id,
				item.TransformToVisual(Scroll(list)).TransformPoint(new(0, 0)).Y, item.ActualHeight));
	private static async Task ReturnedAsync(MainPage page, MauiCollection list, (Guid Id, double Y) anchor)
	{
		await WaitAsync(() => page.FindByName<MauiGrid>("ManagementSurface").IsVisible
			&& NativeRows(list).Any(pair => pair.Id == anchor.Id && Math.Abs(pair.Y - anchor.Y) < 1));
		await Task.Delay(300);
		Check(Math.Abs(AnchorY(list, anchor.Id) - anchor.Y) < 1, "Browser row anchor drifted after restoration");
	}
}
