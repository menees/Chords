namespace Menees.Chords;

/// <summary>
/// How <see cref="Chord"/>s and notes are transcribed.
/// </summary>
public enum Notation
{
	/// <summary>
	/// Chords and notes are named (e.g., A, C#, Eb).
	/// </summary>
	Name,

	/// <summary>
	/// Chords and notes use the Nashville numbering system.
	/// </summary>
	/// <seealso href="https://en.wikipedia.org/wiki/Nashville_Number_System"/>
	Nashville,

	/// <summary>
	/// Chords and notes use Roman numerals.
	/// </summary>
	Roman,
}
