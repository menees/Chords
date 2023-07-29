namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A line of guitar tablature.
/// </summary>
/// <remarks>
/// Example:
/// <c>e|--9/11-9-7-7h9--|--9-11-12-11--| (2x)</c>
/// </remarks>
public sealed class TablatureLine : TextEntry
{
	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="noteLength">The length of the leading note.</param>
	/// <param name="text">The text of the tablature line.</param>
	private TablatureLine(int noteLength, string text)
		: base(text)
	{
		this.NoteLength = noteLength;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the length of the note at the beginning of the tab line's <see cref="TextEntry.Text"/>.
	/// </summary>
	public int NoteLength { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse the current line as a tablature line.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if the line is inside a start_of_tab/end_of_tab environment,
	/// or if the line starts with "Note|" or "Note:".</returns>
	public static TablatureLine? TryParse(LineContext context)
	{
		TablatureLine? result = null;

		Lexer lexer = context.CreateLexer();
		if (lexer.Read(skipLeadingWhiteSpace: true) && lexer.Token.Type == TokenType.Text)
		{
			int noteLength = ChordParser.GetNoteLength(lexer.Token.Text);
			if (context.State.TryGetValue(ChordProDirectiveLine.TabStateKey, out object? gridState) && gridState is ChordProDirectiveLine)
			{
				result = new(noteLength, lexer.ReadToEnd(true));
			}
			else if (noteLength > 0 && lexer.Token.Text.Length > noteLength && (lexer.Token.Text[noteLength] == '|' || lexer.Token.Text[noteLength] == ':'))
			{
				result = new(noteLength, lexer.ReadToEnd(true));
			}
		}

		return result;
	}

	#endregion
}
