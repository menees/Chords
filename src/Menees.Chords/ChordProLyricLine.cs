namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A line of interlaced lyrics and chords in ChordPro format.
/// </summary>
public sealed class ChordProLyricLine : SegmentedEntry
{
	#region Constructors

	private ChordProLyricLine(IReadOnlyList<TextSegment> segments)
		: base(segments)
	{
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Converts a <see cref="ChordLine"/> into a <see cref="ChordProLyricLine"/>
	/// (e.g., by bracketing the chord names).
	/// </summary>
	/// <param name="chords">The chord line to convert.</param>
	/// <returns>A new ChordPro line with bracketed chords.</returns>
	public static ChordProLyricLine Convert(ChordLine chords)
	{
		List<TextSegment> segments = new(chords.Segments.Count);

		foreach (TextSegment segment in chords.Segments)
		{
			if (segment is ChordSegment chord && !(chord.Text.StartsWith('[') && chord.Text.EndsWith(']')))
			{
				segments.Add(new ChordSegment(chord.Chord, $"[{chord.Chord.Name}]"));
			}
			else
			{
				segments.Add(segment);
			}
		}

		ChordProLyricLine result = new(segments);
		result.AddAnnotations(chords.Annotations);
		return result;
	}

	/// <summary>
	/// Converts a <see cref="ChordLyricPair"/> into a <see cref="ChordProLyricLine"/>.
	/// (e.g., by bracketing the chord names and placing them inline with the lyrics).
	/// </summary>
	/// <param name="pair">The chords/lyrics pair to convert.</param>
	/// <returns>A new ChordPro lyric line with bracketed chords inline with lyrics.</returns>
	public static ChordProLyricLine Convert(ChordLyricPair pair)
	{
		// TODO: Also construct from ChordLyricPair [Bill, 7/31/2023]
		return null!;
	}

	/// <summary>
	/// Tries to parse the current line as a ChordPro content line (i.e., interlaced chords and lyrics).
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if the line contains interlaced chords and lyrics.</returns>
	public static ChordProLyricLine? TryParse(LineContext context)
	{
		// If we see a bracketed token that's not a chord, then skip this line.
		// This doesn't try to handle ChordPro preprocessor conditional chords.
		// https://www.chordpro.org/chordpro/support-hints-and-tips/
		IReadOnlyList<TextSegment> segments = TryGetSegments(
			context,
			true,
			token => token.Type == TokenType.Text ? new TextSegment(token.Text) : null,
			out IReadOnlyList<Entry> annotations);

		// If there are no chords, then skip this line.
		ChordProLyricLine? result = null;
		if (segments.OfType<ChordSegment>().Any())
		{
			result = new(segments);
			result.AddAnnotations(annotations);
		}

		return result;
	}

	#endregion
}
