namespace Menees.Chords;

/// <summary>
/// A portion of a text line that's all whitespace per <see cref="char.IsWhiteSpace(char)"/>.
/// </summary>
public sealed class WhiteSpaceSegment : TextSegment
{
	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="whiteSpace">The segment's whitespace</param>
	/// <param name="index">The segment's start index in the line it was extracted from.</param>
	public WhiteSpaceSegment(string whiteSpace, int index = 0)
		: base(whiteSpace, index)
	{
	}

	#endregion
}
