namespace Menees.Chords.Book.Application;

/// <summary>Resolves sounding-key transposition separately from capo chord shapes.</summary>
internal static class PerformanceTranspose
{
	/// <summary>Returns the source-to-displayed chord offset, subtracting capo only for chord-shape mode.</summary>
	public static int GetShownSemitones(int soundingTranspose, int? capoFret, bool showCapoShapes)
	{
		const int MaximumTranspose = 24;
		const int MaximumCapo = 24;
		ArgumentOutOfRangeException.ThrowIfLessThan(soundingTranspose, -MaximumTranspose);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(soundingTranspose, MaximumTranspose);
		if (capoFret is < 0 or > MaximumCapo)
		{
			throw new ArgumentOutOfRangeException(nameof(capoFret));
		}

		return soundingTranspose - (showCapoShapes ? capoFret ?? 0 : 0);
	}
}
