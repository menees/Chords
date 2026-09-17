namespace Menees.Chords.Book.Maui;

/// <summary>A native icon toggle retaining the standard checked-state API.</summary>
public sealed partial class ArchiveToggle : CheckBox
{
	public ArchiveToggle()
	{
		const int ButtonSize = 32;
		this.WidthRequest = ButtonSize;
		this.HeightRequest = ButtonSize;
		SemanticProperties.SetDescription(this, "Show Archived");
		ToolTipProperties.SetText(this, "Show Archived");
	}
}
