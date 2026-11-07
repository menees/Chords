using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui;
using Menees.Chords.Db;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static SetlistSmoke.SmokeMauiApp;
using NativeButton = Microsoft.UI.Xaml.Controls.Button;

namespace SetlistSmoke;

internal static class NativeViewSortSmoke
{
	public static async Task RunAsync(MainPage page, BookSession session, BookApplicationSession app, FileSystemBookStore store, BookLocation book)
	{
		Program.Trace("Verify local view date sorting and native dialog");
		Guid bookId = app.Database!.Id;
		string[] keys = ["Songs", "Artists", "Recent"];
		try
		{
			Call(page, "ShowManagementTab", false, false);
			Song alpha = app.Database.Songs.Single(song => song.Title == "Alpha");
			Song zulu = app.Database.Songs.Single(song => song.Title == "Zulu");
			DateTimeOffset start = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
			alpha.Revision.ModifiedUtc = start;
			zulu.Revision.ModifiedUtc = start.AddDays(1);
			string before = await store.ReadDatabaseJsonAsync(book);
			Task pending = (Task)Call(page, "ChangeViewSortAsync")!;
			ContentDialog dialog = await FindDialogAsync(page);
			var fields = (StackPanel)dialog.Content;
			ComboBox picker = fields.Children.OfType<ComboBox>().Single();
			string?[] options = picker.Items.OfType<ComboBoxItem>().Select(item => item.Tag as string).ToArray();
			Check(options.Contains("created") && options.Contains("modified") && options.Contains("accessed"), "Date sort fields missing");
			Check((picker.SelectedItem as ComboBoxItem)?.Tag is null, "New view sort must default to original view ordering");
			picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == "modified");
			fields.Children.OfType<Microsoft.UI.Xaml.Controls.CheckBox>().Single().IsChecked = true;
			await CaptureAsync(dialog, "view-sort-dialog.png");
			Invoke(dialog, "Apply");
			await pending;
			Check(!page.FindByName<CollectionView>("SongList").IsGrouped, "Date sort must be chronological across the Songs view");
			Check(Visible(page).SequenceEqual(new[] { zulu.Id, alpha.Id }), "Modified descending order incorrect");
			Check(!page.FindByName<Microsoft.Maui.Controls.ScrollView>("SongJumps").IsVisible, "Chronological view retained alphabetic jumps");
			Check(session.GetViewSort("Artists") is null, "Songs sorting leaked to Artists");
			using (BookSession reopened = new(app, session.DeviceId))
			{
				Check(reopened.GetViewSort("Songs") == new SongViewSort("modified", true), "Local sort did not persist across sessions");
			}
			Check(await store.ReadDatabaseJsonAsync(book) == before, "Sorting modified the catalog");
			pending = (Task)Call(page, "ChangeViewSortAsync")!;
			dialog = await FindDialogAsync(page);
			fields = (StackPanel)dialog.Content;
			picker = fields.Children.OfType<ComboBox>().Single();
			Check((picker.SelectedItem as ComboBoxItem)?.Tag as string == "modified", "Dialog lost saved field");
			picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == "created");
			Invoke(dialog, "Cancel");
			await pending;
			Check(session.GetViewSort("Songs") == new SongViewSort("modified", true), "Cancel changed sorting");
			Check(Visible(page).SequenceEqual(new[] { zulu.Id, alpha.Id }), "Cancel changed song order");

			page.FindByName<SearchBar>("SongSearch").Text = "Zulu";
			Check(Visible(page).SequenceEqual(new[] { zulu.Id }), "Sorting lost search filtering");
			page.FindByName<SearchBar>("SongSearch").Text = "";
			Call(page, "ShowManagementTab", false, false);
			Check(Visible(page).SequenceEqual(new[] { zulu.Id, alpha.Id }), "Sort lost on view return");

			session.RecordSongAccess(zulu.Id);
			session.RecordSongAccess(alpha.Id);
			session.SaveViewSort("Songs", new("accessed", true));
			Call(page, "ApplyFilter", "");
			Check(Visible(page).SequenceEqual(new[] { alpha.Id, zulu.Id }), "Device access dates not applied");
			session.FlushRecentHistory();
			using (BookSession reopened = new(app, session.DeviceId))
			{
				Check(reopened.SortSongs(reopened.SearchSongs("", false), "accessed", true).Select(song => song.Id)
					.SequenceEqual(new[] { alpha.Id, zulu.Id }), "Device access dates did not persist");
			}
			Check(await store.ReadDatabaseJsonAsync(book) == before, "Access history rewrote the catalog");
			pending = (Task)Call(page, "ChangeViewSortAsync")!;
			dialog = await FindDialogAsync(page);
			fields = (StackPanel)dialog.Content;
			picker = fields.Children.OfType<ComboBox>().Single();
			picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().Single(item => item.Tag is null);
			Invoke(dialog, "Apply");
			await pending;
			Check(session.GetViewSort("Songs") is null && page.FindByName<CollectionView>("SongList").IsGrouped, "Default did not restore A-Z view");
			await CaptureAsync(page, "view-sort-default.png");
		}
		finally
		{
			session.FlushRecentHistory();
			foreach (string key in keys) Preferences.Default.Remove("ChordBook.ViewSort." + bookId.ToString("D") + "." + key);
			Preferences.Default.Remove("ChordBook.RecentSongs." + bookId.ToString("D"));
			Preferences.Default.Remove("ChordBook.SongAccessTimes." + bookId.ToString("D"));
		}
		Program.Trace("Native view dates, per-view persistence, cancel/default, history reload and unchanged catalog passed");
	}

	private static IEnumerable<Guid> Visible(MainPage page) => ((IReadOnlyList<SongRow>)Field(page, "visibleSongs")!).Select(song => song.Id);
	private static async Task<ContentDialog> FindDialogAsync(MainPage page)
	{
		var root = (FrameworkElement)page.Handler!.PlatformView!;
		ContentDialog? dialog = null;
		await WaitAsync(() =>
		{
			dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).SelectMany(p => Descendants(p.Child)).OfType<ContentDialog>().SingleOrDefault();
			return dialog is { ActualWidth: > 0 };
		});
		return dialog!;
	}
	private static void Invoke(ContentDialog dialog, string caption)
	{
		NativeButton button = Descendants(dialog).OfType<NativeButton>().Single(button => Equals(button.Content, caption));
		((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
	}
}
