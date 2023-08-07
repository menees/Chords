namespace Menees.Chords;

#region Using Directives

using System.Collections.Generic;

#endregion

/// <summary>
/// A sequential <see cref="ChordLine"/> and <see cref="LyricLine"/> pair.
/// </summary>
public sealed class ChordLyricPair : Entry, IEntryContainer
{
	#region Constructors

	/// <summary>
	/// Creates a new instance for a pair of related chord and lyric lines.
	/// </summary>
	/// <param name="chords">The chord line (i.e., top line).</param>
	/// <param name="lyrics">The lyric line (i.e., bottom line).</param>
	public ChordLyricPair(ChordLine chords, LyricLine lyrics)
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
	public LyricLine Lyrics { get; }

	IReadOnlyList<Entry> IEntryContainer.Entries => new Entry[] { this.Chords, this.Lyrics };

	#endregion

	#region Public Methods

	/// <summary>
	/// Concatenates the <see cref="Chords"/> and <see cref="Lyrics"/> lines
	/// with an <see cref="Environment.NewLine"/> separator.
	/// </summary>
	public override string ToString() => $"{this.Chords}{Environment.NewLine}{this.Lyrics}";

	#endregion
}
