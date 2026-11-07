using Menees.Chords.Db;
using Microsoft.UI.Xaml.Controls;

namespace Menees.Chords.Book.Maui.Platforms.Windows;

internal static class WindowsCatalogFieldPicker
{
	public static ComboBox Create(string header, string? selected, bool optional)
	{
		ComboBox result = new() { Header = header, HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch };
		if (optional)
		{
			result.Items.Add(new ComboBoxItem { Content = "None" });
		}

		foreach ((string field, string caption) in optional ? BookGroupingIndex.Fields : BookGroupingIndex.SortFields)
		{
			result.Items.Add(new ComboBoxItem { Content = caption, Tag = field });
		}

		result.SelectedIndex = result.Items.OfType<ComboBoxItem>().ToList().FindIndex(item => item.Tag as string == selected);
		return result;
	}
}
