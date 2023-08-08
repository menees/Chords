﻿namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A line of unbracketed chord names and optional notes (e.g., N.C.) and annotations.
/// </summary>
public sealed class ChordLine : SegmentedEntry
{
	#region Constructors

	private ChordLine(IReadOnlyList<TextSegment> segments)
		: base(segments)
	{
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
		Conditions.RequireReference(context);

		IReadOnlyList<TextSegment> segments = TryGetSegments(context, false, null, out IReadOnlyList<Entry> annotations);

		ChordLine? result = null;
		if (segments.Count > 0)
		{
			result = new(segments);
			result.AddAnnotations(annotations);
		}

		return result;
	}

	#endregion
}
