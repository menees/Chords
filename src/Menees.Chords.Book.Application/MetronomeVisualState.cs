namespace Menees.Chords.Book.Application;

/// <summary>A replaceable observation of the beat clock; never an audio scheduling event.</summary>
public readonly record struct MetronomeVisualState(int Beat, bool IsAccent)
{
	/// <summary>Returns the current short pulse, or no pulse between clicks and while warming.</summary>
	public static MetronomeVisualState? At(double seconds, int beatsPerMinute, int beatsPerMeasure, int subdivision, bool accentFirstBeat)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(beatsPerMinute);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(beatsPerMeasure);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(subdivision);
		const double SecondsPerMinute = 60;
		const double MaximumPulseSeconds = 0.1;
		MetronomeVisualState? result = null;
		if (seconds >= 0 && double.IsFinite(seconds))
		{
			double clicks = seconds * beatsPerMinute * subdivision / SecondsPerMinute;
			long click = (long)Math.Floor(clicks);
			double interval = SecondsPerMinute / (beatsPerMinute * subdivision);
			if ((clicks - click) * interval < Math.Min(MaximumPulseSeconds, interval / 2))
			{
				int beat = (int)((click / subdivision) % beatsPerMeasure) + 1;
				result = new(beat, accentFirstBeat && beat == 1 && click % subdivision == 0);
			}
		}

		return result;
	}
}
