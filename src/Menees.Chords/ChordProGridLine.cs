namespace Menees.Chords;

#region Using Directives

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
public sealed class ChordProGridLine : TextEntry
{
	#region Constructors

	internal ChordProGridLine(string text)
		: base(text)
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
		ChordProGridLine? result = null;

		// The ChordPro grid line syntax is very loose, and ChordPro's examples include things
		// that the documentation says are not allowed. So, we'll require the line to be inside
		// an open start_of_grid/end_of_grid section.
		if (context.State.TryGetValue(ChordProDirective.GridStateKey, out object? gridState) && gridState is ChordProDirective)
		{
			result = new ChordProGridLine(context.LineText);
		}

		return result;
	}

	#endregion
}
