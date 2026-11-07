#region Using Directives

using Menees.Chords.Book.Application;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AutomationProperties = Microsoft.UI.Xaml.Automation.AutomationProperties;

#endregion

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Edits a detached local view preference without changing a saved tab or song catalog.</summary>
internal static class WindowsSongViewSortDialog
{
	#region Public Methods

	public static async Task<SongViewSort?> ShowAsync(
		Microsoft.Maui.Controls.Page owner, SongViewSort original, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (owner.Handler?.PlatformView is not FrameworkElement { XamlRoot: not null } root)
		{
			throw new InvalidOperationException("View sorting is not available yet.");
		}

		const int Gap = 8;
		ComboBox field = WindowsCatalogFieldPicker.Create("Sort songs by", original.Field, optional: false);
		field.Items.Insert(0, new ComboBoxItem { Content = "Default for this view" });
		field.SelectedItem = field.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == original.Field);
		Microsoft.UI.Xaml.Controls.CheckBox descending = new() { Content = "Descending order", IsChecked = original.Descending };
		void UpdateDescending() => descending.IsEnabled = (field.SelectedItem as ComboBoxItem)?.Tag is string;
		field.SelectionChanged += (_, _) => UpdateDescending();
		UpdateDescending();
		StackPanel fields = new() { Spacing = Gap, Children = { field, descending } };
		ContentDialog dialog = new()
		{
			Title = "Sort View", XamlRoot = root.XamlRoot, RequestedTheme = root.ActualTheme, Content = fields,
			PrimaryButtonText = "Apply", SecondaryButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary,
		};
		dialog.Resources["ContentDialogButtonSpacing"] = new Microsoft.UI.Xaml.GridLength(Gap);
		AutomationProperties.SetName(field, "Sort songs by");
		AutomationProperties.SetName(descending, "Descending order");
		ContentDialogResult choice = await dialog.ShowAsync().AsTask(cancellationToken).ConfigureAwait(true);
		return choice == ContentDialogResult.Primary ? new SongViewSort((field.SelectedItem as ComboBoxItem)?.Tag as string, descending.IsChecked == true)
			: null;
	}
	#endregion
}
