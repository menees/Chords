#region Using Directives

using Menees.Chords.Book.Application;
using Menees.Chords.Db;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AutomationProperties = Microsoft.UI.Xaml.Automation.AutomationProperties;
using WinTextBox = Microsoft.UI.Xaml.Controls.TextBox;

#endregion

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>One native saved-tab dialog that retains its fields when validation or persistence fails.</summary>
internal static class WindowsSavedTabDialog
{
	#region Public Methods

	public static async Task<Guid?> ShowAsync(
		Microsoft.Maui.Controls.Page owner,
		CustomTabEdit original,
		Func<CustomTabEdit, CancellationToken, Task<Guid>> save,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (owner.Handler?.PlatformView is not FrameworkElement { XamlRoot: not null } root)
		{
			throw new InvalidOperationException("The saved-tab editor is not available yet.");
		}

		const int Gap = 8;
		WinTextBox name = new() { Header = "Tab name", Text = original.Name, PlaceholderText = "E.g., Eagles" };
		WinTextBox search = new()
		{
			Header = "Search filter", Text = original.Search, PlaceholderText = "E.g., artist:Eagles",
			Description = "Leave empty to include all songs.", TextWrapping = TextWrapping.Wrap,
		};
		ComboBox grouping = new() { Header = "Group by (optional)", HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch };
		grouping.Items.Add(new ComboBoxItem { Content = "None" });
		foreach ((string field, string caption) in BookGroupingIndex.Fields)
		{
			grouping.Items.Add(new ComboBoxItem { Content = caption, Tag = field });
		}

		grouping.SelectedIndex = original.GroupBy is null ? 0 : grouping.Items.OfType<ComboBoxItem>().ToList()
			.FindIndex(item => item.Tag as string == original.GroupBy);
		TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
		StackPanel fields = new() { Spacing = Gap };
		fields.Children.Add(name);
		fields.Children.Add(search);
		fields.Children.Add(grouping);
		fields.Children.Add(status);
		ScrollViewer content = new() { Content = fields, VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto };
		ContentDialog dialog = new()
		{
			XamlRoot = root.XamlRoot, RequestedTheme = root.ActualTheme, Title = original.Id is null ? "New Tab" : "Edit Tab",
			Content = content, PrimaryButtonText = "Save", SecondaryButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary,
		};
		dialog.Resources["ContentDialogButtonSpacing"] = new Microsoft.UI.Xaml.GridLength(Gap);
		AutomationProperties.SetName(name, "Tab name");
		AutomationProperties.SetName(search, "Search filter");
		AutomationProperties.SetName(grouping, "Group by, optional");
		dialog.Opened += (_, _) =>
		{
			name.Focus(FocusState.Programmatic);
			name.SelectAll();
		};
		bool saving = false;
		Guid? savedId = null;
		CustomTabEdit ReadDraft() => new(original.Id, name.Text, search.Text, (grouping.SelectedItem as ComboBoxItem)?.Tag as string);
		void UpdateControls()
		{
			content.IsEnabled = !saving;
			dialog.IsPrimaryButtonEnabled = !saving && ReadDraft().IsValid;
			dialog.IsSecondaryButtonEnabled = !saving;
		}

		name.TextChanged += (_, _) => UpdateControls();
		grouping.SelectionChanged += (_, _) => UpdateControls();
		dialog.Closing += (_, args) => args.Cancel = saving;
		dialog.PrimaryButtonClick += async (_, args) =>
		{
			ContentDialogButtonClickDeferral deferral = args.GetDeferral();
			args.Cancel = true;
			try
			{
				CustomTabEdit draft = ReadDraft();
				if (draft.IsValid)
				{
					saving = true;
					UpdateControls();
					status.Text = "Saving…";
					savedId = await save(draft, cancellationToken).ConfigureAwait(true);
					args.Cancel = false;
				}
			}
#pragma warning disable CA1031 // Keep save failures visible and preserve every entered field in the open dialog.
			catch (Exception exception)
			{
				status.Text = exception is BookStoreConcurrencyException
					? "This book changed while the dialog was open. Cancel, reopen the book, then try again."
					: exception.Message;
			}
#pragma warning restore CA1031
			finally
			{
				saving = false;
				UpdateControls();
				deferral.Complete();
			}
		};
		UpdateControls();
		await dialog.ShowAsync().AsTask(cancellationToken).ConfigureAwait(true);
		return savedId;
	}

	#endregion
}