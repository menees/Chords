namespace Menees.Chords;

/// <summary>
/// A portion of a ChordPro lyric line containing non-chord text that is displayed in the chord row.
/// </summary>
/// <seealso href="https://www.chordpro.org/chordpro/chordpro-chords/"/>
internal sealed class ChordAnnotationSegment : TextSegment
{
	#region Private Data Members

	private const int MinimumLength = 3;

	#endregion

	#region Constructors

	/// <summary>Creates a new instance from bracketed ChordPro annotation text such as <c>[*4x]</c>.</summary>
	/// <param name="text">The bracketed annotation text.</param>
	internal ChordAnnotationSegment(string text)
		: base(text)
	{
		Conditions.RequireArgument(
			text?.StartsWith('[') == true && text.EndsWith(']') && text.Length >= MinimumLength,
			"A chord annotation must be enclosed in ChordPro brackets.");
		int contentStart = text.StartsWith("[*", StringComparison.Ordinal) ? 2 : 1;
		this.Annotation = text[contentStart..^1];
	}

	#endregion

	#region Public Properties

	/// <summary>Gets the display text without the ChordPro brackets and leading asterisk.</summary>
	internal string Annotation { get; }

	#endregion
}
