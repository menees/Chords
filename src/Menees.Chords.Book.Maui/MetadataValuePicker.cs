using Menees.Chords.Book.Application;
using MauiEntry = Microsoft.Maui.Controls.Entry;

namespace Menees.Chords.Book.Maui;

/// <summary>Offers existing catalog values while leaving deliberate new-value entry available.</summary>
public sealed partial class MetadataValuePicker : Picker
{
	public MetadataValuePicker()
	{
		this.IsEnabled = false;
		this.SelectedIndexChanged += (_, _) =>
		{
			if (this.SelectedIndex >= 0 && this.SelectedItem is string selected && this.TargetEntry is MauiEntry entry)
			{
				try
				{
					entry.Text = this.AppendValue ? CatalogValueList.Append(entry.Text, selected) : CatalogValueList.Format([selected]);
				}
				catch (ArgumentException exception)
				{
					this.SelectionRejected?.Invoke(exception.Message);
				}

				int selectedIndex = this.SelectedIndex;
				this.Dispatcher.Dispatch(() =>
				{
					if (this.SelectedIndex == selectedIndex)
					{
						this.SelectedIndex = -1;
					}
				});
			}
		};
	}

	public event Action<string>? SelectionRejected;

	public MauiEntry? TargetEntry { get; set; }

	public bool AppendValue { get; set; }

	public void LoadValues(IReadOnlyList<string> values)
	{
		this.ItemsSource = values as System.Collections.IList ?? values.ToArray();
		this.IsEnabled = values.Count > 0;
	}
}
