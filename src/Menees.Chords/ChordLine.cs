namespace Menees.Chords;

/// <summary>
/// A line of chord names and optional annotations (e.g., N.C.).
/// </summary>
public sealed class ChordLine : Entry
{
	#region Public Methods

	// Make sure most tokens are chords.
	// Ignore tokens in parentheses or special annotations (~↑↓*)
	// Handle NC and N.C. per UG recommendation. Also [stop] from Romeo & Juliet.
	// https://www.ultimate-guitar.com/contribution/help/rubric#iii3 (section C. "No chord"
	// TODO: Parse [Bill, 7/21/2023]

	/// <summary>
	/// Gets chord line text.
	/// </summary>
	public override string ToString() => "TODO"; // TODO: Finish ChordLine.ToString(). [Bill, 7/22/2023]

	#endregion
}
