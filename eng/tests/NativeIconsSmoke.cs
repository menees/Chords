using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Menees.Chords.Book.Maui.Platforms.Windows;
internal static class Program
{
	[STAThread]
	static void Main()
	{
		WinRT.ComWrappersSupport.InitializeComWrappers();
		Microsoft.UI.Xaml.Application.Start(_ => new SmokeApp());
	}
}
sealed class SmokeApp : Microsoft.UI.Xaml.Application
{
	public SmokeApp()
	{
		Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(async () =>
		{
			try
			{
				SynchronizationContext.SetSynchronizationContext(new UiContext());

				string[] names = ["ArrowLeft", "Previous", "Next", "LayerDiagonalAdd", "ArrowRight", "EditLineHorizontal3",
					"SoundWaveCircleSparkle", "LockClosed", "LockOpen", "BookAdd", "Book", "Settings", "ArrowImport",
					"Filter", "MultiselectLtr", "DocumentOnePageAdd", "LayerDiagonal", "ArrowReset", "Archive", "Play", "Stop", "Save", "Dismiss", "Search", "SearchSparkle", "ArrowUndo", "ArrowRedo", "PreviewLink", "Add", "Delete", "ArrowUp", "ArrowDown", "Rename", "ArchiveArrowBack", "Checkmark", "ArrowClockwise", "Subtract", "Metronome", "AttachArrowRight", "Learn", "Piano", "AcousticGuitar", "Banjo", "BassGuitar", "ElectricGuitar", "Mandolin", "Ukulele", "Star", "ArrowSync", "CloudArrowDown", "PlugConnected", "PlugDisconnected", "BracesVariable"];
				Microsoft.UI.Xaml.Window window = new();
				StackPanel panel = new() { Orientation = Orientation.Horizontal };
				window.Content = panel;
				TaskCompletionSource loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
				panel.Loaded += (_, _) => loaded.TrySetResult();
				window.Activate();
				await loaded.Task;

				foreach (string name in names)
				{
					var icon = FluentIconSource.Create(name);
					if (icon.Data is null || icon.Data.Bounds.Width <= 0 || icon.Data.Bounds.Height <= 0 || icon.Width != 20 || icon.Height != 20
						|| icon.Data.Bounds.Width > 20.01 || icon.Data.Bounds.Height > 20.01
						|| icon.Data.Bounds.X < -0.01 || icon.Data.Bounds.Y < -0.01
						|| icon.Data.Bounds.Right > 20.01 || icon.Data.Bounds.Bottom > 20.01) throw new Exception(name);
					panel.Children.Clear();
					icon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black);
					panel.Children.Add(icon);
					panel.UpdateLayout();

					await Task.Delay(30);

					RenderTargetBitmap bitmap = new();
					await bitmap.RenderAsync(icon);

					byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
					if (!pixels.Where((value, index) => index % 4 == 3).Any(value => value > 0)) throw new Exception("Blank rendered icon: " + name);
				}

				File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "result.txt"), $"PASS: All {names.Length} native icons fit their viewports and render visible pixels.");
			}
			catch (Exception error)
			{
				File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "result.txt"), error.ToString());
			}
			this.Exit();
		});
	}
}
// DispatcherQueueSynchronizationContext does not install itself in posted callbacks.
// Keep nested awaits on the UI thread even after XAML Loaded and WinRT callbacks.
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
