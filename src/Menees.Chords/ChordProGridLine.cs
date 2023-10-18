namespace Menees.Chords;

#region Using Directives

using System.Linq;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A ChordPro line within a chord grid.
/// </summary>
/// <remarks>
/// Example:
/// <c>|  Am . . . | C . . . | E  . . . | E  . . . |</c>
/// </remarks>
/// <seealso href="https://www.chordpro.org/chordpro/directives-env_grid/"/>
public sealed class ChordProGridLine : SegmentedEntry
{
	#region Constructors

	private ChordProGridLine(IReadOnlyList<TextSegment> segments)
		: base(segments)
	{
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse the current line as a ChordPro grid line.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if the line is inside a start_of_grid section that hasn't had a matching end_of_grid.</returns>
	public static ChordProGridLine? TryParse(LineContext context)
	{
		Conditions.RequireNonNull(context);

		ChordProGridLine? result = null;

		// The ChordPro grid line syntax is very loose, and ChordPro's examples include things
		// that the documentation says are not allowed. So, we'll require the line to be inside
		// an open start_of_grid/end_of_grid section, or the line has to have multiple '|' separators.
		// If we're not in an explicit grid, then we have to be careful not to match tab lines.
		bool inExplicitGrid = context.State.TryGetValue(ChordProDirectiveLine.GridStateKey, out object? gridState) && gridState is ChordProDirectiveLine;
		const int MinSeparators = 2;
		if (inExplicitGrid || context.LineText.Count(ch => ch == '|') >= MinSeparators)
		{
			IReadOnlyList<TextSegment> segments = TryGetSegments(
				context,
				false,
				token => inExplicitGrid ? new TextSegment(token.Text) : null,
				out IReadOnlyList<Entry> annotations);

			if (segments.Count > 0)
			{
				if (inExplicitGrid)
				{
					result = new ChordProGridLine(segments);
				}
				else
				{
					// Don't allow Nashville-numbered chords because it's ambiguous with TablatureLines.
					List<ChordSegment> chords = segments.OfType<ChordSegment>().ToList();
					if (chords.Count >= (MinSeparators - 1)
						&& chords.All(cs => cs.Chord.Notation == Notation.Name || cs.Chord.Notation == Notation.Roman))
					{
						result = new ChordProGridLine(segments);
					}
				}

				result?.AddAnnotations(annotations);
			}
		}

		return result;
	}

	#endregion
}
