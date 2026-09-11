namespace Menees.Chords.Book.Application;

/// <summary>Sample-clock click synthesis shared by platform audio adapters.</summary>
public static class MetronomePulse
{
	/// <summary>Produces a short decaying click at each beat, with an optional measure accent.</summary>
	public static float Sample(long sample, int sampleRate, int beatsPerMinute, int beatsPerMeasure, double volume, bool accentFirstBeat)
	{
		const double SecondsPerMinute = 60;
		const double ClickDuration = 0.025;
		const double AccentGain = 0.65;
		const double NormalGain = 0.45;
		const double AccentFrequency = 1600;
		const double NormalFrequency = 1000;
		const double DecayRate = 180;
		double samplesPerBeat = sampleRate * SecondsPerMinute / beatsPerMinute;
		long beat = (long)Math.Floor(sample / samplesPerBeat);
		double time = (sample - Math.Ceiling(beat * samplesPerBeat)) / sampleRate;
		bool accent = accentFirstBeat && beat % beatsPerMeasure == 0;
		return time >= 0 && time < ClickDuration
			? (float)(volume * (accent ? AccentGain : NormalGain)
				* Math.Sin(2 * Math.PI * (accent ? AccentFrequency : NormalFrequency) * time) * Math.Exp(-time * DecayRate))
			: 0;
	}
}
