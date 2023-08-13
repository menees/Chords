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
/// <para/>
/// ChordPro remarks are essentially comments in the input that won't be visible
/// in the rendered output. If you want to preserve this non-visible aspect, make
/// sure <see cref="ChordProRemarkLine.TryParse"/> has a higher priority than
/// <see cref="Comment.TryParse"/> when creating a <see cref="DocumentParser"/>.
/// Otherwise, <see cref="Comment.TryParse"/> will parse the remark as a visible
/// comment.
/// </remarks>
/// <seealso href="https://www.chordpro.org/chordpro/chordpro-introduction/"/>
public sealed class ChordProRemarkLine : TextEntry
{
	#region Constructors

	internal ChordProRemarkLine(string text)
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
		Conditions.RequireNonNull(context);

		ChordProRemarkLine? result = null;

		Lexer lexer = context.CreateLexer();
		if (lexer.Read(skipLeadingWhiteSpace: true)
			&& lexer.Token.Type == TokenType.Text
			&& lexer.Token.Text.StartsWith('#'))
		{
			result = new(context.LineText);
		}

		return result;
	}

	#endregion
}
