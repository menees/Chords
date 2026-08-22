namespace Menees.Chords;

/// <summary>
/// Specifies which accidentals should be used when transposing named notes.
/// </summary>
public enum AccidentalPreference
{
	/// <summary>
	/// Use sharps when transposing up and flats when transposing down.
	/// </summary>
	Default,

	/// <summary>
	/// Always use sharps when an accidental is required.
	/// </summary>
	Sharps,

	/// <summary>
	/// Always use flats when an accidental is required.
	/// </summary>
	Flats,
}
