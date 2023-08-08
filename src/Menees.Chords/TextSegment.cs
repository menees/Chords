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
		if (this is not WhiteSpaceSegment)
		{
			Conditions.RequireNonWhiteSpace(text);
		}

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
	/// Creates a segment for the specified <paramref name="text"/>.
	/// </summary>
	/// <param name="text">The text of the segment.</param>
	/// <returns>A new <see cref="WhiteSpaceSegment"/> if <paramref name="text"/> is all whitespace.
	/// Otherwise a new <see cref="TextSegment"/>.</returns>
	public static TextSegment Create(string text)
	{
		Conditions.RequireNonEmpty(text);
		TextSegment result = string.IsNullOrWhiteSpace(text) ? new WhiteSpaceSegment(text) : new TextSegment(text);
		return result;
	}

	/// <summary>
	/// Returns the segment <see cref="Text"/>.
	/// </summary>
	public override string ToString() => this.Text;

	#endregion
}
