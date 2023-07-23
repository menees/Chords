﻿namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A line of lyrics only or otherwise unmatched text.
/// </summary>
/// <remarks>
/// A lyric line is not in ChordPro format, so it won't contain embedded [X] chords.
/// </remarks>
public sealed class TextLine : TextEntry
{
	#region Constructors

	/// <summary>
	/// Creates a new instance for the specified text.
	/// </summary>
	/// <param name="text">The lyrics or text for this line.</param>
	public TextLine(string text)
		: base(text)
	{
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Returns the current context line as a new text line.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance.</returns>
	public static TextLine Parse(LineContext context)
	{
		// TODO: Look for Comment and ChordDefinitions at the end of the line. [Bill, 7/21/2023]
		TextLine result = new(context.LineText);
		return result;
	}

	#endregion
}