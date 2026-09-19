using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;

namespace Menees.Chords.Book.Maui.Platforms.Windows;

public sealed class FluentIconSplitButtonHandler : ViewHandler<FluentIconSplitButton, SplitButton>
{
	private static readonly IPropertyMapper<FluentIconSplitButton, FluentIconSplitButtonHandler> SplitMapper
		= new PropertyMapper<FluentIconSplitButton, FluentIconSplitButtonHandler>(ViewMapper)
		{
			[nameof(FluentIconSplitButton.Icon)] = (handler, view) => handler.PlatformView.Content = FluentIconSource.Create(view.Icon),
			[nameof(FluentIconSplitButton.Description)] = (handler, view) =>
			{
				ToolTipService.SetToolTip(handler.PlatformView, view.Description);
				Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(handler.PlatformView, view.Description);
			},
			[nameof(FluentIconSplitButton.MenuItems)] = (handler, view) => handler.UpdateMenu(view),
		};

	public FluentIconSplitButtonHandler()
		: base(SplitMapper)
	{
	}

	protected override SplitButton CreatePlatformView()
	{
		const int ButtonHeight = 32;
		const int HorizontalPadding = 6;
		return new() { MinHeight = ButtonHeight, Padding = new Microsoft.UI.Xaml.Thickness(HorizontalPadding, 0, HorizontalPadding, 0) };
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

	private void HandleClick(SplitButton sender, SplitButtonClickEventArgs args) => this.VirtualView.Invoke();

	private void UpdateMenu(FluentIconSplitButton view)
	{
		Microsoft.UI.Xaml.Controls.MenuFlyout menu = new();
		foreach (FluentMenuItem action in view.MenuItems)
		{
			Microsoft.UI.Xaml.Controls.MenuFlyoutItem item = new() { Text = action.Text, IsEnabled = action.IsEnabled };
			ToolTipService.SetToolTip(item, action.Description ?? action.Text);
			item.Click += (_, _) => action.Invoke();
			menu.Items.Add(item);
		}

		this.PlatformView.Flyout = menu;
	}
}
