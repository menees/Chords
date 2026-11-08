using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui;
using Menees.Chords.Book.Maui.Platforms.Windows;
using Menees.Chords.Book.Maui.Services;
using Menees.Chords.Db;
using Microsoft.Maui.Hosting;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using NativeElement = Microsoft.UI.Xaml.FrameworkElement;

namespace SetlistSmoke;

internal static class Program
{
	[STAThread]
	static void Main()
	{
		AppDomain.CurrentDomain.UnhandledException += (_, args) => Result(args.ExceptionObject.ToString()!);
		WinRT.ComWrappersSupport.InitializeComWrappers();
		Microsoft.UI.Xaml.Application.Start(_ =>
		{
			SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
				Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
			try { new SmokeWindowsApp(); }
			catch (Exception error) { Result(error.ToString()); Microsoft.UI.Xaml.Application.Current?.Exit(); }
		});
	}
	internal static void Result(string value) => File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "result.txt"), value);
	internal static void Trace(string value) => File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "trace.txt"), value + "\r\n");
}

public sealed partial class SmokeWindowsApp : Microsoft.Maui.MauiWinUIApplication
{
	private readonly WindowsFileActivation activation = new();
	internal static IReadOnlyList<string> StartupFiles { get; private set; } = [];
	internal static TaskCompletionSource<IReadOnlyList<string>> Redirected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private bool launched;

	public SmokeWindowsApp()
	{
		WindowsWebViewProfile.Configure(Path.Combine(AppContext.BaseDirectory, "LocalProfile"));
		this.InitializeComponent();
		_ = Task.Run(async () =>
		{
			await Task.Delay(TimeSpan.FromSeconds(150));
			Program.Result("Timeout: " + File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "trace.txt")));
			Environment.Exit(2);
		});
		this.UnhandledException += (_, args) => { Program.Result(args.Exception.ToString()); args.Handled = true; this.Exit(); };
	}
	protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
	{
		if (!await this.activation.StartAsync(paths =>
		{
			if (!this.launched) StartupFiles = paths;
			else Redirected.TrySetResult(paths);
		})) { this.Exit(); return; }
		base.OnLaunched(args);
		this.activation.DeliverInitial();
		this.launched = true;
	}

	protected override MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiApp<SmokeMauiApp>()
		.ConfigureMauiHandlers(handlers => handlers
			.AddHandler<TransposePicker, TransposePickerHandler>()
			.AddHandler<FluentIconButton, FluentIconButtonHandler>()
			.AddHandler<ArchiveToggle, FluentIconToggleHandler>()
			.AddHandler<FluentIconToggle, FluentIconToggleHandler>()
			.AddHandler<BookTabs, BookTabsHandler>()
			.AddHandler<PositionEntry, PositionEntryHandler>()
			.AddHandler<OutputEditor, OutputEditorHandler>()
			.AddHandler<OpenBookSplitButton, FluentIconSplitButtonHandler>()
			.AddHandler<FluentIconSplitButton, FluentIconSplitButtonHandler>()).Build();
}

public sealed partial class SmokeMauiApp : Microsoft.Maui.Controls.Application
{
	public SmokeMauiApp() => this.InitializeComponent();
	protected override Microsoft.Maui.Controls.Window CreateWindow(Microsoft.Maui.IActivationState? activationState)
	{
		var loading = new ContentPage { Content = new Label { Text = "Disposable setlist acceptance" } };
		var window = new Microsoft.Maui.Controls.Window(loading) { Width = 1100, Height = 800 };
		loading.Loaded += async (_, _) =>
		{
			try { await RunAsync(window); }
			catch (Exception error) { Program.Result(error.ToString()); }
			finally { Microsoft.UI.Xaml.Application.Current.Exit(); }
		};
		return window;
	}

	private static async Task RunAsync(Microsoft.Maui.Controls.Window window)
	{
		await NativeBookStartupSmoke.RunAsync(window);
		await NativeSongBrowserSmoke.RunAsync(window);
		Program.Trace("Create synthetic book");
		string root = Path.Combine(AppContext.BaseDirectory, "DisposableBooks");
		using FileSystemBookStore store = new(root);
		Guid device = Guid.NewGuid();
		BookLocation location = await store.CreateBookAsync("Disposable setlist acceptance", device);
		BookApplicationSession app = new();
		await app.ActivateAsync(store, location);
		Guid zulu = await app.CreateSongAsync("Zulu", ["Synthetic Artist"], ["Acoustic"],
			"{key: Bb}\n{tempo: 108}\n{capo: 2}\n{duration: 3:08}\n[Bb]Zulu", device);
		Guid alpha = await app.CreateSongAsync("Alpha", ["Synthetic Artist"], [], "[G]Alpha", device);
		Guid list = await app.CreateSetlistAsync("Practice", device);
		IReadOnlyList<Guid> original = await app.AddSongsToSetlistAsync(list, [zulu, alpha, zulu], device);
		SetlistEdit dated = app.GetSetlistEdit(list);
		dated.Value.Date = new DateOnly(2026, 11, 7);
		await app.SaveSetlistEditAsync(dated, device);
		using BookSession session = new(app, device);
		typeof(BookSession).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, store);
		typeof(BookSession).GetField("location", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, location);
		typeof(BookSession).GetProperty("DirectoryPath")!.SetValue(session, store.GetDirectory(location));
		using SilentMetronome metronome = new();
		MainPage page = new(session, new NoPicker(), metronome);
		NavigationPage.SetHasNavigationBar(page, false);
		window.Page = new NavigationPage(page);
		await WaitAsync(() => page.FindByName<Label>("Status").Text.StartsWith("Ready"));
		Call(page, "ShowManagementTab", true, false);
		var overview = page.FindByName<CatalogGrid>("SetlistHeader");
		Check(overview.IsVisible && overview.Fields == (CatalogColumn.Songs | CatalogColumn.Date | CatalogColumn.Duration),
			"Setlist overview did not show its date, song count and partial duration columns");
		CatalogCell durationHeader = overview.Children.OfType<CatalogCell>().Single(cell => cell.Text == "Duration");
		Check(overview.ColumnDefinitions[Grid.GetColumn((BindableObject)durationHeader)].Width.IsAbsolute
			&& overview.ColumnDefinitions[0].Width.IsAbsolute, "Setlist overview did not fit the final Duration column");
		SetlistRow setlist = session.GetSetlists().Single();
		Check(setlist.DurationText == "6:16 (2/3 timed)", "Setlist duration did not identify partial timing");
		await CaptureAsync(page, "setlist-columns-overview.png");
		Call(page, "OpenSetlist", list);
		var entryHeader = page.FindByName<CatalogGrid>("EntrySongHeader");
		Check((entryHeader.Fields & (CatalogColumn.Key | CatalogColumn.Capo | CatalogColumn.Tags))
			== (CatalogColumn.Key | CatalogColumn.Capo | CatalogColumn.Tags), "Setlist detail lost catalog columns");
		await CaptureAsync(page, "setlist-columns-detail.png");
		string initial = await store.ReadDatabaseJsonAsync(location);
		Click(page, "EditSetlistButton");
		await WaitAsync(() => page.FindByName<FluentIconButton>("EditSetlistButton").Description == "Save");
		Check(page.FindByName<FluentIconButton>("SetlistEditorBackButton").IsVisible, "Editor Back missing");
		Check(!page.FindByName<FluentIconButton>("ManagementBackButton").IsVisible, "Duplicate Back");
		Check(!page.FindByName<Grid>("ManagementBookToolbar").IsVisible, "Book toolbar must not bypass editor");
		var rows = (ObservableCollection<SetlistEntryRow>)page.FindByName<CollectionView>("SetlistEntries").ItemsSource;
		rows.Move(2, 0);
		Call(page, "HandleSetlistReorderCompleted", null, EventArgs.Empty);
		Click(page, "SortSetlistButton");
		await WaitAsync(() => !Busy(page));
		Check(await store.ReadDatabaseJsonAsync(location) == initial, "Sorting wrote before Save");
		rows = (ObservableCollection<SetlistEntryRow>)page.FindByName<CollectionView>("SetlistEntries").ItemsSource;
		Check(rows.Select(row => row.EntryId).SequenceEqual(new[] { original[1], original[2], original[0] }), "Sort lost repeated occurrence ordering");
		await WaitAsync(() => Descendants((NativeElement)page.Handler!.PlatformView!).OfType<Microsoft.UI.Xaml.Controls.TextBlock>()
			.Count(t => t.Text == "Zulu" && t.ActualWidth > 0) >= 2);
		await CaptureAsync(page, "setlist-editor.png");
		Click(page, "EditSetlistButton");
		await WaitAsync(() => !Busy(page) && page.FindByName<FluentIconButton>("EditSetlistButton").Description == "Edit");
		Check(page.FindByName<Grid>("SetlistDetail").IsVisible, "Save left current setlist");
		string saved = await store.ReadDatabaseJsonAsync(location);
		Check(saved != initial, "Save did not persist sorting");
		Program.Trace("Sort and Save passed; verify discard and selection return");
		Click(page, "EditSetlistButton");
		await WaitAsync(() => !Busy(page));
		Click(page, "ArchiveSetlistButton");
		await WaitAsync(() => !Busy(page));
		Check(await store.ReadDatabaseJsonAsync(location) == saved, "Archive wrote before Save");
		Click(page, "SetlistEditorBackButton");
		await ChooseAsync(page, "Keep Editing");
		await WaitAsync(() => !(bool)Field(page, "leavingSetlistEdit")!);
		Check(page.FindByName<FluentIconButton>("EditSetlistButton").Description == "Save", "Keep Editing left editor");
		Click(page, "AddSetlistSongsButton");
		await WaitAsync(() => (bool)Field(page, "selectingSongs")!);
		Click(page, "SelectAllSongsButton");
		Check(page.FindByName<CollectionView>("SongList").SelectedItems.Count == 2, "Select All failed during setlist addition");
		Click(page, "UnselectAllSongsButton");
		Check(page.FindByName<CollectionView>("SongList").SelectedItems.Count == 0, "Unselect All failed during setlist addition");
		Check(await store.ReadDatabaseJsonAsync(location) == saved, "Selection changed the unsaved setlist");
		page.FindByName<CollectionView>("SongList").SelectedItems = [((IReadOnlyList<SongRow>)Field(page, "visibleSongs")!)[0]];
		Click(page, "AddSelectedSongsButton");
		await WaitAsync(() => !(bool)Field(page, "selectingSongs")! && !Busy(page));
		Check(((SetlistEdit)Field(page, "setlistEdit")!).Value.Entries.Count == 4, "Add Songs did not update draft");
		Check(await store.ReadDatabaseJsonAsync(location) == saved, "Add Songs wrote before Save");
		Click(page, "AddSetlistSongsButton");
		await WaitAsync(() => (bool)Field(page, "selectingSongs")!);
		Click(page, "ManagementBackButton");
		await WaitAsync(() => !(bool)Field(page, "selectingSongs")!);
		Check(page.FindByName<Grid>("SetlistDetail").IsVisible && page.FindByName<FluentIconButton>("EditSetlistButton").Description == "Save",
			"Selection cancel lost editor context");
		Check(((SetlistEdit)Field(page, "setlistEdit")!).HasChanges, "Selection cancel discarded pending changes");
		Click(page, "SetlistEditorBackButton");
		await ChooseAsync(page, "Discard Changes");
		await WaitAsync(() => page.FindByName<FluentIconButton>("EditSetlistButton").Description == "Edit");
		Check(page.FindByName<Grid>("SetlistDetail").IsVisible, "Discard left current setlist");
		Check(await store.ReadDatabaseJsonAsync(location) == saved, "Discard changed catalog");
		Click(page, "EditSetlistButton");
		await WaitAsync(() => !Busy(page));
		Click(page, "SetlistEditorBackButton");
		await WaitAsync(() => page.FindByName<FluentIconButton>("EditSetlistButton").Description == "Edit");
		Program.Trace("Verify save failure retains draft and retry succeeds");
		Click(page, "EditSetlistButton");
		await WaitAsync(() => !Busy(page));
		((SetlistEdit)Field(page, "setlistEdit")!).Value.Name = "Retained after failure";
		using (FileStream locked = File.Open(Path.Combine(store.GetDirectory(location), "database.json"), FileMode.Open, FileAccess.Read, FileShare.None))
		{
			Click(page, "EditSetlistButton");
			await WaitAsync(() => !Busy(page));
			Check(page.FindByName<FluentIconButton>("EditSetlistButton").Description == "Save", "Failed save closed editor");
			Check(((SetlistEdit)Field(page, "setlistEdit")!).Value.Name == "Retained after failure", "Failed save lost draft");
		}
		Check(await store.ReadDatabaseJsonAsync(location) == saved, "Failed save modified catalog");
		Click(page, "EditSetlistButton");
		await WaitAsync(() => !Busy(page) && page.FindByName<FluentIconButton>("EditSetlistButton").Description == "Edit");
		Check(DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location)).Setlists.Single().Name == "Retained after failure", "Save retry failed");
		window.Width = 800;
		await Task.Delay(150);
		await CaptureAsync(page, "setlist-detail-narrow.png");
		await NativeViewSortSmoke.RunAsync(page, session, app, store, location);
		await NativeMetadataImportSmoke.RunAsync(page);
		await NativeSongMetadataSmoke.RunAsync(page, session);
		await NativeSongMetadataSmoke.RunBulkTagsAsync(page, session);
		await NativeImportReviewSmoke.RunAsync(page, session);
		await NativeFileOpeningSmoke.RunAsync(page, session, app, store, location);
		await NativeOutputEditorSmoke.RunAsync(window, session);
		Program.Result("PASS: Wide and compact song columns, empty filtered columns, setlist overview/detail columns and partial durations; filtered/grouped/flat Select All and Unselect All, batched 721-song selection, narrow toolbar and setlist-addition selection; setlist Select Songs, compact toolbar, Tags column and selection Edit Tags union/add/remove/clear-all, cancel/no source changes and exclusion search; mixed import review failure categories/named errors/valid Apply; grouped/flat browser row preservation after actual text Save/Back and stale-search cancellation; legacy-source startup/recent books, file activation/opening, metadata directive review, view date sorting, output auto-scroll and production setlist Sort/Save, stable repeated entries, no writes before Save, editor Back, Keep Editing, staged song addition, song-selection cancel, Discard, unchanged Back and failed-save/retry all passed on a disposable book.");
	}

	internal static object? Field(MainPage page, string name) => typeof(MainPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
	internal static object? Call(MainPage page, string name, params object?[] args) => typeof(MainPage).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, args);
	private static bool Busy(MainPage page) => (bool)Field(page, "bookMutationInProgress")!;
	private static void Click(MainPage page, string name) { Program.Trace("Click " + name); ((IButtonController)page.FindByName<Button>(name)).SendClicked(); }
	internal static void Check(bool value, string message) { if (!value) throw new Exception(message); }
	internal static async Task WaitAsync(Func<bool> ready)
	{
		for (int attempt = 0; attempt < 300; attempt++) { if (ready()) return; await Task.Delay(20); }
		throw new Exception("Setlist editor did not reach expected state. " + File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "trace.txt")));
	}
	internal static IEnumerable<Microsoft.UI.Xaml.DependencyObject> Descendants(Microsoft.UI.Xaml.DependencyObject parent)
	{
		yield return parent;
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
			foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
	}
	private static async Task ChooseAsync(MainPage page, string caption)
	{
		Program.Trace("Choose " + caption + "; status=" + page.FindByName<Label>("Status").Text + "; busy=" + Busy(page)
			+ "; leaving=" + Field(page, "leavingSetlistEdit") + "; editing=" + Field(page, "editingSetlist"));
		var root = (NativeElement)page.Handler!.PlatformView!;
		NativeElement? choice = null;
		await WaitAsync(() =>
		{
			var elements = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).SelectMany(p => Descendants(p.Child)).ToArray();
			choice = elements.OfType<Microsoft.UI.Xaml.Controls.Button>().FirstOrDefault(b => Equals(b.Content, caption));
			choice ??= elements.OfType<Microsoft.UI.Xaml.Controls.ListViewItem>()
				.FirstOrDefault(item => Equals(item.Content, caption) || Descendants(item).OfType<Microsoft.UI.Xaml.Controls.TextBlock>().Any(t => t.Text == caption));
			return choice is { ActualWidth: > 0 };
		});
		var peer = FrameworkElementAutomationPeer.CreatePeerForElement(choice!);
		if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
		else if (peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selection) selection.Select();
		else throw new Exception("Action sheet choice has no invocation/selection pattern: " + choice!.GetType());
		await WaitAsync(() => VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).Count == 0);
		await Task.Delay(100);
	}
	internal static Task CaptureAsync(Microsoft.Maui.Controls.Page page, string name)
		=> CaptureAsync((Microsoft.UI.Xaml.UIElement)page.Handler!.PlatformView!, name);

	internal static async Task CaptureAsync(Microsoft.UI.Xaml.UIElement view, string name)
	{
		await Task.Delay(100);
		var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
		await bitmap.RenderAsync(view);
		byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
		Check(pixels.Length > 0, "Empty native page capture");
		using FileStream output = File.Create(Path.Combine(AppContext.BaseDirectory, name));
		var encoder = await global::Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(global::Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, output.AsRandomAccessStream());
		encoder.SetPixelData(global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, global::Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
			(uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
		await encoder.FlushAsync();
	}
}

internal sealed class NoPicker : IWindowsPicker
{
	public Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
	public Task<string?> PickFolderAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
	public Task<string?> PickFileAsync(string extension, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
	public Task<string?> SaveFileAsync(string suggestedName, string extension, string description, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
	public Task OpenFolderAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
}
internal sealed class SilentMetronome : IMetronomeEngine
{
	public MetronomeSettings CurrentSettings { get; } = new();
	public bool IsRunning => false;
	public int CurrentBeat => 0;
	public int BeatsPerMeasure => 4;
	public bool VisualEnabled => false;
	public bool AccentFirstBeat => true;
	public MetronomeVisualState? VisualPulse => null;
	public Task StartAsync(MetronomeSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
	public void Stop() { }
	public void Dispose() { }
}
