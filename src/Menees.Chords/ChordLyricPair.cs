namespace Menees.Chords;

#region Using Directives

using System.Collections.Generic;
using System.IO;

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
		Conditions.RequireNonNull(chords);
		Conditions.RequireNonNull(lyrics);

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
	/// with a <see cref="TextWriter.WriteLine()"/> separator.
	/// </summary>
	/// <param name="writer">Used to write the output.</param>
	/// <param name="includeAnnotations">Whether annotations should be appended at the end of the entries.</param>
	public override void Write(TextWriter writer, bool includeAnnotations)
	{
		Conditions.RequireNonNull(writer);
		this.Chords.Write(writer, includeAnnotations);
		writer.WriteLine();
		this.Lyrics.Write(writer, includeAnnotations);
	}

	#endregion

	#region Protected Methods

	/// <inheritdoc/>
	protected override void WriteWithoutAnnotations(TextWriter writer)
		=> this.Write(writer, false);

	#endregion
}
