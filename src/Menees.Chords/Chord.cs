namespace Menees.Chords;

#region Using Directives

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A named chord (e.g., Am, C#7b5/D).
/// </summary>
public sealed class Chord
{
	#region Constructors

	internal Chord(string name, string root, IReadOnlyList<string> modifiers, string? bass, Notation notation)
	{
		this.Name = name;
		this.Root = root;
		this.Modifiers = modifiers;
		this.Bass = bass;
		this.Notation = notation;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the full name of the chord.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets the chord's root note.
	/// </summary>
	/// <seealso cref="Notation"/>
	public string Root { get; }

	/// <summary>
	/// Gets any modifiers use in the chord <see cref="Name"/> between the <see cref="Root"/> and <see cref="Bass"/>.
	/// </summary>
	public IReadOnlyList<string> Modifiers { get; }

	/// <summary>
	/// Gets the chord's bass note if any (i.e., if this is a slash chord).
	/// </summary>
	/// <seealso cref="Notation"/>
	/// <seealso href="https://en.wikipedia.org/wiki/Chord_notation#Slash_chords"/>
	public string? Bass { get; }

	/// <summary>
	/// Gets the notation system used to transcribe the chord.
	/// </summary>
	public Notation Notation { get; }

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
		ChordParser parser = new(text ?? string.Empty);
		if (parser.Chord is null)
		{
			throw new FormatException(string.Join(Environment.NewLine, parser.Errors));
		}

		return parser.Chord;
	}

	/// <summary>
	/// Tries to parse <paramref name="text"/> as a chord name.
	/// </summary>
	/// <param name="text">The chord name to parse.</param>
	/// <param name="chord">Returns a new chord instance if the <paramref name="text"/> was parsed.</param>
	/// <returns>True if <paramref name="text"/> was parsed and a <paramref name="chord"/> returned. False otherwise.</returns>
	public static bool TryParse([NotNullWhen(true)] string? text, [MaybeNullWhen(false)] out Chord chord)
	{
		bool result = false;
		chord = null;

		if (!string.IsNullOrWhiteSpace(text))
		{
			ChordParser parser = new(text!);
			if (parser.Chord != null)
			{
				result = true;
				chord = parser.Chord;
			}
		}

		return result;
	}

	/// <summary>
	/// Returns the chord <see cref="Name"/>.
	/// </summary>
	public override string ToString() => this.Name;

	/// <summary>
	/// Normalizes notes B#, E#, Cb and Fb to C, F, B and E, respectively if <see cref="Notation"/> is <see cref="Notation.Name"/>.
	/// </summary>
	/// <returns>A new chord instance if a change was needed, or the same chord instance otherwise.</returns>
	public Chord Normalize()
	{
		Chord result = this;

		if (this.Notation == Notation.Name)
		{
			string normalizedRoot = NormalizeNote(this.Root);
			string? normalizedBass = this.Bass != null ? NormalizeNote(this.Bass) : null;
			if (normalizedRoot != this.Root || normalizedBass != this.Bass)
			{
				StringBuilder sb = new(this.Name.Length);
				sb.Append(normalizedRoot);
				foreach (string modifier in this.Modifiers)
				{
					sb.Append(modifier);
				}

				if (normalizedBass is not null)
				{
					sb.Append('/');
					sb.Append(normalizedBass);
				}

				result = new(sb.ToString(), normalizedRoot, this.Modifiers, normalizedBass, this.Notation);
			}
		}

		return result;
	}

	#endregion

	#region Private Methods

	private static string NormalizeNote(string note)
	{
		string result = note switch
		{
			"B#" => "C",
			"E#" => "F",
			"Cb" => "B",
			"Fb" => "E",
			"b#" => "c",
			"e#" => "f",
			"cb" => "b",
			"fb" => "e",
			_ => note,
		};

		return result;
	}

	#endregion
}
