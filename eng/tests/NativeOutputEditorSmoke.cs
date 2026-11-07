using Menees.Chords.Book.Maui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SetlistSmoke;

internal static class NativeOutputEditorSmoke
{
	public static async Task RunAsync(Microsoft.Maui.Controls.Window window)
	{
		Program.Trace("Verify native output follows text changes after layout");
		OutputEditor editor = new() { Text = Lines(150) };
		ContentPage page = new() { Content = new Microsoft.Maui.Controls.Grid { Children = { editor } } };
		window.Page = page;
		await WaitAsync(() => editor.Handler?.PlatformView is TextBox { IsLoaded: true });
		TextBox text = (TextBox)editor.Handler!.PlatformView!;
		ScrollViewer? scroll = null;
		await WaitAsync(() => (scroll = FindScrollViewer(text)) is { ScrollableHeight: > 0 });
		await WaitAsync(() => AtBottom(scroll!));
		if (!text.IsReadOnly) throw new Exception("Sync output must remain read-only.");

		scroll!.ChangeView(null, 0, null, disableAnimation: true);
		await WaitAsync(() => scroll.VerticalOffset == 0);
		await Task.Delay(150);
		if (scroll.VerticalOffset != 0) throw new Exception("Idle output prevents reading older lines.");
		editor.Text += "\nAppended line";
		await WaitAsync(() => text.Text.EndsWith("Appended line") && AtBottom(scroll));

		scroll.ChangeView(null, 0, null, disableAnimation: true);
		await WaitAsync(() => scroll.VerticalOffset == 0);
		editor.Text = Lines(400) + "\nCompleted synthetic sync.";
		await WaitAsync(() => text.Text.EndsWith("Completed synthetic sync.") && AtBottom(scroll));
		double beforeBurst = scroll.ScrollableHeight;
		for (int index = 0; index < 50; index++) editor.Text += "\nBurst " + index;
		await WaitAsync(() => text.Text.EndsWith("Burst 49") && scroll.ScrollableHeight > beforeBurst && AtBottom(scroll));
		editor.Text = string.Empty;
		await WaitAsync(() => text.Text.Length == 0 && scroll.ScrollableHeight == 0 && scroll.VerticalOffset == 0);
		editor.Text = "Short report\nCompleted.";
		await WaitAsync(() => text.Text.EndsWith("Completed.") && scroll.VerticalOffset == 0);
		editor.Text = Lines(200);
		await WaitAsync(() => scroll.ScrollableHeight > 0 && AtBottom(scroll));
		Program.Trace($"Output scrolling passed: offset {scroll.VerticalOffset}; bottom {scroll.ScrollableHeight}.");
	}

	private static string Lines(int count) => string.Join("\n", Enumerable.Range(1, count).Select(index => $"Synthetic sync output line {index}"));
	private static bool AtBottom(ScrollViewer scroll) => Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1;
	private static ScrollViewer? FindScrollViewer(DependencyObject element)
	{
		if (element is ScrollViewer viewer) return viewer;
		for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
			if (FindScrollViewer(VisualTreeHelper.GetChild(element, index)) is ScrollViewer child) return child;
		return null;
	}
	private static async Task WaitAsync(Func<bool> ready)
	{
		for (int index = 0; index < 250; index++) { if (ready()) return; await Task.Delay(20); }
		throw new Exception("Native output did not reach expected scroll position. " + File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "trace.txt")));
	}
}
