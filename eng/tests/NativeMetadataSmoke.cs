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
		var page = new ContentPage { Content = new Label { Text = "Disposable comparison and saved-tab acceptance" } };
		bool started = false;
		page.Loaded += async (_, _) =>
		{
			if (started) return;
			started = true;
			try
			{
				SynchronizationContext.SetSynchronizationContext(new UiContext());
				await NativeSavedTabSmoke.RunAsync(page);
				this.UserAppTheme = Microsoft.Maui.ApplicationModel.AppTheme.Dark;
				this.Resources["AppBackground"] = Microsoft.Maui.Graphics.Color.FromArgb("#202020");
				this.Resources["AppText"] = Microsoft.Maui.Graphics.Color.FromArgb("#F2F2F2");
				this.Resources["AppControl"] = Microsoft.Maui.Graphics.Color.FromArgb("#353535");
				if (this.Windows[0].Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow
					&& nativeWindow.Content is Microsoft.UI.Xaml.FrameworkElement root)
				{
					root.RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark;
				}

				await NativeComparisonSmoke.RunAsync(page);
				File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "result.txt"), "PASS: Production comparison text/PDF/mixed/source-handle checks and native saved-tab defaults/sorting/error/retry/cancel.");
			}
			catch (Exception error) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "result.txt"), error.ToString()); }
			finally { Microsoft.UI.Xaml.Application.Current.Exit(); }
		};
		return new(page) { Width = 1100, Height = 800 };
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
