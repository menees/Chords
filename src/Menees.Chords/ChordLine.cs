namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A line of chord names and optional annotations (e.g., N.C.).
/// </summary>
public sealed class ChordLine : Entry
{
	#region Public Methods

	/// <summary>
	/// Tries to parse the current line as sequence of whitespace and chords.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if the line was parsed as whitespace and chords. Null otherwise.</returns>
	public static ChordLine? TryParse(LineContext context)
	{
		// Make sure most tokens are chords. Chords can be in brackets.
		// Ignore tokens in parentheses or special annotations (~↑↓*) or things with no letter.
		// Handle NC and N.C. per UG recommendation. Also [stop] from Romeo & Juliet.
		// https://www.ultimate-guitar.com/contribution/help/rubric#iii3 (section C. "No chord"
		// TODO: Finish TryParse. [Bill, 7/26/2023]
		context.GetHashCode();
		return null;
	}

	/// <summary>
	/// Gets chord line text.
	/// </summary>
	public override string ToString() => "TODO"; // TODO: Finish ChordLine.ToString(). [Bill, 7/22/2023]

	#endregion
}
