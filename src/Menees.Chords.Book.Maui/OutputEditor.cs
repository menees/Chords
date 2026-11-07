namespace Menees.Chords.Book.Maui;

/// <summary>A selectable, read-only output pane that follows new output.</summary>
public sealed partial class OutputEditor : Editor
{
	public OutputEditor()
	{
		this.IsReadOnly = true;
		this.AutoSize = EditorAutoSizeOption.Disabled;
	}
}
