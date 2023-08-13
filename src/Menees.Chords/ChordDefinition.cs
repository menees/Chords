namespace Menees.Chords;

#region Using Directives

using System.Text;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// Combines a <see cref="Chord"/> with its fretted positions definition.
/// </summary>
public sealed class ChordDefinition
{
	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="chord">The named chord being defined.</param>
	/// <param name="definition">The fretted positions for the chord.</param>
	private ChordDefinition(Chord chord, IReadOnlyList<byte?> definition)
	{
		this.Chord = chord;
		this.Definition = definition;
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

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse the named chord definition.
	/// </summary>
	/// <param name="name">The name of a chord.</param>
	/// <param name="definition">The fretted positions for the chord.</param>
	/// <returns>A new instance if the inputs were parsed as a chord definition. Null otherwise.</returns>
	public static ChordDefinition? TryParse(string name, string definition)
	{
		ChordDefinition? result = null;

		if (Chord.TryParse(name, out Chord? chord))
		{
			List<byte?>? frets = null;

			// For low frets the notes should all be concatenated (Am x02210) where 'x' or '_' indicate an unplayed string.
			// UG suggests '-' as a separator for high frets: Cmaj7 x-x-10-12-12-12
			// https://www.ultimate-guitar.com/contribution/help/rubric#iii3 (section D. Fingering)
			IEnumerable<string> parts = definition.Contains('-') ? definition.Split('-') : definition.Select(ch => ch.ToString());
			foreach (string part in parts)
			{
				if (byte.TryParse(part, out byte fret))
				{
					frets ??= new();
					frets.Add(fret);
				}
				else if (ChordParser.Comparer.Equals(part, "x") || part == "_")
				{
					frets ??= new();
					frets.Add(null);
				}
				else
				{
					frets = null;
					break;
				}
			}

			const int MinStringCount = 4;
			if (frets != null && frets.Count >= MinStringCount && !frets.All(fret => fret is null))
			{
				result = new(chord, frets);
			}
		}

		return result;
	}

	/// <summary>
	/// Gets a formatted chord definition.
	/// </summary>
	public override string ToString()
	{
		StringBuilder sb = new(this.Chord.Name);
		sb.Append(' ');

		const int DoubleDigitFret = 10;
		bool useSeparator = this.Definition.Any(fret => fret is not null && fret >= DoubleDigitFret);
		foreach (byte? fret in this.Definition)
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

		string result = sb.ToString();
		return result;
	}

	#endregion
}
