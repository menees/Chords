using System.Runtime.InteropServices.WindowsRuntime;
using System.Xml.Linq;
using Microsoft.Maui.Controls.Xaml;
using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui;
using Menees.Chords.Db;
using Microsoft.Maui.Hosting;

namespace MetadataSmoke;

internal static class Program
{
	[STAThread]
	static void Main()
	{
		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
			File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "result.txt"), args.ExceptionObject.ToString());
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "trace.txt"), "Starting");
		WinRT.ComWrappersSupport.InitializeComWrappers();
		Microsoft.UI.Xaml.Application.Start(_ =>
		{
			SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
				Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
			try
			{
				new SmokeWindowsApp();
			}
			catch (Exception error)
			{
				File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "result.txt"), error.ToString());
				Microsoft.UI.Xaml.Application.Current?.Exit();
			}
		});
	}
}

public sealed partial class SmokeWindowsApp : Microsoft.Maui.MauiWinUIApplication
{
	public SmokeWindowsApp()
	{
		Menees.Chords.Book.Maui.Platforms.Windows.WindowsWebViewProfile.Configure(Path.Combine(AppContext.BaseDirectory, "LocalProfile"));
		this.InitializeComponent();
		var timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
		timer.Interval = TimeSpan.FromSeconds(45);
		timer.IsRepeating = false;
		timer.Tick += (_, _) =>
		{
			File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "result.txt"), "Timeout: " + File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "trace.txt")));
			this.Exit();
		};
		timer.Start();
		this.UnhandledException += (_, args) =>
		{
			File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "result.txt"), args.Exception.ToString());
			args.Handled = true;
			this.Exit();
		};
	}

	protected override MauiApp CreateMauiApp()
		{
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "trace.txt"), "Building MAUI app");
		return MauiApp.CreateBuilder().UseMauiApp<SmokeMauiApp>()
			.ConfigureMauiHandlers(handlers => handlers.AddHandler<FluentIconButton, Menees.Chords.Book.Maui.Platforms.Windows.FluentIconButtonHandler>()).Build();
	}
}

public sealed partial class SmokeMauiApp : Microsoft.Maui.Controls.Application
{
	public SmokeMauiApp() => this.InitializeComponent();

	protected override Microsoft.Maui.Controls.Window CreateWindow(Microsoft.Maui.IActivationState? activationState)
	{
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "trace.txt"), "Creating window");
		var entry = new Microsoft.Maui.Controls.Entry { Text = "  Cafe\u0301 ", WidthRequest = 240 };
		MetadataValuePicker picker = new() { Title = "Add existing artist", TargetEntry = entry, AppendValue = true, WidthRequest = 220 };
		ChordDatabase database = new();
		Song song = new() { Artists = ["Café", "AC; DC"] };
		song.MetadataOverrides["album"] = ["Catalog album; part"];
		song.MetadataOverrides["time"] = ["6/8"];
		database.Songs.Add(song);
		BookMetadataValueIndex values = new(database);
		picker.LoadValues(values.GetValues("artist"));
		ScalarMetadataEditor scalars = new();
		SongEditDocument document = new(Guid.NewGuid(), 1, "Disposable", [], [], null, null, null)
		{
			Metadata = new Dictionary<string, IReadOnlyList<string>>
			{
				["album"] = new[] { "Original; album", "Other album" },
				["time"] = new[] { "4/4" },
			},
		};
		scalars.Load(document);
		scalars.LoadSuggestions(values);
		var content = new Microsoft.Maui.Controls.VerticalStackLayout
		{
			Padding = 16,
			Spacing = 8,
			Children = { new Microsoft.Maui.Controls.Label { Text = "Disposable metadata controls", FontSize = 22 }, entry, picker, scalars },
		};
		var page = new Microsoft.Maui.Controls.ContentPage { Content = content };
		bool started = false;
		page.Loaded += async (_, _) =>
		{
			if (started) return;
			started = true;
			File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "trace.txt"), "Page loaded");
			SynchronizationContext.SetSynchronizationContext(new UiContext());
			try
			{
				await Task.Delay(250);
				if (picker.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.ComboBox native || native.ActualWidth <= 0 || native.ActualHeight <= 0)
					throw new Exception("Picker did not create a visible native control.");
				File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "trace.txt"), "Selecting duplicate artist");
				picker.SelectedIndex = picker.Items.IndexOf("Café");
				await Task.Delay(50);
				if (entry.Text != "  Cafe\u0301 ") throw new Exception("Duplicate selection rewrote an unchanged entry.");
				File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "trace.txt"), "Selecting delimiter artist");
				picker.SelectedIndex = picker.Items.IndexOf("AC; DC");
				await Task.Delay(50);
				if (!CatalogValueList.Parse(entry.Text).SequenceEqual(new[] { "Café", "AC; DC" }.Select((value, index) => index == 0 ? "Cafe\u0301" : value)))
					throw new Exception("Artist selection split an embedded delimiter.");
				string? rejected = null;
				picker.SelectionRejected += message => rejected = message;
				entry.Text = "\"Unclosed";
				picker.SelectedIndex = 0;
				await Task.Delay(50);
				if (rejected is null || entry.Text != "\"Unclosed") throw new Exception("Invalid entry was silently replaced.");
				if (!scalars.GetValues()["album"].SequenceEqual(document.Metadata["album"])) throw new Exception("Untouched multi-value album changed.");
				var scalarBody = (Microsoft.Maui.Controls.VerticalStackLayout)scalars.Content;
				var scalarGrid = (Microsoft.Maui.Controls.Grid)scalarBody.Children[0];
				MetadataValuePicker album = scalarGrid.Children.OfType<Microsoft.Maui.Controls.Grid>()
					.SelectMany(cell => cell.Children.OfType<MetadataValuePicker>()).Single(value => value.AutomationId == "albumChoices");
				File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "trace.txt"), "Selecting album");
				album.SelectedIndex = 0;
				await Task.Delay(50);
				if (!scalars.GetValues()["album"].SequenceEqual(new[] { "Catalog album; part" })) throw new Exception("Album selection split an embedded delimiter.");
				if (scalars.GetValues()["time"].Single() != "4/4") throw new Exception("Album selection changed time signature.");
				entry.Text = CatalogValueList.Format(["Café", "AC; DC"]);
				this.UserAppTheme = Microsoft.Maui.ApplicationModel.AppTheme.Light;
				await Task.Delay(250);
				XElement metadataElement = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "SongEditorPage.xaml"))
					.Descendants().Single(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "metadataGrid"));
				metadataElement.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Remove();
				var metadataGrid = new Microsoft.Maui.Controls.Grid().LoadFromXaml(metadataElement.ToString().Replace("clr-namespace:Menees.Chords.Book.Maui",
					"clr-namespace:Menees.Chords.Book.Maui;assembly=" + typeof(MetadataValuePicker).Assembly.GetName().Name, StringComparison.Ordinal));
				content.Children.Remove(scalars);
				scalars.AttachTo(metadataGrid);
				content.Children.Add(metadataGrid);
				this.Windows[0].Width = 1908;
				await Task.Delay(300);
				AssertAlignedRows(metadataGrid);
				this.Windows[0].Width = 1100;
				await Task.Delay(200);
				AssertAlignedRows(metadataGrid);
				this.Windows[0].Width = 1908;
				await Task.Delay(200);
				File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "trace.txt"), "Capturing light");
				await CaptureAsync(page, "metadata-light.png");
				this.UserAppTheme = Microsoft.Maui.ApplicationModel.AppTheme.Dark;
				this.Resources["AppBackground"] = Microsoft.Maui.Graphics.Color.FromArgb("#202020");
				this.Resources["AppText"] = Microsoft.Maui.Graphics.Color.FromArgb("#F2F2F2");
				this.Resources["AppControl"] = Microsoft.Maui.Graphics.Color.FromArgb("#353535");
				if (this.Windows[0].Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow
					&& nativeWindow.Content is Microsoft.UI.Xaml.FrameworkElement root)
				{
					root.RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark;
				}
				await Task.Delay(250);
				File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "trace.txt"), "Capturing dark");
				if (!Equals(picker.TitleColor, this.Resources["AppText"])) throw new Exception("Picker title ignored the dark palette.");
				AssertAlignedRows(metadataGrid);
				await CaptureAsync(page, "metadata-dark.png");
				File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "trace.txt"), "Native saved-tab dialog");
				await NativeSavedTabSmoke.RunAsync(page);
				File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "trace.txt"), "Native comparison pages");
				await NativeComparisonSmoke.RunAsync(page);
				File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "result.txt"),
					"PASS: Native MAUI metadata selectors, duplicate preservation, quoted values, invalid-entry reporting and untouched scalar metadata. Shared production editor input rows at 1,100/1,908 widths and light/dark captures; production comparison text, PDF paging/painting, mixed preview and released source handles; saved-tab defaults, sorting, failed-save retention, retry, edit and cancel.");
			}
			catch (Exception error)
			{
				File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "result.txt"), error.ToString());
			}
			finally
			{
				Microsoft.UI.Xaml.Application.Current.Exit();
			}
		};
		return new(page) { Width = 800, Height = 700 };
	}

	static void AssertAlignedRows(Microsoft.Maui.Controls.Grid metadataGrid)
	{
		var root = (Microsoft.UI.Xaml.FrameworkElement)metadataGrid.Handler!.PlatformView!;
		foreach (int row in new[] { 1, 3, 5, 7 })
		{
			var controls = metadataGrid.Children.Where(child => Microsoft.Maui.Controls.Grid.GetRow((Microsoft.Maui.Controls.BindableObject)child) == row)
				.SelectMany(child => child is Microsoft.Maui.Controls.Grid cell ? cell.Children : new[] { child })
				.OfType<Microsoft.Maui.Controls.View>().Where(child => child is Microsoft.Maui.Controls.Entry or MetadataValuePicker).ToArray();
			var tops = controls.Select(control => ((Microsoft.UI.Xaml.FrameworkElement)control.Handler!.PlatformView!)
				.TransformToVisual(root).TransformPoint(new global::Windows.Foundation.Point()).Y).ToArray();
			if (tops.Length < 2 || tops.Max() - tops.Min() > 1.1) throw new Exception("Metadata inputs are staggered in row " + row + ": " + string.Join(",", tops));
		}
	}

	static async Task CaptureAsync(Microsoft.Maui.Controls.ContentPage page, string name)
	{
		var view = (Microsoft.UI.Xaml.UIElement)page.Handler!.PlatformView!;
		Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap bitmap = new();
		await bitmap.RenderAsync(view);
		byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
		if (!pixels.Where((value, index) => index % 4 == 3).Any(value => value > 0)) throw new Exception("Blank metadata capture: " + name);
		using FileStream output = File.Create(Path.Combine(AppContext.BaseDirectory, name));
		var encoder = await global::Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(global::Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, output.AsRandomAccessStream());
		encoder.SetPixelData(global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, global::Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
			(uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
		await encoder.FlushAsync();
	}
}

sealed class UiContext : SynchronizationContext
{
	private readonly Microsoft.UI.Dispatching.DispatcherQueue queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
	public override void Post(SendOrPostCallback callback, object? state)
		=> this.queue.TryEnqueue(() =>
		{
			SetSynchronizationContext(this);
			callback(state);
		});
}
