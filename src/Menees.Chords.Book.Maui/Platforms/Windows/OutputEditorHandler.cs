using Microsoft.Maui.Handlers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TextChangedEventArgs = Microsoft.UI.Xaml.Controls.TextChangedEventArgs;

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Scrolls output after native text layout, coalescing changes into one pending update.</summary>
public sealed class OutputEditorHandler : EditorHandler
{
	private ScrollViewer? scrollViewer;
	private bool pendingScroll;

	protected override void ConnectHandler(TextBox platformView)
	{
		base.ConnectHandler(platformView);
		platformView.TextChanged += this.HandleTextChanged;
		platformView.Loaded += this.HandleLoaded;
	}

	protected override void DisconnectHandler(TextBox platformView)
	{
		platformView.TextChanged -= this.HandleTextChanged;
		platformView.Loaded -= this.HandleLoaded;
		platformView.LayoutUpdated -= this.HandleLayoutUpdated;
		this.pendingScroll = false;
		this.scrollViewer = null;
		base.DisconnectHandler(platformView);
	}

	private static ScrollViewer? FindScrollViewer(DependencyObject parent)
	{
		ScrollViewer? result = parent as ScrollViewer;
		for (int index = 0; result is null && index < VisualTreeHelper.GetChildrenCount(parent); index++)
		{
			result = FindScrollViewer(VisualTreeHelper.GetChild(parent, index));
		}

		return result;
	}

	private void HandleTextChanged(object sender, TextChangedEventArgs e) => this.RequestScroll();

	private void HandleLoaded(object sender, RoutedEventArgs e) => this.RequestScroll();

	private void RequestScroll()
	{
		if (!this.pendingScroll)
		{
			this.pendingScroll = true;
			this.PlatformView.LayoutUpdated += this.HandleLayoutUpdated;
			this.PlatformView.InvalidateMeasure();
		}
	}

	private void HandleLayoutUpdated(object? sender, object e)
	{
		this.PlatformView.LayoutUpdated -= this.HandleLayoutUpdated;
		TextBox platformView = this.PlatformView;
		if (!platformView.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
		{
			if (this.pendingScroll && ReferenceEquals(this.PlatformView, platformView))
			{
				this.pendingScroll = false;
				this.scrollViewer ??= FindScrollViewer(platformView);
				this.scrollViewer?.ChangeView(null, this.scrollViewer.ScrollableHeight, null, disableAnimation: true);
			}
		}))
		{
			this.pendingScroll = false;
		}
	}
}
