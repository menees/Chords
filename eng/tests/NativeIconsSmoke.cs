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
		Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() =>
		{
			try
			{
				string[] names = ["ArrowLeft", "Previous", "LayerDiagonalAdd", "ArrowRight", "EditLineHorizontal3",
					"SoundWaveCircleSparkle", "LockClosed", "LockOpen", "BookAdd", "Book", "Settings", "ArrowImport",
					"Filter", "MultiselectLtr", "DocumentOnePageAdd", "LayerDiagonal", "ArrowReset", "Archive", "Play", "Stop", "Save", "Dismiss"];
				foreach (string name in names)
				{
					var icon = FluentIconSource.Create(name);
					if (icon.Data is null || icon.Width != 20 || icon.Height != 20) throw new Exception(name);
				}
				File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "result.txt"), "PASS: All 22 embedded Fluent SVG geometries loaded as native WinUI PathIcons.");
			}
			catch (Exception error)
			{
				File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "result.txt"), error.ToString());
			}
			this.Exit();
		});
	}
}