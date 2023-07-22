namespace Menees.Chords;

/// <summary>
/// A line of lyrics only or otherwise unmatched text.
/// </summary>
/// <remarks>
/// A lyric line is not in ChordPro format, so it won't contain embedded [X] chords.
/// </remarks>
public sealed class LyricLine : Entry
{
	#region Constructors

	/// <summary>
	/// Creates a new instance for the specified text.
	/// </summary>
	/// <param name="text">The lyrics or text for this line.</param>
	public LyricLine(string text)
	{
		this.Text = text;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the lyrics or text for this entry.
	/// </summary>
	public string Text { get; }

	#endregion

	// TODO: Parse [Bill, 7/21/2023]
}
