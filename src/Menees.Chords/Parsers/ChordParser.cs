namespace Menees.Chords.Parsers;

#region Using Directives

using System.Diagnostics.CodeAnalysis;

#endregion

/// <summary>
/// Parses <see cref="Chord"/> names.
/// </summary>
public sealed class ChordParser
{
	#region Public Constants

	/// <summary>
	/// Gets the case-insensitive mode used to compare strings.
	/// </summary>
	public const StringComparison Comparison = StringComparison.CurrentCultureIgnoreCase;

	#endregion

	#region Private Data Members

	// These must listed order from longest to shortest.
	private static readonly string[] RomanNumerals = new[] { "III", "VII", "II", "IV", "VI", "I", "V" };

	// These must listed order from longest to shortest.
	private static readonly string[] KnownModifiers = new[]
	{
		"add", "sus", "dim", "min", "maj", "aug", "dom", "alt", // 3 char modifiers
		"11", "13", // 2 char modifiers
		"2", "3", "4", "5", "6", "7", "9", "#", "b", "m", "-", "+", "Δ", "°", "ø", "^", // 1 char modifiers

		// Notes: These are matched case-insensitively, so we don't need to include "M".
		// ChordPro's chord implementation allows '^' for "maj" and '-' for "m" for a minor chord.
		// https://www.chordpro.org/chordpro/chordpro-chords/
		// Wikipedia's "Chord notation" articles mention the others.
		// https://en.wikipedia.org/wiki/Chord_notation
		// https://en.wikibooks.org/wiki/Music_Theory/Complete_List_of_Chord_Patterns
	};

	private readonly List<string> errors = new();
	private Notation notation;
	private int index;

	#endregion

	#region Constructors

	/// <summary>
	/// Creates a new instance to parse <paramref name="text"/>.
	/// </summary>
	/// <param name="text">The text to parse.</param>
	public ChordParser(string text)
		: this(text, 0, text?.Length ?? 0)
	{
	}

	/// <summary>
	/// Creates a new instance to parse a substring of <paramref name="text"/>
	/// </summary>
	/// <param name="text">The text to parse.</param>
	/// <param name="startIndex">The index in <paramref name="text"/> to start parsing from.</param>
	/// <param name="length">The number of characters to parse from <paramref name="text"/>.</param>
	public ChordParser(string text, int startIndex, int length)
	{
		this.Text = (text ?? string.Empty).Substring(startIndex, length).Trim();
		this.Parse();

		if (this.Chord is null && this.errors.Count == 0)
		{
			this.errors.Add($"Cannot parse \"{text}\" as a chord.");
		}
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the text to be parsed.
	/// </summary>
	public string Text { get; }

	/// <summary>
	/// Gets the chord that was parsed from <see cref="Text"/> if <see cref="Errors"/> is empty.
	/// </summary>
	public Chord? Chord { get; private set; }

	/// <summary>
	/// Gets the errors that occurred while parsing <see cref="Text"/>.
	/// </summary>
	public IReadOnlyList<string> Errors => this.errors;

	#endregion

	#region Public Methods

	/// <summary>
	/// Gets the length of a note in <paramref name="text"/> at <paramref name="startIndex"/>.
	/// </summary>
	/// <param name="text">The text to match in.</param>
	/// <param name="startIndex">The 0-based index to start matching.</param>
	/// <returns>1 for a single letter note match (e.g., A, G), 2 for a sharp or flat match (e.g., A#, Bb), or 0 if no match.</returns>
	public static int GetNoteLength(string text, int startIndex = 0)
	{
		int result = 0;

		if (text.Length > startIndex)
		{
			char ch = text[startIndex];
			if ((ch >= 'A' && ch <= 'G') || (ch >= 'a' && ch <= 'g'))
			{
				result = 1;
				if (text.Length > (startIndex + 1))
				{
					ch = text[startIndex + 1];
					if (ch == '#' || ch == 'b')
					{
						result = 2;
					}
				}
			}
		}

		return result;
	}

	#endregion

	#region Private Methods

	private static int GetNashvilleNumberLength(string text, int startIndex = 0)
	{
		int result = 0;

		if (text.Length > startIndex)
		{
			char ch = text[startIndex];

			// Nashville numbering puts the #/b modifier before the number.
			if ((ch == '#' || ch == 'b') && text.Length > (startIndex + 1))
			{
				ch = text[startIndex + 1];
				if (ch >= '1' && ch <= '7')
				{
					result = 2;
				}
			}
			else if (ch >= '1' && ch <= '7')
			{
				result = 1;
			}
		}

		return result;
	}

	private static int GetRomanNumeralLength(string text, int startIndex = 0)
	{
		int result = 0;

		if (text.Length > startIndex)
		{
			int matchStart = startIndex;
			char ch = text[startIndex];
			if ((ch == '#' || ch == 'b') && text.Length > (startIndex + 1))
			{
				matchStart++;
			}

			foreach (string numeral in RomanNumerals)
			{
				if (MatchAhead(text, numeral, matchStart))
				{
					result = numeral.Length + matchStart - startIndex;
					break;
				}
			}
		}

		return result;
	}

	private static bool MatchAhead(string text, string match, int startIndex)
		=> ((startIndex + match.Length) <= text.Length) && text.Substring(startIndex, match.Length).Equals(match, Comparison);

	private void Parse()
	{
		if (this.ParseRoot(out string? root))
		{
			List<string> modifiers = new();
			while (this.TryParseModifier(modifiers))
			{
			}

			string? bass = this.TryParseBass();

			if (this.index < this.Text.Length)
			{
				this.errors.Add($"Unsupported characters at the end of chord \"{this.Text}\": {this.Text.Substring(this.index)}");
			}

			if (this.errors.Count == 0)
			{
				this.Chord = new(this.Text, root, modifiers, bass, this.notation);
			}
		}
		else
		{
			this.errors.Add($"Unable to parse root for chord \"{this.Text}\".");
		}
	}

	private bool ParseRoot([MaybeNullWhen(false)] out string root)
	{
		root = null;

		int rootLength;
		if ((rootLength = GetNoteLength(this.Text, this.index)) > 0)
		{
			this.notation = Notation.Name;
		}
		else if ((rootLength = GetNashvilleNumberLength(this.Text, this.index)) > 0)
		{
			this.notation = Notation.Nashville;
		}
		else if ((rootLength = GetRomanNumeralLength(this.Text, this.index)) > 0)
		{
			this.notation = Notation.Roman;
		}

		if (rootLength > 0)
		{
			root = this.Text.Substring(0, rootLength);
			this.index = rootLength;
		}

		return root != null;
	}

	private bool TryParseModifier(List<string> modifiers)
	{
		// This is a bit looser than ChordPro's list of known qualifiers and extensions.
		// https://www.chordpro.org/chordpro/chordpro-directives/
		// I'm not supporting rarely used notations like parentheses or '|'-separated polychords.
		// https://music.stackexchange.com/questions/13976/what-does-a-number-inside-a-parentheses-in-a-chord-name-mean-example-b79b9
		// https://en.wikibooks.org/wiki/Music_Theory/Complete_List_of_Chord_Patterns#Polychords
		bool result = false;

		foreach (string modifier in KnownModifiers)
		{
			if (this.MatchAhead(modifier))
			{
				// Get the exact case modifier from Text because 'm' != 'M'.
				string exactModifier = this.Text.Substring(this.index, modifier.Length);
				modifiers.Add(exactModifier);
				this.index += exactModifier.Length;
				result = true;
				break;
			}
		}

		return result;
	}

	private string? TryParseBass()
	{
		string? result = null;

		if (this.Peek() == '/')
		{
			int bassLength = 0;
			int startIndex = this.index + 1;
			switch (this.notation)
			{
				case Notation.Name:
					bassLength = GetNoteLength(this.Text, startIndex);
					break;
				case Notation.Nashville:
					bassLength = GetNashvilleNumberLength(this.Text, startIndex);
					break;
				case Notation.Roman:
					bassLength = GetRomanNumeralLength(this.Text, startIndex);
					break;
			}

			if (bassLength == 0)
			{
				this.errors.Add($"Unable to parse bass for chord \"{this.Text}\".");
			}
			else
			{
				result = this.Text.Substring(startIndex, bassLength);
				this.index = startIndex + bassLength;
			}
		}

		return result;
	}

	private char? Peek(int offset = 0) => (this.index + offset) < this.Text.Length ? this.Text[this.index + offset] : null;

	private bool MatchAhead(string match) => MatchAhead(this.Text, match, this.index);

	#endregion
}
