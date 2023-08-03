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
	public TextSegment(string text)
	{
		this.Text = text ?? string.Empty;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the segment's text content.
	/// </summary>
	public string Text { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Returns the segment <see cref="Text"/>.
	/// </summary>
	public override string ToString() => this.Text;

	#endregion
}
