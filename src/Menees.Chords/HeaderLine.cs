namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A "Ultimate Guitar"-style bracketed header (e.g., [Chorus], [Verse]).
/// </summary>
public sealed class HeaderLine : Entry
{
	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="text">The header label or section name.</param>
	public HeaderLine(string text)
	{
		this.Text = text;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the main header label (e.g., Chorus, Verse #, Bridge).
	/// </summary>
	public string Text { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse a header line from the current context.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if <paramref name="context"/>'s line was parsed. Null otherwise.</returns>
	public static HeaderLine? TryParse(LineContext context)
	{
		// Criteria: Only one '[' and only one ']'. Text in brackets is not a chord.
		// Label: Text inside brackets excluding any trailing comment.
		// Comment: Parenthesized text inside brackets. Text after '-' inside brackets. Or text after brackets.
		// Known: Intro, Outro, Verse, Verse #, Chorus, Interlude, Bridge, Pre-Chorus, Solo, Solo #, Break, Post-Chorus, Pre-Verse
		//
		// "Alone With You - Outfield.docx" has some inside the brackets. Also, "Jack and Diane - John Mellencamp.txt" [Bill, 7/21/2023]
		// "Line A Stone - Original.docx" has some outside the brackets. Also, "Authority Song - John Mellencamp.docx"
		// TODO: Finish TryParse. [Bill, 7/21/2023]
		//
		// This is used for cases like "[Verse (Softer)]", "[Verse - Softer]" and "[Verse] - Softer" where
		// "Verse" is the header label, and "Softer" is the header comment.
		context.GetHashCode();
		return null;
	}

	/// <summary>
	/// Returns <see cref="Text"/>.
	/// </summary>
	public override string ToString() => this.Text;

	#endregion
}
