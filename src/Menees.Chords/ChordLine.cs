namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A line of unbracketed chord names and optional notes (e.g., N.C.) and annotations.
/// </summary>
public sealed class ChordLine : SegmentedEntry
{
	#region Constructors

	internal ChordLine(IReadOnlyList<TextSegment> segments, IEnumerable<Entry>? annotations = null)
		: base(segments)
	{
		if (annotations != null)
		{
			this.AddAnnotations(annotations);
		}
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse the current line as sequence of whitespace and chords.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if the line was parsed as whitespace and chords. Null otherwise.</returns>
	public static ChordLine? TryParse(LineContext context)
	{
		Conditions.RequireNonNull(context);

		IReadOnlyList<TextSegment> segments = TryGetSegments(context, false, null, out IReadOnlyList<Entry> annotations);

		// Make sure there's at least one chord segment, so we avoid degenerate
		// cases like "[]", which is a segment of non-letters but not a chord.
		ChordLine? result = null;
		if (segments.OfType<ChordSegment>().Any())
		{
			result = new(segments);
			result.AddAnnotations(annotations);
		}

		return result;
	}

	#endregion
}
