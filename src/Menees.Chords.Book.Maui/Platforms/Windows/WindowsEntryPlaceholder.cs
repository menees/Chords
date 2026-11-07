#region Using Directives

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MauiEntry = Microsoft.Maui.Controls.Entry;

#endregion

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Styles the Windows placeholder independently of the entry's editable text.</summary>
internal static class WindowsEntryPlaceholder
{
	#region Public Methods

	public static void Configure(MauiEntry entry)
	{
		entry.Loaded += static (sender, _) =>
		{
			if (((MauiEntry)sender!).Handler?.PlatformView is TextBox textBox)
			{
				textBox.ApplyTemplate();
				StylePlaceholder(textBox);
			}
		};
	}

	#endregion

	#region Private Methods

	private static void StylePlaceholder(DependencyObject parent)
	{
		if (parent is Control { Name: "PlaceholderTextContentPresenter" } placeholder)
		{
			placeholder.FontStyle = global::Windows.UI.Text.FontStyle.Italic;
		}
		else if (parent is TextBlock { Name: "PlaceholderTextContentPresenter" } placeholderText)
		{
			placeholderText.FontStyle = global::Windows.UI.Text.FontStyle.Italic;
		}
		else
		{
			for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
			{
				StylePlaceholder(VisualTreeHelper.GetChild(parent, index));
			}
		}
	}

	#endregion
}
