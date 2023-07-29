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
	/// <param name="index">The segment's start index in the line it was extracted from.</param>
	/// <param name="text">The text for the chord (e.g., if it was originally bracketed).
	/// If this is null, then <see cref="Chord.Name"/> is used.</param>
	public ChordSegment(Chord chord, int index = 0, string? text = null)
		: base(text ?? chord?.Name!, index)
	{
		this.Chord = chord ?? throw new ArgumentNullException(nameof(chord));
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the chord named in the segment.
	/// </summary>
	public Chord Chord { get; }

	#endregion
}
