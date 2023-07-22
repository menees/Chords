namespace Menees.Chords;

/// <summary>
/// A sequential <see cref="ChordLine"/> and <see cref="TextLine"/> pair within a <see cref="Section"/>.
/// </summary>
public sealed class ChordLyricPair : Entry
{
	#region Constructors

	/// <summary>
	/// Creates a new instance for a pair of related chord and lyric lines.
	/// </summary>
	/// <param name="chords">The chord line (i.e., top line).</param>
	/// <param name="lyrics">The lyric line (i.e., bottom line).</param>
	public ChordLyricPair(ChordLine chords, TextLine lyrics)
	{
		this.Chords = chords;
		this.Lyrics = lyrics;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the line of chords.
	/// </summary>
	public ChordLine Chords { get; }

	/// <summary>
	/// Gets the line of lyrics.
	/// </summary>
	public TextLine Lyrics { get; }

	#endregion
}
