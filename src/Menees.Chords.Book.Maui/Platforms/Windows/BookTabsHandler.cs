using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;

namespace Menees.Chords.Book.Maui.Platforms.Windows;

public sealed class BookTabsHandler : ViewHandler<BookTabs, TabView>
{
	private static readonly IPropertyMapper<BookTabs, BookTabsHandler> TabsMapper = new PropertyMapper<BookTabs, BookTabsHandler>(ViewMapper)
	{
		[nameof(BookTabs.SelectedIndex)] = (handler, view) => handler.PlatformView.SelectedIndex = view.SelectedIndex,
		[nameof(BookTabs.Titles)] = (handler, view) => handler.UpdateTitles(view),
	};

	private bool updatingTitles;

	public BookTabsHandler()
		: base(TabsMapper)
	{
	}

	protected override TabView CreatePlatformView()
	{
		return new() { IsAddTabButtonVisible = false, CanReorderTabs = false, CanDragTabs = false, TabWidthMode = TabViewWidthMode.SizeToContent };
	}

	protected override void ConnectHandler(TabView platformView)
	{
		base.ConnectHandler(platformView);
		platformView.SelectionChanged += this.HandleSelectionChanged;
	}

	protected override void DisconnectHandler(TabView platformView)
	{
		platformView.SelectionChanged -= this.HandleSelectionChanged;
		base.DisconnectHandler(platformView);
	}

	private void HandleSelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
	{
		if (!this.updatingTitles && this.PlatformView.SelectedIndex >= 0)
		{
			this.VirtualView.SelectedIndex = this.PlatformView.SelectedIndex;
		}
	}

	private void UpdateTitles(BookTabs view)
	{
		this.updatingTitles = true;
		try
		{
			this.PlatformView.TabItems.Clear();
			foreach (string title in view.Titles)
			{
				TabViewItem item = new() { Header = title, IsClosable = false };
				ToolTipService.SetToolTip(
					item, new Microsoft.UI.Xaml.Controls.ToolTip { IsEnabled = false, Visibility = Microsoft.UI.Xaml.Visibility.Collapsed });
				this.PlatformView.TabItems.Add(item);
			}

			this.PlatformView.SelectedIndex = Math.Clamp(view.SelectedIndex, 0, Math.Max(0, view.Titles.Count - 1));
		}
		finally
		{
			this.updatingTitles = false;
		}
	}
}
