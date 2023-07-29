namespace Menees.Chords;

/// <summary>
/// A portion of a text line.
/// </summary>
public class TextSegment
{
	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="text">The segment's text content.</param>
	/// <param name="index">The segment's start index in the line it was extracted from.</param>
	public TextSegment(string text, int index = 0)
	{
		this.Text = text ?? string.Empty;
		this.Index = index;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the segment's text content.
	/// </summary>
	public string Text { get; }

	/// <summary>
	/// Gets the segment's start index in the line it was extracted from.
	/// </summary>
	public int Index { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Returns the segment <see cref="Text"/>.
	/// </summary>
	public override string ToString() => this.Text;

	#endregion
}
