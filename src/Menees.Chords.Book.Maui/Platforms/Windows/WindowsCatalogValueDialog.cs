#region Using Directives

using Menees.Chords.Book.Application;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AutomationProperties = Microsoft.UI.Xaml.Automation.AutomationProperties;
using Button = Microsoft.UI.Xaml.Controls.Button;
using GridLength = Microsoft.UI.Xaml.GridLength;
using ListView = Microsoft.UI.Xaml.Controls.ListView;
using ListViewSelectionMode = Microsoft.UI.Xaml.Controls.ListViewSelectionMode;
using NativeGrid = Microsoft.UI.Xaml.Controls.Grid;

#endregion

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Virtualized checked-list editing for catalog values; only Apply returns a draft.</summary>
internal static class WindowsCatalogValueDialog
{
	#region Public Methods

	public static async Task<IReadOnlyList<string>?> ShowAsync(
		VisualElement owner, string title, CatalogValueSelection draft, string? description = null, CancellationToken cancellationToken = default)
	{
		if (owner.Handler?.PlatformView is not FrameworkElement { XamlRoot: not null } root)
		{
			throw new InvalidOperationException("Metadata selection is not available yet.");
		}

		const int Gap = 8;
		const int SummaryRow = 3;
		const int PreferredWidth = 480;
		const int MinimumHeight = 160;
		const int PreferredHeight = 400;
		const int HorizontalChrome = 96;
		const int VerticalChrome = 240;
		const int ChoiceRowHeight = 44;
		const int SearchAndSummaryHeight = 80;
		const int DescriptionHeight = 60;
		double listHeight = ((double)draft.Filter(null).Count * ChoiceRowHeight) + SearchAndSummaryHeight;
		double desiredHeight = Math.Clamp(listHeight + (description is null ? 0 : DescriptionHeight), MinimumHeight, PreferredHeight);
		TextBox search = new() { PlaceholderText = "Search or enter a new value" };
		Button add = new() { Content = FluentIconSource.Create("Add"), IsEnabled = false };
		ToolTipService.SetToolTip(add, "Add and select the entered value");
		AutomationProperties.SetAutomationId(search, "CatalogValueSearch");
		AutomationProperties.SetAutomationId(add, "CatalogValueAdd");
		AutomationProperties.SetName(add, "Add and select value");
		AutomationProperties.SetName(search, "Search or enter a new value");
		ListView choices = new() { SelectionMode = ListViewSelectionMode.Multiple };
		AutomationProperties.SetName(choices, title);
		AutomationProperties.SetAutomationId(choices, "CatalogValueChoices");
		TextBlock count = new() { Margin = new(Gap, 0, 0, 0) };
		NativeGrid searchRow = new() { ColumnSpacing = Gap, ColumnDefinitions = { new(), new() { Width = GridLength.Auto } } };
		searchRow.Children.Add(search);
		NativeGrid.SetColumn(add, 1);
		searchRow.Children.Add(add);
		NativeGrid content = new()
		{
			Width = Math.Max(0, Math.Min(PreferredWidth, root.XamlRoot.Size.Width - HorizontalChrome)),
			Height = Math.Min(desiredHeight, Math.Clamp(root.XamlRoot.Size.Height - VerticalChrome, MinimumHeight, PreferredHeight)),
			RowSpacing = Gap, Margin = new(0, Gap, 0, 0),
			RowDefinitions = { new() { Height = GridLength.Auto }, new() { Height = GridLength.Auto }, new(), new() { Height = GridLength.Auto } },
		};
		content.Children.Add(new TextBlock
		{
			Text = description ?? string.Empty, TextWrapping = TextWrapping.Wrap,
			Visibility = description is null ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible,
		});
		NativeGrid.SetRow(searchRow, 1);
		content.Children.Add(searchRow);
		NativeGrid.SetRow(choices, 2);
		content.Children.Add(choices);
		NativeGrid.SetRow(count, SummaryRow);
		content.Children.Add(count);
		ContentDialog dialog = new()
		{
			Title = title, XamlRoot = root.XamlRoot, RequestedTheme = root.ActualTheme, Content = content,
			PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary,
		};
		dialog.Resources["ContentDialogButtonSpacing"] = new GridLength(Gap);
		bool updating = false;
		void UpdateSelection()
		{
			updating = true;
			choices.SelectedItems.Clear();
			foreach (string value in choices.Items.OfType<string>().Where(draft.IsSelected))
			{
				choices.SelectedItems.Add(value);
			}

			updating = false;
			count.Text = $"{draft.Values.Count} selected";
		}

		void Filter()
		{
			updating = true;
			choices.ItemsSource = draft.Filter(search.Text);
			UpdateSelection();
			add.IsEnabled = !string.IsNullOrWhiteSpace(search.Text);
		}

		choices.SelectionChanged += (_, args) =>
		{
			if (!updating)
			{
				foreach (string value in args.RemovedItems.OfType<string>())
				{
					draft.SetSelected(value, false);
				}

				foreach (string value in args.AddedItems.OfType<string>())
				{
					draft.SetSelected(value, true);
				}

				UpdateSelection();
			}
		};
		search.TextChanged += (_, _) => Filter();
		add.Click += (_, _) =>
		{
			string added = draft.Add(search.Text);
			search.Text = string.Empty;
			Filter();
			choices.ScrollIntoView(added);
		};
		Filter();
		dialog.Opened += (_, _) =>
		{
			if (draft.Values.Count > 0)
			{
				choices.ScrollIntoView(draft.Values[0]);
			}

			search.Focus(FocusState.Programmatic);
		};
		ContentDialogResult choice = await dialog.ShowAsync().AsTask(cancellationToken).ConfigureAwait(true);
		return choice == ContentDialogResult.Primary ? draft.Values.ToArray() : null;
	}
	#endregion

}
