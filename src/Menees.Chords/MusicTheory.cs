namespace Menees.Chords;

internal static class MusicTheory
{
	#region Private Data Members

	// GetDegree treats a raised fourth differently from other chromatic intervals.
	private const int FourthDegreeIndex = 3;

	// DegreeOffsets describes the seven unaltered major-scale degrees as semitone offsets from the key.
	private static readonly int[] DegreeOffsets = [Pitch.C, Pitch.D, Pitch.E, Pitch.F, Pitch.G, Pitch.A, Pitch.B];
	private static readonly string[] SharpNotes = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
	private static readonly string[] FlatNotes = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];
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

	internal static int GetNamedPitch(string note)
	{
		int natural = char.ToUpperInvariant(note[0]) switch
		{
			'C' => Pitch.C,
			'D' => Pitch.D,
			'E' => Pitch.E,
			'F' => Pitch.F,
			'G' => Pitch.G,
			'A' => Pitch.A,
			'B' or 'H' => Pitch.B,
			_ => throw new ArgumentException("The note is invalid.", nameof(note)),
		};
		int accidental = note.Length > 1 ? note[1] == '#' ? 1 : -1 : 0;
		return Mod(natural + accidental);
	}

	internal static string GetNamedNote(int pitch, string keyRoot)
		=> (PrefersSharps(keyRoot, keyRoot) ? SharpNotes : FlatNotes)[Mod(pitch)];

	internal static sbyte NormalizeTranspose(sbyte halfSteps)
		=> (sbyte)(halfSteps % Pitch.Count);

	internal static int NormalizePitch(int pitch) => Mod(pitch);

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
			// When enharmonic intent is unavailable, use flat spellings for chromatic degrees,
			// except canonicalize the tritone as #4 like most music theorists. Typically, only
			// blues and jazz players prefer b5.
			result = pitch == Pitch.Tritone
				? (FourthDegreeIndex, 1)
				: (Array.IndexOf(DegreeOffsets, Mod(pitch + 1)), -1);
		}

		return result;
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

	private static int Mod(int value) => ((value % Pitch.Count) + Pitch.Count) % Pitch.Count;

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

	#region Private Types

	private static class Pitch
	{
		public const int Count = 12;
		public const int Tritone = 6;
		public const int C = 0;
		public const int D = 2;
		public const int E = 4;
		public const int F = 5;
		public const int G = 7;
		public const int A = 9;
		public const int B = 11;
	}

	#endregion
}
