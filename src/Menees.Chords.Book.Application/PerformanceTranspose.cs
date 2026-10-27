using Menees.Chords.Db;
using Menees.Chords.Transformers;

namespace Menees.Chords.Book.Application;

/// <summary>Resolves sounding-key transposition separately from capo chord shapes.</summary>
internal static class PerformanceTranspose
{
	/// <summary>Returns the source-to-displayed chord offset, applying the selected capo direction.</summary>
	public static int GetShownSemitones(int soundingTranspose, int? capoFret, CapoBehavior behavior)
	{
		const int MaximumTranspose = 24;
		const int MaximumCapo = 24;
		ArgumentOutOfRangeException.ThrowIfLessThan(soundingTranspose, -MaximumTranspose);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(soundingTranspose, MaximumTranspose);
		if (capoFret is < 0 or > MaximumCapo)
		{
			throw new ArgumentOutOfRangeException(nameof(capoFret));
		}

		int capo = capoFret ?? 0;
		return soundingTranspose + behavior switch
		{
			CapoBehavior.DisplayOnly => 0,
			CapoBehavior.AffectsShownChords => -capo,
			CapoBehavior.RaiseShownChords => capo,
			_ => throw new ArgumentOutOfRangeException(nameof(behavior)),
		};
	}

	public static AccidentalPreference ResolveSpelling(Key key, AccidentalPreference preference)
		=> preference != AccidentalPreference.Default ? preference
			: key.Root.Length > 1 && key.Root[1] == 'b' ? AccidentalPreference.Flats
			: key.Root.Contains('#') ? AccidentalPreference.Sharps : AccidentalPreference.Default;

	public static Document Transform(Document document, Key key, int chordTranspose, int keyTranspose, AccidentalPreference spelling)
		=> chordTranspose == 0 && keyTranspose == 0 && spelling == AccidentalPreference.Default ? document
			: new TransposeTransformer(document, checked((sbyte)chordTranspose), key, spelling)
			{
				KeyHalfSteps = checked((sbyte)keyTranspose),
			}.Transform().Document;
}
