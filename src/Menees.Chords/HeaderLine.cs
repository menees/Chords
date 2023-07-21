namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A "Ultimate Guitar"-style bracketed header (e.g., [Chorus], [Verse]).
/// </summary>
public sealed class HeaderLine : LabeledEntry
{
	#region Constructors

	private HeaderLine(string? label, string? comment)
		: base(label)
	{
		this.Comment = comment;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets an optional comment that followed the header's label (on the same line).
	/// </summary>
	/// <remarks>
	/// This is used for cases like "[Verse (Softer)]" and "[Verse] - Softer" where
	/// "Verse" is the header label, and "Softer" is the header comment.
	/// </remarks>
	public string? Comment { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse a header line from the current context.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if <paramref name="context"/>'s line was parsed. Null otherwise.</returns>
	public static HeaderLine? TryParse(LineContext context)
	{
		// "Alone With You - Outfield.docx" has some inside the brackets. [Bill, 7/21/2023]
		// "Line A Stone - Original.docx" has some outside the brackets.
		// TODO: Finish TryParse. [Bill, 7/21/2023]
		context.GetHashCode();
		return null;
	}

	#endregion
}
