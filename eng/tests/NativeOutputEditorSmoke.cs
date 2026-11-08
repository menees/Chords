using System.Reflection;
using Menees.Chords.Book.Maui;
using Menees.Chords.Sync;
using static SetlistSmoke.SmokeMauiApp;
using MauiProgressBar = Microsoft.Maui.Controls.ProgressBar;
using NativeProgressBar = Microsoft.UI.Xaml.Controls.ProgressBar;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SetlistSmoke;

internal static class NativeOutputEditorSmoke
{
	public static async Task RunAsync(Microsoft.Maui.Controls.Window window, BookSession session)
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
		await VerifySharedSyncPageAsync(window, session);
	}

	private static async Task VerifySharedSyncPageAsync(Microsoft.Maui.Controls.Window window, BookSession session)
	{
		using TestCloudConnection connection = new();
		CloudSyncPage page = new(session, connection);
		window.Page = page;
		const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
		object? Field(string name) => typeof(CloudSyncPage).GetField(name, PrivateInstance)!.GetValue(page);
		object? Call(string name, params object?[] args) => typeof(CloudSyncPage).GetMethod(name, PrivateInstance)!.Invoke(page, args);
		await WaitAsync(() => Field("loaded") is true && Field("operation") is null);
		MauiProgressBar bar = (MauiProgressBar)Field("transferBar")!;
		OutputEditor report = (OutputEditor)Field("report")!;
		Label status = (Label)Field("status")!;
		Check(page.Title == "Test Cloud" && ((FluentIconButton)Field("connect")!).Description == "Reconnect Test Cloud",
			"Shared sync page did not use provider labels");

		report.Text = Lines(20);
		report.Focus();
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Task running = (Task)Call("RunAsync", (Func<CancellationToken, Task>)(_ =>
		{
			var progress = (IProgress<BookSyncProgress>)Call("CreateTransferProgress")!;
			progress.Report(new("Removing obsolete cloud sheets: 3 of 6 files removed.", 3, 6));
			return release.Task;
		}))!;
		try
		{
			await WaitAsync(() => bar.IsVisible && bar.Progress == 0.5 && bar.Handler?.PlatformView is NativeProgressBar { IsLoaded: true });
			NativeProgressBar native = (NativeProgressBar)bar.Handler!.PlatformView!;
			await WaitAsync(() => FindElement(native, "DeterminateProgressBarIndicator") is { ActualHeight: >= 9 });
			Check(FindElement(native, "ProgressBarTrack") is { ActualHeight: >= 9 }, "Native progress track is too thin");
			Check(((Microsoft.Maui.Controls.Grid)page.Content).Children.Cast<Microsoft.Maui.Controls.View>()
				.Single(child => child is VerticalStackLayout stack && stack.Children.Contains(bar)) is View panel
				&& Microsoft.Maui.Controls.Grid.GetRow(panel) < Microsoft.Maui.Controls.Grid.GetRow(report), "Progress must be above the report");
			Check(status.Text.Contains("3 of 6"), "Progress count was not shown beside the bar");
			await CaptureAsync(page, "cloud-cleanup-progress.png");
		}
		finally
		{
			release.TrySetResult();
			await running;
		}

		Check(!bar.IsVisible, "A completed operation left the bar visible");
		var apply = (FluentIconButton)Field("apply")!;
		((IButtonController)apply).SendClicked();
		await WaitAsync(() => Field("operation") is null);
		Check(report.Text?.Contains("Completed") == true && report.Text.Contains("from Test Cloud") && !bar.IsVisible,
			"Shared cloud sync report or completed UI is incorrect: " + status.Text);
		((IButtonController)apply).SendClicked();
		await WaitAsync(() => Field("operation") is null);
		Check(status.Text.StartsWith("Already up to date.", StringComparison.Ordinal) && !bar.IsVisible, "No-op sync UI: " + status.Text);
		await CaptureAsync(page, "cloud-sync-idle.png");
		Program.Trace("Shared sync page used the fake provider; native track and indicator are thick and the bar hides at completion.");
	}

	private static FrameworkElement? FindElement(DependencyObject root, string name)
	{
		if (root is FrameworkElement element && element.Name == name) return element;
		for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
			if (FindElement(VisualTreeHelper.GetChild(root, index), name) is { } child) return child;
		return null;
	}

	private sealed class TestCloudConnection : ICloudSyncConnection
	{
		private readonly InMemoryCloudReplica replica = new(new("test", "disposable-account", "disposable-folder"));
		public string ProviderName => "Test Cloud";
		public string AccountLabel => "Test account";
		public string AccountName => "disposable-account";
		public bool IsConnected => this.replica.IsAuthenticated;
		public string StateDirectory => Path.Combine(AppContext.BaseDirectory, "disposable-cloud-state");
		public int MaximumConcurrentTransfers => 1;
		public Task LoadAsync(CancellationToken token) => this.replica.AuthenticateAsync(token);
		public Task EnsureConnectedAsync(CancellationToken token) => this.replica.AuthenticateAsync(token);
		public Task ConnectAsync(CancellationToken token) => this.replica.AuthenticateAsync(token);
		public Task DisconnectAsync(CancellationToken token) => this.replica.DisconnectAsync(token);
		public Task<IReadOnlyList<CloudBookChoice>> ListBooksAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<CloudBookChoice>>([]);
		public Task<ICloudReplica> OpenBookAsync(Guid bookId, CancellationToken token) => Task.FromResult<ICloudReplica>(this.replica);
		public ICloudReplica OpenFolder(string folderId) => this.replica;
		public void Dispose() { }
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
