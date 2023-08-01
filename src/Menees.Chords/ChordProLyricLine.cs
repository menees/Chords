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
			token => token.Type == TokenType.Text ? new TextSegment(token.Text, token.Index) : null,
			out IReadOnlyList<Entry> annotations);

		// If there are no chords, then skip this line.
		ChordProLyricLine? result = null;
		if (segments.OfType<ChordSegment>().Any())
		{
			result = new(segments);
			result.AddAnnotations(annotations);
		}

		// TODO: Also construct from ChordLine and ChordLyricPair [Bill, 7/31/2023]
		return result;
	}

	#endregion
}
