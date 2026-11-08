namespace Menees.Chords.Book.Maui;

/// <summary>A catalog cell that takes no space when its field or value is absent.</summary>
public sealed partial class CatalogCell : Label
{
	internal const string DefaultFontFamily = "Segoe UI";
	internal const int DefaultFontSize = 14;
	internal const int BylineFontSize = 12;
	internal const int HorizontalPadding = 4;
	private bool isByline;
	private bool showColumn = true;

	public CatalogCell()
	{
		this.FontFamily = DefaultFontFamily;
		this.FontSize = DefaultFontSize;
		this.Padding = new(HorizontalPadding, 0);
		this.LineBreakMode = LineBreakMode.WordWrap;
		this.SetDynamicResource(TextColorProperty, "AppText");
	}

	internal bool IsByline
	{
		set
		{
			if (this.isByline != value)
			{
				this.isByline = value;
				this.FontSize = value ? BylineFontSize : DefaultFontSize;
				this.SetDynamicResource(TextColorProperty, value ? "AppMuted" : "AppText");
			}
		}
	}

	internal bool ShowColumn
	{
		set
		{
			this.showColumn = value;
			this.UpdateVisibility();
		}
	}

	protected override void OnPropertyChanged(string? propertyName = null)
	{
		base.OnPropertyChanged(propertyName);
		if (propertyName == nameof(this.Text))
		{
			this.UpdateVisibility();
		}
	}

	private void UpdateVisibility() => this.IsVisible = this.showColumn && !string.IsNullOrWhiteSpace(this.Text);
}
