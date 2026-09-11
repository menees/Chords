using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;

namespace Menees.Chords.Book.Maui.Platforms.Windows;

public sealed class BookTabsHandler : ViewHandler<BookTabs, TabView>
{
	private static readonly IPropertyMapper<BookTabs, BookTabsHandler> TabsMapper = new PropertyMapper<BookTabs, BookTabsHandler>(ViewMapper)
	{
		[nameof(BookTabs.SelectedIndex)] = (handler, view) => handler.PlatformView.SelectedIndex = view.SelectedIndex,
	};

	public BookTabsHandler()
		: base(TabsMapper)
	{
	}

	protected override TabView CreatePlatformView()
	{
		TabView tabs = new() { IsAddTabButtonVisible = false, CanReorderTabs = false, CanDragTabs = false };
		tabs.TabItems.Add(new TabViewItem { Header = "Songs", IsClosable = false });
		tabs.TabItems.Add(new TabViewItem { Header = "Setlists", IsClosable = false });
		return tabs;
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
		if (this.PlatformView.SelectedIndex >= 0)
		{
			this.VirtualView.SelectedIndex = this.PlatformView.SelectedIndex;
		}
	}
}
