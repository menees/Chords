namespace Menees.Chords;

/// <summary>
/// A sequential <see cref="ChordLine"/> and <see cref="LyricLine"/> pair within a section.
/// </summary>
public sealed class ChordLyricPair : Entry
{
	#region Constructors

	/// <summary>
	/// Creates a new instance for a pair of related chord and lyric lines.
	/// </summary>
	/// <param name="chordLine">The chord line (i.e., top line).</param>
	/// <param name="lyricLine">The lyric line (i.e., bottom line).</param>
	public ChordLyricPair(ChordLine chordLine, LyricLine lyricLine)
	{
		this.Chords = chordLine;
		this.Lyrics = lyricLine;
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
	public LyricLine Lyrics { get; }

	#endregion
}
