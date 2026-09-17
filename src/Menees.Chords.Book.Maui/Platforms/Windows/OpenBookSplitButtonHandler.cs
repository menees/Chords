using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;

namespace Menees.Chords.Book.Maui.Platforms.Windows;

public sealed class OpenBookSplitButtonHandler : ViewHandler<OpenBookSplitButton, SplitButton>
{
	private static readonly IPropertyMapper<OpenBookSplitButton, OpenBookSplitButtonHandler> SplitMapper
		= new PropertyMapper<OpenBookSplitButton, OpenBookSplitButtonHandler>(ViewMapper)
		{
			[nameof(OpenBookSplitButton.RecentBooks)] = (handler, view) => handler.UpdateMenu(view),
		};

	public OpenBookSplitButtonHandler()
		: base(SplitMapper)
	{
	}

	protected override SplitButton CreatePlatformView()
	{
		SplitButton button = new() { Content = FluentIconSource.Create("Book") };
		ToolTipService.SetToolTip(button, "Open Book");
		Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Open Book");
		return button;
	}

	protected override void ConnectHandler(SplitButton platformView)
	{
		base.ConnectHandler(platformView);
		platformView.Click += this.HandleClick;
	}

	protected override void DisconnectHandler(SplitButton platformView)
	{
		platformView.Click -= this.HandleClick;
		base.DisconnectHandler(platformView);
	}

	private void HandleClick(SplitButton sender, SplitButtonClickEventArgs args) => this.VirtualView.Open();

	private void UpdateMenu(OpenBookSplitButton view)
	{
		Microsoft.UI.Xaml.Controls.MenuFlyout menu = new();
		foreach (RecentBook book in view.RecentBooks)
		{
			Microsoft.UI.Xaml.Controls.MenuFlyoutItem item = new() { Text = book.Name };
			ToolTipService.SetToolTip(item, book.Path);
			item.Click += (_, _) => view.OpenRecent(book);
			menu.Items.Add(item);
		}

		if (menu.Items.Count == 0)
		{
			menu.Items.Add(new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "No recent books", IsEnabled = false });
		}

		this.PlatformView.Flyout = menu;
	}
}
