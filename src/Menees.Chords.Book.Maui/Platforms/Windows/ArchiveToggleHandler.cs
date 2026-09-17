using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Menees.Chords.Book.Maui.Platforms.Windows;

public sealed class ArchiveToggleHandler : ViewHandler<ArchiveToggle, ToggleButton>
{
	private static readonly IPropertyMapper<ArchiveToggle, ArchiveToggleHandler> ToggleMapper =
		new PropertyMapper<ArchiveToggle, ArchiveToggleHandler>(ViewMapper)
		{
			[nameof(ArchiveToggle.IsChecked)] = (handler, view) => handler.PlatformView.IsChecked = view.IsChecked,
		};

	public ArchiveToggleHandler()
		: base(ToggleMapper)
	{
	}

	protected override ToggleButton CreatePlatformView()
	{
		const int IconPadding = 4;
		ToggleButton button = new()
		{
			Content = FluentIconSource.Create("Archive"), MinWidth = 0, MinHeight = 0, Padding = new Microsoft.UI.Xaml.Thickness(IconPadding),
		};
		ToolTipService.SetToolTip(button, "Show Archived");
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
