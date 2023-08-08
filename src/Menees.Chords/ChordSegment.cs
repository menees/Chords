namespace Menees.Chords;

/// <summary>
/// A portion of a text line that has been parsed as a <see cref="Chord"/>.
/// </summary>
public sealed class ChordSegment : TextSegment
{
	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="chord">The chord name in the text segment.</param>
	/// <param name="text">The text for the chord (e.g., if it was originally bracketed).
	/// If this is null, then <see cref="Chord.Name"/> is used.</param>
	public ChordSegment(Chord chord, string? text = null)
		: base(text ?? chord?.Name!)
	{
		Conditions.RequireReference(chord);
		this.Chord = chord;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the chord named in the segment.
	/// </summary>
	public Chord Chord { get; }

	#endregion
}
