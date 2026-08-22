namespace Menees.Chords;

internal static class MusicTheory
{
	#region Private Data Members

	private const int PitchCount = 12;
	private const int TritonePitch = 6;
	private const int FourthDegreeIndex = 3;
	private const int EPitch = 4;
	private const int FPitch = 5;
	private const int GPitch = 7;
	private const int APitch = 9;
	private const int BPitch = 11;

	private static readonly string[] SharpNotes = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
	private static readonly string[] FlatNotes = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];
	private static readonly int[] DegreeOffsets = [0, 2, 4, 5, 7, 9, 11];
	private static readonly string[] RomanDegrees = ["I", "II", "III", "IV", "V", "VI", "VII"];

	#endregion

	#region Internal Methods

	internal static string ChangeNoteNotation(string note, Notation source, Notation target, Key key)
	{
		int pitch = source switch
		{
			Notation.Name => GetNamedPitch(note),
			Notation.Nashville => GetRelativePitch(note),
			Notation.Roman => GetRelativePitch(note),
			_ => throw new ArgumentOutOfRangeException(nameof(source)),
		};

		string result;
		if (target == Notation.Name)
		{
			int absolutePitch = source == Notation.Name ? pitch : Mod(GetNamedPitch(key.Root) + pitch);
			bool useSharps = PrefersSharps(key.Root, note);
			result = (useSharps ? SharpNotes : FlatNotes)[absolutePitch];
		}
		else
		{
			int relativePitch = source == Notation.Name ? Mod(pitch - GetNamedPitch(key.Root)) : pitch;
			(int degree, int accidental) = GetDegree(relativePitch);
			string prefix = accidental switch
			{
				-1 => "b",
				1 => "#",
				_ => string.Empty,
			};
			result = prefix + (target == Notation.Nashville ? (degree + 1).ToString() : RomanDegrees[degree]);
		}

		return result;
	}

	internal static string TransposeNamedNote(string note, sbyte halfSteps, AccidentalPreference accidentalPreference)
	{
		bool useSharps = accidentalPreference == AccidentalPreference.Sharps
			|| (accidentalPreference == AccidentalPreference.Default && halfSteps > 0);
		return MatchCase(note, (useSharps ? SharpNotes : FlatNotes)[Mod(GetNamedPitch(note) + halfSteps)]);
	}

	internal static bool IsMinor(IReadOnlyList<string> modifiers)
		=> modifiers.Count > 0 && (modifiers[0].Equals("m", StringComparison.Ordinal)
			|| modifiers[0].Equals("min", StringComparison.OrdinalIgnoreCase)
			|| modifiers[0].Equals("-", StringComparison.Ordinal));

	internal static sbyte NormalizeTranspose(sbyte halfSteps)
		=> (sbyte)(halfSteps % PitchCount);

	#endregion

	#region Private Methods

	private static (int Degree, int Accidental) GetDegree(int pitch)
	{
		(int Degree, int Accidental) result = default;
		bool found = false;
		for (int index = 0; index < DegreeOffsets.Length; index++)
		{
			if (DegreeOffsets[index] == pitch)
			{
				result = (index, 0);
				found = true;
				break;
			}
		}

		if (!found)
		{
			// Prefer the conventional flat degrees, except for the raised fourth.
			result = pitch == TritonePitch
				? (FourthDegreeIndex, 1)
				: (Array.IndexOf(DegreeOffsets, Mod(pitch + 1)), -1);
		}

		return result;
	}

	private static int GetNamedPitch(string note)
	{
		int natural = char.ToUpperInvariant(note[0]) switch
		{
			'C' => 0,
			'D' => 2,
			'E' => EPitch,
			'F' => FPitch,
			'G' => GPitch,
			'A' => APitch,
			'B' or 'H' => BPitch,
			_ => throw new ArgumentException("The note is invalid.", nameof(note)),
		};
		int accidental = note.Length > 1 ? note[1] == '#' ? 1 : -1 : 0;
		return Mod(natural + accidental);
	}

	private static int GetRelativePitch(string note)
	{
		int index = 0;
		int accidental = 0;
		if (note[0] is '#' or 'b')
		{
			accidental = note[0] == '#' ? 1 : -1;
			index++;
		}

		int degree = char.IsDigit(note[index])
			? note[index] - '1'
			: Array.FindIndex(RomanDegrees, value => value.Equals(note[index..], StringComparison.OrdinalIgnoreCase));
		return Mod(DegreeOffsets[degree] + accidental);
	}

	private static string MatchCase(string source, string result)
		=> char.IsLower(source[0]) ? result.ToLowerInvariant() : result;

	private static int Mod(int value) => ((value % PitchCount) + PitchCount) % PitchCount;

	private static bool PrefersSharps(string keyRoot, string sourceNote)
	{
		bool result;
		if (sourceNote.Contains('#'))
		{
			result = true;
		}
		else if (sourceNote.Contains('b'))
		{
			result = false;
		}
		else
		{
			result = !keyRoot.Contains('b') && !keyRoot.Equals("F", StringComparison.OrdinalIgnoreCase);
		}

		return result;
	}

	#endregion
}
