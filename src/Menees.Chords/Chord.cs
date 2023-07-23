namespace Menees.Chords;

#region Using Directives

using System;
using System.Diagnostics.CodeAnalysis;

#endregion

/// <summary>
/// A named chord (e.g., Am, C#7b5/D).
/// </summary>
public sealed class Chord
{
	#region Constructors

	private Chord(string name)
	{
		this.Name = name;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the name of the chord.
	/// </summary>
	public string Name { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Parses <paramref name="text"/> as a chord name.
	/// </summary>
	/// <param name="text">The chord name to parse.</param>
	/// <returns>A new chord instance.</returns>
	/// <exception cref="FormatException"><paramref name="text"/> is not a valid chord name.</exception>
	public static Chord Parse(string text)
	{
		if (!TryParse(text, out Chord? chord))
		{
			throw new FormatException($"Cannot parse {text} as a chord.");
		}

		return chord;
	}

	/// <summary>
	/// Tries to parse <paramref name="text"/> as a chord name.
	/// </summary>
	/// <param name="text">The chord name to parse.</param>
	/// <param name="chord">Returns a new chord instance if the <paramref name="text"/> was parsed.</param>
	/// <returns>True if <paramref name="text"/> was parsed and a <paramref name="chord"/> returned. False otherwise.</returns>
	public static bool TryParse([NotNullWhen(true)] string? text, [MaybeNullWhen(false)] out Chord chord)
	{
		// Ignore leading and trailing whitespace.
		// Named has to start with A-G (case insensitive).
		// Handle Nashville numbered chords (including slash chords like 1sus4/#3).
		// Handle Roman numeral chords
		// Can contain at most one '/' (for https://en.wikipedia.org/wiki/Slash_chord).
		// Can contain multiple '#', digits, "add", "sus", "m", "M", "dim", "min", "maj", "aug", "-", "+", "dom", Δ, "alt", °, ø, |
		// Can contain parentheses.
		// https://music.stackexchange.com/questions/13976/what-does-a-number-inside-a-parentheses-in-a-chord-name-mean-example-b79b9
		// Normalize keys B#, E#, Cb and Fb to C, F, B and E
		// https://en.wikipedia.org/wiki/Chord_notation
		// https://en.wikibooks.org/wiki/Music_Theory/Complete_List_of_Chord_Patterns
		// https://www.chordpro.org/chordpro/chordpro-chords/
		// TODO: Finish TryParse. [Bill, 7/21/2023]
		chord = new(text ?? "?");
		return true;
	}

	/// <summary>
	/// Returns the chord <see cref="Name"/>.
	/// </summary>
	public override string ToString() => this.Name;

	#endregion
}
