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
		// Has to start with A-G (case insensitive).
		// Can contain at most one '/' (for slash chords a.k.a. split chords).
		// Can contain multiple '#', digits, "add", "sus", "m", "M", "dim"
		// TODO: Finish TryParse. [Bill, 7/21/2023]
		chord = new(text ?? "?");
		return true;
	}

	#endregion
}
