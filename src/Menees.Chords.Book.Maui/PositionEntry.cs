namespace Menees.Chords.Book.Maui;

/// <summary>An entry whose platform handler commits Enter before the enclosing list processes the key.</summary>
public sealed partial class PositionEntry : Microsoft.Maui.Controls.Entry
{
	public event EventHandler? PositionSubmitted;

	public void SubmitPosition() => this.PositionSubmitted?.Invoke(this, EventArgs.Empty);
}
