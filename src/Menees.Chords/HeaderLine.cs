namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A "Ultimate Guitar"-style bracketed header (e.g., [Chorus], [Verse]).
/// </summary>
public sealed class HeaderLine : TextEntry
{
	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="text">The header label or section name.</param>
	private HeaderLine(string text)
		: base(text)
	{
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse a header line from the current context.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if <paramref name="context"/>'s line was parsed. Null otherwise.</returns>
	public static HeaderLine? TryParse(LineContext context)
	{
		HeaderLine? result = null;

		Lexer lexer = context.CreateLexer(out IReadOnlyList<Entry> annotations);
		if (lexer.Read(skipLeadingWhiteSpace: true) && lexer.Token.Type == TokenType.Bracketed)
		{
			string headerText = lexer.Token.Text;

			// Since annotations were removed earlier, make sure there's nothing else on the line.
			// And make sure the header text isn't a chord.
			if ((!lexer.Read() || string.IsNullOrEmpty(lexer.ReadToEnd(skipTrailingWhiteSpace: true))) && !Chord.TryParse(headerText, out _))
			{
				// TODO: Handle other special header cases? [Bill, 7/30/2023]
				// Label: Text inside brackets excluding any trailing comment.
				// Comment: Parenthesized text inside brackets. Text after '-' inside brackets. Or text after brackets.
				// Known: Intro, Outro, Verse, Verse #, Chorus, Interlude, Bridge, Pre-Chorus, Solo, Solo #, Break, Post-Chorus, Pre-Verse
				//
				// "Alone With You - Outfield.docx" has some inside the brackets. Also, "Jack and Diane - John Mellencamp.txt" [Bill, 7/21/2023]
				// "Line A Stone - Original.docx" has some outside the brackets. Also, "Authority Song - John Mellencamp.docx"
				//
				// This is used for cases like "[Verse (Softer)]", "[Verse - Softer]" and "[Verse] - Softer" where
				// "Verse" is the header label, and "Softer" is the header comment.
				result = new(headerText);
				result.AddAnnotations(annotations);
			}
		}

		return result;
	}

	#endregion
}
