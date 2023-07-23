namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A line of interlaced chords and lyrics in ChordPro format.
/// </summary>
public sealed class ChordProContent : TextEntry
{
	#region Constructors

	private ChordProContent(string text)
		: base(text)
	{
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse the current line as a ChordPro content line (i.e., interlaced chords and lyrics).
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if the line contains interlaced chords and lyrics.</returns>
	public static ChordProContent? TryParse(LineContext context)
	{
		ChordProContent? result = null;

		// Line with embedded [id] tokens.
		// Also construct from ChordLine and ChordLyricPair
		// TODO: TryParse using Regex.Split? [Bill, 7/21/2023]
		context.GetHashCode();
		return result;
	}

	#endregion
}
