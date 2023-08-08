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
	public WhiteSpaceSegment(string whiteSpace)
		: base(whiteSpace)
	{
		Conditions.RequireArgument(
			whiteSpace != null && whiteSpace.Length > 0 && string.IsNullOrWhiteSpace(whiteSpace),
			"Text must be non-empty whitespace.");
	}

	#endregion
}
