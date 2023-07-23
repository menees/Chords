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
public sealed class ChordGridLine : Entry
{
	#region Constructors

	internal ChordGridLine(string text)
	{
		this.Text = text;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the chord grid line's text.
	/// </summary>
	public string Text { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse the current line as a tablature line.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if the line starts with "Note|" or "Note:".</returns>
	public static ChordGridLine? TryParse(LineContext context)
	{
		ChordGridLine? result = null;

		// The chord grid line syntax is very loose, and ChordPro's examples include things
		// that the syntax says are not allowed. So, we'll require the line to be inside an
		// open start_of_grid/end_of_grid section.
		if (context.State.TryGetValue(ChordProDirective.GridStateKey, out object? gridState) && gridState is ChordProDirective)
		{
			result = new ChordGridLine(context.LineText);
		}

		return result;
	}

	/// <summary>
	/// Returns <see cref="Text"/>.
	/// </summary>
	public override string ToString() => this.Text;

	#endregion
}
