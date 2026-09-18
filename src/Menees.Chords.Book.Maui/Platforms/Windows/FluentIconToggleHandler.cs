using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Menees.Chords.Book.Maui.Platforms.Windows;

public sealed class FluentIconToggleHandler : ViewHandler<FluentIconToggle, ToggleButton>
{
	private static readonly IPropertyMapper<FluentIconToggle, FluentIconToggleHandler> ToggleMapper =
		new PropertyMapper<FluentIconToggle, FluentIconToggleHandler>(ViewMapper)
		{
			[nameof(FluentIconToggle.Icon)] = (handler, view) => handler.PlatformView.Content = FluentIconSource.Create(view.Icon),
			[nameof(FluentIconToggle.Description)] = (handler, view) => ToolTipService.SetToolTip(handler.PlatformView, view.Description),
			[nameof(FluentIconToggle.IsChecked)] = (handler, view) => handler.PlatformView.IsChecked = view.IsChecked,
		};

	public FluentIconToggleHandler()
		: base(ToggleMapper)
	{
	}

	protected override ToggleButton CreatePlatformView()
	{
		const int IconPadding = 4;
		ToggleButton button = new()
		{
			MinWidth = 0, MinHeight = 0, Padding = new Microsoft.UI.Xaml.Thickness(IconPadding),
		};
		ToolTipService.SetToolTip(button, this.VirtualView.Description);
		return button;
	}

	protected override void ConnectHandler(ToggleButton platformView)
	{
		base.ConnectHandler(platformView);
		platformView.Checked += this.HandleChecked;
		platformView.Unchecked += this.HandleChecked;
	}

	protected override void DisconnectHandler(ToggleButton platformView)
	{
		platformView.Checked -= this.HandleChecked;
		platformView.Unchecked -= this.HandleChecked;
		base.DisconnectHandler(platformView);
	}

	private void HandleChecked(object sender, RoutedEventArgs e) => this.VirtualView.IsChecked = this.PlatformView.IsChecked == true;
}
