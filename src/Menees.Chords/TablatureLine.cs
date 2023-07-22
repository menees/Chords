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
public sealed class TablatureLine : Entry
{
	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="noteLength">The length of the leading note.</param>
	/// <param name="text">The text of the tablature line.</param>
	private TablatureLine(int noteLength, string text)
	{
		this.NoteLength = noteLength;
		this.Text = text;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the length of the note at the beginning of the tab <see cref="Text"/>.
	/// </summary>
	public int NoteLength { get; }

	/// <summary>
	/// Gets the text of the tablature line.
	/// </summary>
	public string Text { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse the current line as a tablature line.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if the line starts with "Note|" or "Note:".</returns>
	public static TablatureLine? TryParse(LineContext context)
	{
		TablatureLine? result = null;

		string line = context.LineText.Trim();
		if (!string.IsNullOrEmpty(line))
		{
			int noteLength = ChordParser.GetNoteLength(line);
			if (noteLength > 0 && line.Length > noteLength && (line[noteLength] == '|' || line[noteLength] == ':'))
			{
				result = new(noteLength, line);
			}
		}

		return result;
	}

	/// <summary>
	/// Returns <see cref="Text"/>.
	/// </summary>
	public override string ToString() => this.Text;

	#endregion
}
