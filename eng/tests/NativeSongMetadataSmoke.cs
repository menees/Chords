using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
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
using NativeEntry = Microsoft.UI.Xaml.Controls.TextBox;
using NativeList = Microsoft.UI.Xaml.Controls.ListView;

namespace SetlistSmoke;

internal static class NativeSongMetadataSmoke
{
	internal static async Task RunBulkTagsAsync(MainPage owner, BookSession session)
	{
		Program.Trace("Verify setlist Select Songs and bulk tagging through the main Songs view");
		Guid listId = session.Database!.Setlists.First().Id;
		Guid[] selectedIds = session.Database.Setlists.First().Entries.Select(entry => entry.SongId).Distinct().ToArray();
		await session.SetSongTagsAsync([selectedIds[0]], ["Practice", "Shared"]);
		await session.SetSongTagsAsync([selectedIds[1]], ["Live", "Shared"]);
		Guid archivedMember = session.Database.Setlists.First().Entries[0].SongId;
		await session.SetSongsArchivedAsync([archivedMember], true);
		Call(owner, "RefreshCatalogAfterEdit", "Synthetic archived member");
		Call(owner, "ShowManagementTab", true, false);
		Call(owner, "OpenSetlist", listId);
		string path = Path.Combine(session.DirectoryPath!, "database.json");
		string before = await File.ReadAllTextAsync(path);
		string files = System.Text.Json.JsonSerializer.Serialize(session.Database.SongFiles);
		Guid[] ids = session.Database.Setlists.First().Entries.Select(entry => entry.SongId).Distinct().ToArray();
		var selectButton = owner.FindByName<FluentIconButton>("SelectSetlistSongsButton");
		Check(selectButton.IsVisible && selectButton.IsEnabled, "Setlist Select Songs unavailable");
		Check(owner.FindByName<FluentIconButton>("TagSetlistSongsButton") is null, "Setlist Tags button remains");
		await Task.Delay(100);
		var editButton = owner.FindByName<FluentIconButton>("EditSetlistButton");
		Check(Math.Abs(selectButton.X - editButton.X - editButton.Width - 8) < 1, "Hidden toolbar actions leave gaps");
		await CaptureAsync(owner, "setlist-select-songs-toolbar.png");
		owner.FindByName<Microsoft.Maui.Controls.SearchBar>("SongSearch").Text = "No matching titles";
		((IButtonController)selectButton).SendClicked();
		await WaitAsync(() => (bool)Field(owner, "selectingSongs")!);
		Check(owner.FindByName<BookTabs>("ManagementTabs").SelectedIndex == 0, "Selection did not switch to main Songs");
		Check(owner.FindByName<Microsoft.Maui.Controls.SearchBar>("SongSearch").Text.Length == 0, "Prior filter hides setlist songs");
		Check(((IReadOnlyList<SongRow>)Field(owner, "visibleSongs")!).Where(song => song.IsSelected).Select(song => song.Id).ToHashSet().SetEquals(ids),
			"Setlist selection lost or duplicated song identities");
		Check(owner.FindByName<ArchiveToggle>("ShowArchived").IsChecked, "Archived setlist member was hidden");
		Check(await File.ReadAllTextAsync(path) == before, "Select Songs wrote the catalog");
		await CaptureAsync(owner, "setlist-songs-selected.png");
		var tagButton = owner.FindByName<FluentIconButton>("TagSelectedSongsButton");
		((IButtonController)tagButton).SendClicked();
		ContentDialog dialog = await FindDialogAsync(owner);
		Check(dialog.Title.ToString() == $"Edit tags for {ids.Length:N0} songs", "Repeated entries counted more than once");
		Check(Descendants(dialog).OfType<NativeList>().Single().SelectedItems.Cast<string>().ToHashSet().SetEquals(["Practice", "Shared", "Live"]),
			"Dialog did not preselect the selected songs' tag union");
		Check(Descendants(dialog).OfType<NativeList>().Single().ActualHeight >= 3 * 44, "Short union list clipped by description");
		await CaptureAsync(dialog, "edit-tags-union.png");
		Descendants(dialog).OfType<NativeList>().Single().SelectedItems.Remove("Practice");
		Search(dialog).Text = "Christian";
		await InvokeAddAsync(dialog);
		await CaptureAsync(dialog, "setlist-selection-tags-dialog.png");
		Invoke(dialog, "Cancel");
		await WaitAsync(() => !(bool)Field(owner, "bookMutationInProgress")!);
		Check(await File.ReadAllTextAsync(path) == before, "Bulk tag Cancel wrote catalog");
		((IButtonController)tagButton).SendClicked();
		dialog = await FindDialogAsync(owner);
		Check(Descendants(dialog).OfType<NativeList>().Single().SelectedItems.Contains("Practice"), "Cancel lost union preselection");
		Descendants(dialog).OfType<NativeList>().Single().SelectedItems.Remove("Practice");
		Search(dialog).Text = "Christian";
		await InvokeAddAsync(dialog);
		Invoke(dialog, "Apply");
		await WaitAsync(() => !(bool)Field(owner, "bookMutationInProgress")!);
		Check(ids.All(id => session.Database.Songs.Single(song => song.Id == id).Tags.ToHashSet().SetEquals(["Shared", "Live", "Christian"])),
			"Apply did not replace every song with the checked tag set");
		Check(!(bool)Field(owner, "selectingSongs")!, "Apply did not leave selection mode");
		Check(((IReadOnlyList<SongRow>)Field(owner, "allSongs")!).All(song => song.Tags.Split(", ").Contains("Christian")), "The Tags column did not show the saved tag");
		Check(System.Text.Json.JsonSerializer.Serialize(session.Database.SongFiles) == files, "Tagging changed source records");
		Call(owner, "ShowManagementTab", false, false);
		Call(owner, "BeginSongSelection", new object?[] { null });
		var selected = ((IReadOnlyList<SongRow>)Field(owner, "visibleSongs")!).First(song => song.Id == ids[0]);
		owner.FindByName<Microsoft.Maui.Controls.CollectionView>("SongList").SelectedItems = [selected];
		var selectedButton = owner.FindByName<FluentIconButton>("TagSelectedSongsButton");
		await WaitAsync(() => selectedButton.IsEnabled);
		((IButtonController)selectedButton).SendClicked();
		dialog = await FindDialogAsync(owner);
		Search(dialog).Text = "LyricOnly";
		await InvokeAddAsync(dialog);
		Invoke(dialog, "Apply");
		await WaitAsync(() => !(bool)Field(owner, "bookMutationInProgress")!);
		Check(session.Database.Songs.Single(song => song.Id == ids[0]).Tags.Contains("LyricOnly"), "Song selection tags missing");
		Check(session.Database.Songs.Single(song => song.Id == ids[0]).Tags.Contains("Christian"), "Existing tag discarded");
		owner.FindByName<Microsoft.Maui.Controls.SearchBar>("SongSearch").Text = "tag:!LyricOnly";
		await WaitAsync(() => ((IReadOnlyList<SongRow>)Field(owner, "visibleSongs")!).All(song => song.Id != ids[0]));
		Check(((IReadOnlyList<SongRow>)Field(owner, "visibleSongs")!).Count > 0, "Exclusion hid untagged songs");
		await CaptureAsync(owner, "bulk-tags-exclusion-search.png");
		owner.FindByName<Microsoft.Maui.Controls.SearchBar>("SongSearch").Text = string.Empty;
		Call(owner, "ShowManagementTab", true, false);
		Call(owner, "OpenSetlist", listId);
		((IButtonController)owner.FindByName<FluentIconButton>("SelectSetlistSongsButton")).SendClicked();
		((IButtonController)owner.FindByName<FluentIconButton>("TagSelectedSongsButton")).SendClicked();
		dialog = await FindDialogAsync(owner);
		Descendants(dialog).OfType<NativeList>().Single().SelectedItems.Clear();
		await CaptureAsync(dialog, "edit-tags-clear-all.png");
		Invoke(dialog, "Apply");
		await WaitAsync(() => !(bool)Field(owner, "bookMutationInProgress")!);
		Check(ids.All(id => session.Database.Songs.Single(song => song.Id == id).Tags.Count == 0), "Empty Apply did not remove all tags");
		Check(System.Text.Json.JsonSerializer.Serialize(session.Database.SongFiles) == files, "Editing tags changed source records");
		await session.SetSongsArchivedAsync([archivedMember], false);
		Call(owner, "RefreshCatalogAfterEdit", "Synthetic archived member restored");
		owner.FindByName<ArchiveToggle>("ShowArchived").IsChecked = false;
		Call(owner, "ShowManagementTab", true, false);
		Call(owner, "OpenSetlist", listId);
	}

	private static async Task VerifySongDetailsAndDuplicationAsync(MainPage owner, BookSession session)
	{
		Guid songId = session.Database!.Setlists.First().Entries[0].SongId;
		SongEditDocument original = await session.GetSongEditAsync(songId);
		Guid sourceId = original.FileId!.Value;
		string catalogPath = Path.Combine(session.DirectoryPath!, "database.json");
		string before = await File.ReadAllTextAsync(catalogPath);
		SongEditorPage editor = new(original, session);
		await owner.Navigation.PushAsync(editor, false);
		var form = editor.FindByName<SongMetadataEditor>("metadataEditor");
		await WaitAsync(() => (bool)typeof(SongEditorPage).GetField("ready", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!);
		form.TitleEntry.Text = "Unsaved title";
		((IButtonController)editor.FindByName<FluentIconButton>("songDetails")).SendClicked();
		await WaitAsync(() => owner.Navigation.NavigationStack.Last().Title == "Song Details");
		var details = owner.Navigation.NavigationStack.Last();
		var output = Elements(details).OfType<Microsoft.Maui.Controls.Editor>().Single();
		Check(output.IsReadOnly && output.Text.Contains("Modified:") && output.Text.Contains("+00:00")
			&& output.Text.Contains(session.Database.Setlists.First().Name) && output.Text.Contains(original.Title),
			"Song Details did not include read-only saved timestamps or setlist membership");
		await CaptureAsync(details, "song-details.png");
		await owner.Navigation.PopAsync(false);
		Check(form.TitleEntry.Text == "Unsaved title" && await File.ReadAllTextAsync(catalogPath) == before, "Details changed the draft or book");
		form.Load(original);
		typeof(SongEditorPage).GetMethod("HandleCancel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, [null, EventArgs.Empty]);
		await editor.Completion;

		SongFilesPage sheets = new(songId, session, null!);
		await owner.Navigation.PushAsync(sheets, false);
		var duplicate = sheets.FindByName<FluentIconButton>("duplicate");
		var files = sheets.FindByName<Microsoft.Maui.Controls.CollectionView>("files");
		Check(!duplicate.IsEnabled, "Duplicate is enabled without a sheet selection");
		files.SelectedItem = ((IEnumerable<SongFileRow>)files.ItemsSource).Single(row => row.Id == sourceId);
		Check(duplicate.IsEnabled, "Duplicate is disabled for a selected sheet");
		((IButtonController)duplicate).SendClicked();
		await WaitAsync(() => files.SelectedItem is SongFileRow row && row.Id != sourceId && duplicate.IsEnabled);
		SongFileRow copy = (SongFileRow)files.SelectedItem!;
		Check(copy.Name.StartsWith("Copy of ") && !copy.IsArchived && copy.CanEdit, "Duplicate is not an editable independent sheet");
		Check(session.GetSongFiles(songId).Single(file => file.IsDefault).Id == sourceId, "Duplicate changed the default sheet");
		await CaptureAsync(sheets, "duplicate-sheet.png");
		typeof(SongFilesPage).GetMethod("HandleClose", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(sheets, [null, EventArgs.Empty]);
		Check(await sheets.Completion, "Duplicate did not report a changed sheet list");
		await session.SetSongFileArchivedAsync(songId, copy.Id, true);
		await session.DeleteArchivedSongFileAsync(songId, copy.Id);
		Program.Trace("Song Details preserved the draft; Duplicate Sheet selected an editable copy and retained the default");
	}

	internal static async Task RunAsync(MainPage owner, BookSession session)
	{
		Program.Trace("Verify compact full-editor metadata and shared value dialogs");
		await VerifyPickerPlaceholderAsync(owner, session);
		await VerifySongDetailsAndDuplicationAsync(owner, session);
		string catalogPath = Path.Combine(session.DirectoryPath!, "database.json");
		string before = await File.ReadAllTextAsync(catalogPath);
		Guid songId = session.Database!.Songs.First().Id;
		SongEditDocument original = (await session.GetSongEditAsync(songId)) with
		{
			Title = "A synthetic long song title", Artists = ["Current; Artist", "Other Artist"], Tags = ["Practice"],
			Metadata = new Dictionary<string, IReadOnlyList<string>>
			{
				["key"] = ["B"], ["tempo"] = ["120"], ["capo"] = ["4"], ["album"] = ["Original; album", "Other album"],
				["time"] = ["4/4"], ["year"] = ["2020"], ["duration"] = ["4:08"], ["copyright"] = ["Keep existing copyright"],
			},
		};
		SongEditorPage page = new(original, session);
		await owner.Navigation.PushAsync(page, false);
		SongMetadataEditor form = page.FindByName<SongMetadataEditor>("metadataEditor");
		await WaitAsync(() => (bool)typeof(SongEditorPage).GetField("ready", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!);
		await WaitAsync(() => typeof(SongMetadataEditor).GetField("suggestions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form) is not null);
		ChordDatabase choices = new();
		choices.Songs = [.. Enumerable.Range(0, 10000).Select(index => new Song { Artists = [$"Artist {index:D5}"] })];
		choices.Songs[0].Tags = ["Practice", "Live", "New tag"];
		choices.Songs[0].MetadataOverrides["time"] = ["4/4", "6/8"];
		form.LoadSuggestions(new BookMetadataValueIndex(choices));
		Check(!form.HasChanges, "Initial metadata draft is dirty");
		Check(form.GetMetadata().Scalars["copyright"].Single() == "Keep existing copyright", "Hidden copyright metadata lost");
		Check(!Elements(form).OfType<Microsoft.Maui.Controls.Picker>().Any(), "Combobox remains in metadata form");
		Check(!Elements(form).OfType<Microsoft.Maui.Controls.Label>().Any(label => label.Text == "Copyright"), "Copyright field remains");
		Microsoft.Maui.Controls.Window window = owner.Window;
		window.Height = 850;
		foreach (var size in new[] { (1908, 1), (1100, 2), (750, 3), (480, 5) })
		{
			window.Width = size.Item1;
			await Task.Delay(250);
			GridRows(form).ShouldHaveRows(size.Item2);
			AssertBounds(form);
			Check(page.FindByName<Microsoft.Maui.Controls.WebView>("text").Height > 180, "Metadata squeezed the source editor");
			await CaptureAsync(page, $"song-metadata-{size.Item1}.png");
		}
		window.Width = 1100;
		foreach (string name in new[] { "key", "tempo", "capo", "time", "year", "duration", "tag" })
		{
			var entry = Entry(form, name);
			entry.Text = string.Empty;
			string expected = name switch
			{
				"key" => "Dm", "tempo" => "###", "capo" => "#", "time" => "n/m",
				"year" => "yyyy", "duration" => "m:ss", _ => "Practice; Live",
			};
			Check(entry.Placeholder == expected, "Metadata placeholder incorrect: " + name);
			NativeEntry native = (NativeEntry)entry.Handler!.PlatformView!;
			Check(native.PlaceholderText == entry.Placeholder, "Native placeholder missing: " + name);
			Check(PlaceholderPart(native).FontStyle == global::Windows.UI.Text.FontStyle.Italic, "Placeholder is not italic: " + name);
			entry.Text = name == "time" ? "4/4" : "2";
			Check(native.FontStyle == global::Windows.UI.Text.FontStyle.Normal, "Entered metadata is italic: " + name);
			entry.Text = string.Empty;
			Check(PlaceholderPart(native).FontStyle == global::Windows.UI.Text.FontStyle.Italic, "Clearing did not retain italic placeholder: " + name);
			Check(Equals(entry.PlaceholderColor, Microsoft.Maui.Controls.Application.Current!.Resources["AppPlaceholder"]), "Placeholder has incorrect color: " + name);
		}
		await Task.Delay(150);
		Check(Elements(form).OfType<Microsoft.Maui.Controls.Label>().Any(label => label.Text == "Duration"), "Duration caption missing");
		Check(!Elements(form).OfType<Microsoft.Maui.Controls.Label>().Any(label => label.Text == "Duration (m:ss)"), "Duration caption still has the format");
		await CaptureAsync(page, "song-metadata-placeholders.png");
		var application = Microsoft.Maui.Controls.Application.Current!;
		var resourceNames = new[] { "AppBackground", "AppText", "AppControl", "AppPlaceholder" };
		var originalColors = resourceNames.Select(name => application.Resources[name]).ToArray();
		try
		{
			application.Resources["AppBackground"] = Microsoft.Maui.Graphics.Color.FromArgb("#202020");
			application.Resources["AppText"] = Microsoft.Maui.Graphics.Color.FromArgb("#F2F2F2");
			application.Resources["AppControl"] = Microsoft.Maui.Graphics.Color.FromArgb("#353535");
			application.Resources["AppPlaceholder"] = Microsoft.Maui.Graphics.Color.FromArgb("#A0A0A0");
			((FrameworkElement)page.Handler!.PlatformView!).RequestedTheme = ElementTheme.Dark;
			await Task.Delay(150);
			Check(Equals(Entry(form, "time").PlaceholderColor, application.Resources["AppPlaceholder"]), "Dark placeholder color did not update");
			Check(PlaceholderPart((NativeEntry)Entry(form, "time").Handler!.PlatformView!).FontStyle == global::Windows.UI.Text.FontStyle.Italic, "Dark placeholder lost italics");
			await CaptureAsync(page, "song-metadata-placeholders-dark.png");
		}
		finally
		{
			for (int index = 0; index < resourceNames.Length; index++) application.Resources[resourceNames[index]] = originalColors[index];
			((FrameworkElement)page.Handler!.PlatformView!).RequestedTheme = ElementTheme.Default;
		}
		form.Load(original);
		Check(!form.HasChanges, "Placeholder geometry check dirtied metadata");
		window.Width = 750;
		await Task.Delay(150);
		string artistText = Entry(form, "artist").Text;
		ClickEdit(form, "artist");
		ContentDialog dialog = await FindDialogAsync(page);
		NativeList list = Descendants(dialog).OfType<NativeList>().Single();
		Check(list.SelectedItems.Count == 2 && list.SelectedItems.Contains("Current; Artist"), "Current artists not preselected");
		await CaptureAsync(dialog, "metadata-artist-dialog.png");
		typeof(SongEditorPage).GetMethod("HandleSave", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [null, EventArgs.Empty]);
		await Task.Delay(50);
		Check(!page.Completion.IsCompleted, "Page Save escaped an open metadata dialog");
		Check(Descendants(list).OfType<ListViewItem>().Count() < 100, "Large artist list is not virtualized");
		Search(dialog).Text = "Artist 09999";
		await WaitAsync(() => list.Items.Count == 1);
		list.SelectedItems.Add("Artist 09999");
		Search(dialog).Text = "No matching artist";
		await WaitAsync(() => list.Items.Count == 0);
		Invoke(dialog, "Cancel");
		await ClosedAsync(page);
		Check(Entry(form, "artist").Text == artistText, "Dialog Cancel edited artists");
		ClickEdit(form, "artist");
		dialog = await FindDialogAsync(page);
		Search(dialog).Text = "New; Artist";
		await InvokeAddAsync(dialog);
		Invoke(dialog, "Apply");
		await ClosedAsync(page);
		Check(CatalogValueList.Parse(Entry(form, "artist").Text).SequenceEqual(new[] { "Current; Artist", "Other Artist", "New; Artist" }),
			"Adding a literal artist split semicolons or discarded current checks");
		ClickEdit(form, "tag");
		dialog = await FindDialogAsync(page);
		list = Descendants(dialog).OfType<NativeList>().Single();
		list.SelectedItems.Add("Live");
		Invoke(dialog, "Apply");
		await ClosedAsync(page);
		Check(form.GetMetadata().Tags.SequenceEqual(new[] { "Practice", "Live" }), "Multi-select tags failed");
		ClickEdit(form, "album");
		dialog = await FindDialogAsync(page);
		Check(Descendants(dialog).OfType<NativeList>().Single().SelectedItems.Count == 2, "Legacy album values not preselected");
		Search(dialog).Text = "New; Album";
		await InvokeAddAsync(dialog);
		Check(Descendants(dialog).OfType<NativeList>().Single().SelectedItems.Count == 1, "Album dialog allowed a new multiple selection");
		Invoke(dialog, "Apply");
		await ClosedAsync(page);
		Check(form.GetMetadata().Scalars["album"].SequenceEqual(new[] { "New; Album" }), "Album replacement split the literal value");
		((FrameworkElement)page.Handler!.PlatformView!).RequestedTheme = ElementTheme.Dark;
		ClickEdit(form, "time");
		dialog = await FindDialogAsync(page);
		list = Descendants(dialog).OfType<NativeList>().Single();
		list.SelectedItems.Add("6/8");
		await WaitAsync(() => list.SelectedItems.Count == 1);
		await CaptureAsync(dialog, "metadata-time-dialog.png");
		Check(list.SelectedItems.Count == 1, "Time signature is not single-select");
		Invoke(dialog, "Apply");
		await ClosedAsync(page);
		Check(form.GetMetadata().Scalars["time"].Single() == "6/8", "Time signature edit failed");
		((FrameworkElement)page.Handler!.PlatformView!).RequestedTheme = ElementTheme.Default;
		Check(await File.ReadAllTextAsync(catalogPath) == before, "Metadata dialog wrote catalog before editor Save");
		form.Load(original);
		Check(!form.HasChanges, "Restoring original metadata did not clear dirty state");
		typeof(SongEditorPage).GetMethod("HandleCancel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [null, EventArgs.Empty]);
		await WaitAsync(() => owner.Navigation.NavigationStack.Count == 1);
		Check(await File.ReadAllTextAsync(catalogPath) == before, "Cancel changed the catalog");
		SongEditDocument saveOriginal = await session.GetSongEditAsync(songId);
		SongEditorPage savePage = new(saveOriginal, session);
		await owner.Navigation.PushAsync(savePage, false);
		await WaitAsync(() => (bool)typeof(SongEditorPage).GetField("ready", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(savePage)!);
		SongMetadataEditor saveForm = savePage.FindByName<SongMetadataEditor>("metadataEditor");
		saveForm.TitleEntry.Text = saveOriginal.Title + " metadata edit";
		Entry(saveForm, "artist").Text = CatalogValueList.Format(["Saved; Artist"]);
		typeof(SongEditorPage).GetMethod("HandleSave", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(savePage, [null, EventArgs.Empty]);
		Check(await savePage.Completion, "Editor Save did not complete");
		SongEditDocument savedEdit = await session.GetSongEditAsync(songId);
		Check(savedEdit.Title == saveOriginal.Title + " metadata edit" && savedEdit.Artists.SequenceEqual(new[] { "Saved; Artist" }), "Metadata Save did not persist fields");
		Check(savedEdit.Text == saveOriginal.Text && savedEdit.ContentHash == saveOriginal.ContentHash, "Metadata Save rewrote authored source");
		await session.SaveSongEditAsync(savedEdit, saveOriginal.Title, saveOriginal.Artists, saveOriginal.Tags, savedEdit.Text);
		window.Width = 800;
		window.Height = 800;
		Program.Trace("Responsive full editor, virtualized searchable checked lists, multi/single selection, new values and Cancel passed");
	}

	private static async Task VerifyPickerPlaceholderAsync(MainPage owner, BookSession session)
	{
		Program.Trace("Reproduce empty Tags placeholder becoming real text through the picker");
		SongEditorPage page = new(session);
		await owner.Navigation.PushAsync(page, false);
		await WaitAsync(() => (bool)typeof(SongEditorPage).GetField("ready", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!);
		SongMetadataEditor form = page.FindByName<SongMetadataEditor>("metadataEditor");
		var entry = Entry(form, "tag");
		NativeEntry native = (NativeEntry)entry.Handler!.PlatformView!;
		Check(PlaceholderPart(native).FontStyle == global::Windows.UI.Text.FontStyle.Italic, "New empty Tags hint is not italic");
		global::Windows.UI.Text.FontStyle? insertionStyle = null;
		native.BeforeTextChanging += (_, args) =>
		{
			if (args.NewText == "Christian") insertionStyle = native.FontStyle;
		};
		ClickEdit(form, "tag");
		ContentDialog dialog = await FindDialogAsync(page);
		Search(dialog).Text = "Christian";
		await InvokeAddAsync(dialog);
		Invoke(dialog, "Apply");
		await ClosedAsync(page);
		Check(entry.Text == "Christian", "Picker did not apply the tag");
		Program.Trace($"Picker tag font attributes {entry.FontAttributes}, native font style {native.FontStyle}, focused {entry.IsFocused}");
		Check(native.FontStyle == global::Windows.UI.Text.FontStyle.Normal, "Picker-applied tag retained placeholder italics");
		Check(insertionStyle == global::Windows.UI.Text.FontStyle.Normal, "Picker inserted real text using the italic placeholder font");
		await CaptureAsync(page, "picker-tag-font.png");
		byte[] appliedPixels = await EntryPixelsAsync(native);
		SongEditorPage reference = new(session);
		var referenceForm = reference.FindByName<SongMetadataEditor>("metadataEditor");
		var blank = new SongEditDocument(Guid.Empty, 0, string.Empty, [], [], null, null, string.Empty);
		referenceForm.Load(blank with { Tags = ["Christian"] });
		await owner.Navigation.PushAsync(reference, false);
		await WaitAsync(() => (bool)typeof(SongEditorPage).GetField("ready", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(reference)!);
		await Task.Delay(100);
		byte[] referencePixels = await EntryPixelsAsync((NativeEntry)Entry(referenceForm, "tag").Handler!.PlatformView!);
		await CaptureAsync(reference, "reopened-tag-font-reference.png");
		Check(appliedPixels.SequenceEqual(referencePixels), "Picker-applied tag renders differently from a freshly loaded regular tag");
		referenceForm.Load(blank);
		typeof(SongEditorPage).GetMethod("HandleCancel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reference, [null, EventArgs.Empty]);
		await WaitAsync(() => owner.Navigation.NavigationStack.LastOrDefault() == page);
		entry.Text = string.Empty;
		typeof(SongEditorPage).GetMethod("HandleCancel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [null, EventArgs.Empty]);
		await WaitAsync(() => owner.Navigation.NavigationStack.Count == 1);
	}

	private static TextBlock PlaceholderPart(NativeEntry entry)
		=> Descendants(entry).OfType<TextBlock>().Single(control => control.Name == "PlaceholderTextContentPresenter");

	private static async Task<byte[]> EntryPixelsAsync(NativeEntry entry)
	{
		await Task.Delay(100);
		var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
		await bitmap.RenderAsync(entry);
		return (await bitmap.GetPixelsAsync()).ToArray();
	}

	private static Microsoft.Maui.Controls.Grid GridRows(SongMetadataEditor form)
		=> (Microsoft.Maui.Controls.Grid)((Microsoft.Maui.Controls.VerticalStackLayout)form.Content).Children[0];
	private static void ShouldHaveRows(this Microsoft.Maui.Controls.Grid rows, int expected) => Check(rows.RowDefinitions.Count == expected,
		$"Expected {expected} metadata rows, got {rows.RowDefinitions.Count}");
	private static Microsoft.Maui.Controls.Entry Entry(SongMetadataEditor form, string name)
		=> Elements(form).OfType<Microsoft.Maui.Controls.Entry>().Single(entry => entry.AutomationId == "Metadata." + name);
	private static void ClickEdit(SongMetadataEditor form, string name)
		=> ((Microsoft.Maui.Controls.IButtonController)Elements(form).OfType<FluentIconButton>().Single(button => button.AutomationId == "Metadata.Edit." + name)).SendClicked();
	private static void AssertBounds(SongMetadataEditor form)
	{
		FrameworkElement root = (FrameworkElement)form.Handler!.PlatformView!;
		foreach (var entry in Elements(form).OfType<Microsoft.Maui.Controls.Entry>())
		{
			FrameworkElement native = (FrameworkElement)entry.Handler!.PlatformView!;
			var point = native.TransformToVisual(root).TransformPoint(new(0, 0));
			Check(native.ActualWidth >= 32 && point.X >= -1 && point.X + native.ActualWidth <= root.ActualWidth + 1,
				"Metadata entry clipped: " + entry.AutomationId);
		}
		foreach (FluentIconButton button in Elements(form).OfType<FluentIconButton>())
		{
			string name = button.AutomationId["Metadata.Edit.".Length..];
			FrameworkElement entry = (FrameworkElement)Entry(form, name).Handler!.PlatformView!;
			FrameworkElement edit = (FrameworkElement)button.Handler!.PlatformView!;
			double right = entry.TransformToVisual(root).TransformPoint(new(0, 0)).X + entry.ActualWidth;
			double left = edit.TransformToVisual(root).TransformPoint(new(0, 0)).X;
			Check(left - right >= 0 && left - right <= 2.1, "Edit button is not grouped with its field: " + name);
		}
		foreach (var label in Elements(form).OfType<Microsoft.Maui.Controls.Label>().Where(label => label.IsVisible))
			Check(label.Height < 30, "Metadata label wrapped vertically: " + label.Text);
	}
	private static IEnumerable<Microsoft.Maui.IVisualTreeElement> Elements(Microsoft.Maui.IVisualTreeElement root)
	{
		yield return root;
		foreach (var child in root.GetVisualChildren()) foreach (var descendant in Elements(child)) yield return descendant;
	}
	private static NativeEntry Search(ContentDialog dialog) => Descendants(dialog).OfType<NativeEntry>().Single();
	private static async Task InvokeAddAsync(ContentDialog dialog)
	{
		NativeButton add = Descendants(dialog).OfType<NativeButton>().Single(button =>
			Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == "CatalogValueAdd");
		await WaitAsync(() => add.IsEnabled);
		((IInvokeProvider)new ButtonAutomationPeer(add).GetPattern(PatternInterface.Invoke)).Invoke();
		await WaitAsync(() => Search(dialog).Text.Length == 0);
	}
	private static void Invoke(ContentDialog dialog, string text)
	{
		NativeButton button = Descendants(dialog).OfType<NativeButton>().Single(button => Equals(button.Content, text));
		((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
	}
	private static async Task<ContentDialog> FindDialogAsync(Microsoft.Maui.Controls.Page page)
	{
		ContentDialog? found = null;
		await WaitAsync(() => (found = Popups(page).SingleOrDefault()) is { ActualWidth: > 0 });
		return found!;
	}
	private static IEnumerable<ContentDialog> Popups(Microsoft.Maui.Controls.Page page)
		=> VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)page.Handler!.PlatformView!).XamlRoot)
			.SelectMany(popup => Descendants(popup.Child)).OfType<ContentDialog>();
	private static Task ClosedAsync(SongEditorPage page) => WaitAsync(() => !Popups(page).Any()
		&& !page.FindByName<SongMetadataEditor>("metadataEditor").IsSelecting);
}
