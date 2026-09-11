using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Menees.Chords.Book.Maui.Platforms.Windows;

public sealed class PositionEntryHandler : EntryHandler
{
	protected override void ConnectHandler(TextBox platformView)
	{
		base.ConnectHandler(platformView);
		platformView.PreviewKeyDown += this.HandlePreviewKeyDown;
	}

	protected override void DisconnectHandler(TextBox platformView)
	{
		platformView.PreviewKeyDown -= this.HandlePreviewKeyDown;
		base.DisconnectHandler(platformView);
	}

	private void HandlePreviewKeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (e.Key == VirtualKey.Enter && this.VirtualView is PositionEntry entry)
		{
			e.Handled = true;
			entry.Text = this.PlatformView.Text;
			entry.SubmitPosition();
		}
	}
}
