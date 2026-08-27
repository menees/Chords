namespace Menees.Chords;

/// <summary>
/// A portion of a text line that has been parsed as a <see cref="Chord"/>.
/// </summary>
public sealed class ChordSegment : TextSegment
{
	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="chord">The chord name in the text segment.</param>
	/// <param name="text">The text for the chord (e.g., if it was originally bracketed).
	/// If this is null, then <see cref="Chord.Name"/> is used.</param>
	public ChordSegment(Chord chord, string? text = null)
		: base(text ?? chord?.Name!)
	{
		Conditions.RequireNonNull(chord);
		this.Chord = chord;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the chord named in the segment.
	/// </summary>
	public Chord Chord { get; }

	#endregion

	#region Internal Properties

	/// <summary>
	/// Gets whether the chord's original text is enclosed in parentheses.
	/// </summary>
	internal bool IsParenthesized => TryParseParenthesized(this.Text) is not null;

	#endregion

	#region Internal Methods

	internal static Chord? TryParseParenthesized(string text)
	{
		Chord? result = null;
		if (text?.Length > 2 && text[0] == '(' && text[^1] == ')')
		{
			string inner = text.Substring(1, text.Length - 2);
			if (!char.IsLetter(inner[0]) || char.IsUpper(inner[0]))
			{
				// explicit for CA1806
				result = Chord.TryParse(inner, out Chord? parsed) ? parsed : null;
			}
		}

		return result;
	}

	internal ChordSegment ChangeChord(Func<Chord, Chord> change)
	{
		Chord chord = change(this.Chord);
		ChordSegment result = this;
		if (!ReferenceEquals(chord, this.Chord))
		{
			int index = this.Text.IndexOf(this.Chord.Name, StringComparison.Ordinal);
			string text = index < 0 ? chord.Name
				: this.Text.Remove(index, this.Chord.Name.Length).Insert(index, chord.Name);
			result = new(chord, text);
		}

		return result;
	}

	#endregion
}