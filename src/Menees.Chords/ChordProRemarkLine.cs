namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A #-prefixed line that ChordPro treats as a remark for maintainers.
/// </summary>
/// <remarks>
/// ChordPro's introduction says, "Finally, all lines that start with a # are ignored.
/// These can be used to insert remarks into the ChordPro file that are only relevant
/// for maintainers."
/// </remarks>
/// <seealso href="https://www.chordpro.org/chordpro/chordpro-introduction/"/>
public sealed class ChordProRemarkLine : TextEntry
{
	#region Constructors

	private ChordProRemarkLine(string text)
		: base(text)
	{
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse the current line as a ChordPro remark line.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if the current trimmed line starts with '#'.</returns>
	public static ChordProRemarkLine? TryParse(LineContext context)
	{
		ChordProRemarkLine? result = null;

		if (context.LineText.TrimStart().StartsWith("#"))
		{
			result = new(context.LineText);
		}

		return result;
	}

	#endregion
}
