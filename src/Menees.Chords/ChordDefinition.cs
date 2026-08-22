namespace Menees.Chords;

#region Using Directives

using System.Text;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// Combines a <see cref="Chord"/> with its fretted positions definition and optional fingering.
/// </summary>
public sealed class ChordDefinition
{
	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="chord">The named chord being defined.</param>
	/// <param name="definition">The fretted positions for the chord.</param>
	/// <param name="fingering">The optional finger positions for the chord.</param>
	private ChordDefinition(Chord chord, IReadOnlyList<byte?> definition, IReadOnlyList<byte?>? fingering)
	{
		this.Chord = chord;
		this.Definition = definition;
		this.Fingering = fingering;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the named chord being defined.
	/// </summary>
	public Chord Chord { get; }

	/// <summary>
	/// Gets the fretted positions for the <see cref="Chord"/>.
	/// </summary>
	/// <remarks>
	/// A null entry indicates that a string is not fretted or played.
	/// </remarks>
	public IReadOnlyList<byte?> Definition { get; }

	/// <summary>
	/// Gets the optional finger positions for the fretted positions in <see cref="Definition"/>.
	/// </summary>
	/// <remarks>
	/// A null entry indicates that a string is not fretted or played.
	/// </remarks>
	public IReadOnlyList<byte?>? Fingering { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse the named chord definition.
	/// </summary>
	/// <param name="name">The name of a chord.</param>
	/// <param name="definition">The fretted positions for the chord.</param>
	/// <param name="fingering">Optional finger positions for the fretted positions in <paramref name="definition"/>.</param>
	/// <returns>A new instance if the inputs were parsed as a chord definition. Null otherwise.</returns>
	public static ChordDefinition? TryParse(string name, string definition, string? fingering = null)
	{
		ChordDefinition? result = null;

		if (Chord.TryParse(name, out Chord? chord))
		{
			List<byte?>? frets = TryGetPositions(definition);
			List<byte?>? fingers = TryGetPositions(fingering);
			result = TryCreate(chord, frets, fingers);
		}

		return result;
	}

	/// <summary>
	/// Gets a formatted chord definition.
	/// </summary>
	public override string ToString()
	{
		StringBuilder sb = new(this.Chord.Name);
		AppendPositions(sb, this.Definition);
		if (this.Fingering is not null && this.Fingering.Count > 0)
		{
			AppendPositions(sb, this.Fingering);
		}

		string result = sb.ToString();
		return result;

		static void AppendPositions(StringBuilder sb, IReadOnlyList<byte?> positions)
		{
			sb.Append(' ');

			const int DoubleDigitFret = 10;
			bool useSeparator = positions.Any(fret => fret is not null && fret >= DoubleDigitFret);
			foreach (byte? fret in positions)
			{
				sb.Append(fret is null ? "x" : fret.ToString());
				if (useSeparator)
				{
					sb.Append('-');
				}
			}

			if (useSeparator)
			{
				sb.Length--;
			}
		}
	}

	#endregion

	#region Internal Methods

	internal static bool IsUnplayedString(string part)
	{
		// https://www.chordpro.org/chordpro/directives-define/ says, "Use -1, N or x to denote a non-sounding string."
		StringComparer comparer = ChordParser.Comparer;
		bool result = comparer.Equals(part, "x") || comparer.Equals(part, "N") || part == "-1" || part == "_";
		return result;
	}

	internal static ChordDefinition? TryCreate(string name, IReadOnlyList<byte?>? frets, IReadOnlyList<byte?>? fingers)
		=> Chord.TryParse(name, out Chord? chord) ? TryCreate(chord, frets, fingers) : null;

	internal ChordDefinition ChangeChord(Func<Chord, Chord> change)
	{
		Chord chord = change(this.Chord);
		ChordDefinition result = ReferenceEquals(chord, this.Chord) ? this : new(chord, this.Definition, this.Fingering);
		return result;
	}

	#endregion

	#region Private Methods

	private static ChordDefinition? TryCreate(Chord chord, IReadOnlyList<byte?>? frets, IReadOnlyList<byte?>? fingers)
	{
		// I'm intentionally not validating the finger numbers. Should they go 1..4, 1..5, 1..10, etc.?
		const int MinStringCount = 4;
		ChordDefinition? result = frets != null
			&& frets.Count >= MinStringCount
			&& frets.Any(fret => fret is not null)
			&& (fingers is null || (fingers.Count == frets.Count && fingers.Any(f => f is not null)))
				? new(chord, frets, fingers)
				: null;
		return result;
	}

	private static List<byte?>? TryGetPositions(string? positions)
	{
		List<byte?>? frets = null;

		// For low frets the notes should all be concatenated (Am x02210) where x, N, or _ indicate an unplayed string.
		// UG suggests '-' as a separator for high frets: Cmaj7 x-x-10-12-12-12
		// https://www.ultimate-guitar.com/contribution/help/rubric#iii3 (section D. Fingering)
		// ChordPro "define" allows -1 for an unplayed string, but that would be ambiguous here with UG's '-' separator.
		IEnumerable<string> parts = positions is null ? []
			: positions.Contains('-') ? positions.Split('-')
			: positions.Select(ch => ch.ToString());
		foreach (string part in parts)
		{
			if (byte.TryParse(part, out byte fret))
			{
				frets ??= [];
				frets.Add(fret);
			}
			else if (IsUnplayedString(part))
			{
				frets ??= [];
				frets.Add(null);
			}
			else
			{
				frets = null;
				break;
			}
		}

		return frets;
	}

	#endregion
}
