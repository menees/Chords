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

	internal Chord(string name, string root, IReadOnlyList<string> modifiers, string? bass, string? annotation, Notation notation)
	{
		this.Name = name;
		this.Root = root;
		this.Modifiers = modifiers;
		this.Bass = bass;
		this.Annotation = annotation;
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
	/// Gets any modifiers used in the chord <see cref="Name"/> between the <see cref="Root"/> and <see cref="Bass"/>.
	/// </summary>
	public IReadOnlyList<string> Modifiers { get; }

	/// <summary>
	/// Gets the chord's bass note if any (i.e., if this is a slash chord).
	/// </summary>
	/// <seealso cref="Notation"/>
	/// <seealso href="https://en.wikipedia.org/wiki/Chord_notation#Slash_chords"/>
	public string? Bass { get; }

	/// <summary>
	/// Gets any short suffix used to indicate a footnote or special chord.
	/// </summary>
	/// <remarks>
	/// This will typically be a single character like '*', '~', '←', '↑', '↓', or '→'.
	/// This may be extended to allow other known multi-character suffxes in the future.
	/// </remarks>
	public string? Annotation { get; }

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
		Conditions.RequireNonWhiteSpace(text);

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
	/// Changes this chord to the specified notation relative to <paramref name="key"/>.
	/// </summary>
	/// <param name="notation">The notation to use.</param>
	/// <param name="key">The key used to interpret key-relative notation.</param>
	/// <returns>A new chord instance if its notation changed, or the same instance otherwise.</returns>
	public Chord ChangeNotation(Notation notation, Key key)
	{
		if (!Enum.IsDefined(notation))
		{
			throw new ArgumentOutOfRangeException(nameof(notation));
		}

		Chord result = this;
		if (notation != this.Notation)
		{
			Conditions.RequireNonNull(key);
			string root = MusicTheory.ChangeNoteNotation(this.Root, this.Notation, notation, key);
			string? bass = this.Bass is null ? null : MusicTheory.ChangeNoteNotation(this.Bass, this.Notation, notation, key);
			IReadOnlyList<string> modifiers = this.Modifiers;
			int romanRootIndex = this.Root[0] is '#' or 'b' ? 1 : 0;
			bool sourceRomanMinor = this.Notation == Notation.Roman && char.IsLower(this.Root[romanRootIndex]);
			if (notation == Notation.Roman && MusicTheory.IsMinor(modifiers))
			{
				root = root.ToLowerInvariant();
				modifiers = [.. modifiers.Skip(1)];
			}
			else if (sourceRomanMinor && !MusicTheory.IsMinor(modifiers))
			{
				modifiers = ["m", .. modifiers];
			}

			result = this.WithNotes(root, modifiers, bass, notation);
		}

		return result;
	}

	/// <summary>
	/// Transposes this chord by the specified number of half steps.
	/// </summary>
	/// <param name="halfSteps">The signed number of half steps. Values outside one octave wrap around.</param>
	/// <param name="accidentalPreference">Which accidental names should be used.</param>
	/// <returns>A new chord instance if its named notes change, or the same instance otherwise.</returns>
	/// <remarks>
	/// Nashville and Roman chords are key-relative, so they are returned unchanged.
	/// With <see cref="AccidentalPreference.Default"/>, positive values use sharps and negative values use flats.
	/// The other preferences explicitly select sharps or flats.
	/// </remarks>
	public Chord Transpose(sbyte halfSteps, AccidentalPreference accidentalPreference = AccidentalPreference.Default)
	{
		if (!Enum.IsDefined(accidentalPreference))
		{
			throw new ArgumentOutOfRangeException(nameof(accidentalPreference));
		}

		halfSteps = MusicTheory.NormalizeTranspose(halfSteps);
		Chord result = this;
		if (halfSteps != 0 && this.Notation == Notation.Name)
		{
			string root = MusicTheory.TransposeNamedNote(this.Root, halfSteps, accidentalPreference);
			string? bass = this.Bass is null ? null : MusicTheory.TransposeNamedNote(this.Bass, halfSteps, accidentalPreference);
			result = this.WithNotes(root, this.Modifiers, bass, this.Notation);
		}

		return result;
	}

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
				result = this.WithNotes(normalizedRoot, this.Modifiers, normalizedBass, this.Notation);
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

	private Chord WithNotes(string root, IReadOnlyList<string> modifiers, string? bass, Notation notation)
	{
		StringBuilder sb = new(this.Name.Length);
		sb.Append(root);
		foreach (string modifier in modifiers)
		{
			sb.Append(modifier);
		}

		if (bass is not null)
		{
			sb.Append('/');
			sb.Append(bass);
		}

		sb.Append(this.Annotation);
		return new(sb.ToString(), root, modifiers, bass, this.Annotation, notation);
	}

	#endregion
}
