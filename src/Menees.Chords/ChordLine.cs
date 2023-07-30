namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A line of chord names and optional annotations (e.g., N.C.).
/// </summary>
public sealed class ChordLine : Entry
{
	#region Private Data Members

	// https://www.ultimate-guitar.com/contribution/help/rubric#iii3 (section C. "No chord")
	private static readonly ISet<string> PseudoChords = new HashSet<string>(ChordParser.Comparer) { "N.C.", "NC", "stop" };

	#endregion

	#region Constructors

	private ChordLine(IReadOnlyList<TextSegment> segments)
	{
		this.Segments = segments;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the text segments that make up the chord line.
	/// </summary>
	/// <remarks>
	/// This can include a mix of segment types (e.g., <see cref="ChordSegment"/> and <see cref="WhiteSpaceSegment"/>).
	/// </remarks>
	public IReadOnlyList<TextSegment> Segments { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse the current line as sequence of whitespace and chords.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if the line was parsed as whitespace and chords. Null otherwise.</returns>
	public static ChordLine? TryParse(LineContext context)
	{
		ChordLine? result = null;

		List<TextSegment> segments = new();
		Lexer lexer = context.CreateLexer(out IReadOnlyList<Entry> annotations);
		while (lexer.Read())
		{
			if (lexer.Token.Type == TokenType.WhiteSpace)
			{
				segments.Add(new WhiteSpaceSegment(lexer.Token.Text));
			}
			else if (lexer.Token.Text.All(ch => !char.IsLetter(ch))
				|| PseudoChords.Contains(lexer.Token.Text)
				|| (lexer.Token.Text[0] == '(' && lexer.Token.Text[^1] == ')'))
			{
				// Allow tokens with no letter (e.g., ~↑↓*), pseudo-chords, or annotations in parentheses.
				segments.Add(new TextSegment(lexer.Token.ToString(), lexer.Token.Index));
			}
			else if (lexer.Token.Text[^1] == '*' && Chord.TryParse(lexer.Token.Text[0..^1], out Chord? chord))
			{
				// Allow chords to end with an asterisk since they probably relate to a comment or footnote later.
				segments.Add(new ChordSegment(chord, lexer.Token.Index, lexer.Token.ToString()));
			}
			else if (Chord.TryParse(lexer.Token.Text, out chord))
			{
				segments.Add(new ChordSegment(chord, lexer.Token.Index, lexer.Token.ToString()));
			}
			else
			{
				segments.Clear();
				break;
			}
		}

		if (segments.Count > 0)
		{
			result = new(segments);
			result.AddAnnotations(annotations);
		}

		return result;
	}

	/// <summary>
	/// Gets chord line text.
	/// </summary>
	public override string ToString() => string.Concat(this.Segments);

	#endregion
}
