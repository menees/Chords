#region Using Directives

using System.IO;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application;

/// <summary>Renders preloaded mono clicks using absolute sample deadlines, without per-click tasks or allocations.</summary>
public sealed class MetronomeRenderer
{
	#region Private Data

	private const int SourceSampleRate = 48000;
	private const float AccentGain = 1.3f;
	private const int SecondsPerMinute = 60;
	private const double AccentPitch = 1.25;
	private static readonly Dictionary<string, float[]> Samples = LoadSamples();
	private readonly int sampleRate;
	private readonly Dictionary<string, (float[] Normal, float[] Accent)> sounds = new(StringComparer.Ordinal);
	private double samplesPerBeat;
	private double originBeat;
	private long originSample;
	private int subdivision;
	private int beatsPerMeasure;
	private bool accentFirstBeat;
	private float volume;
	private string sound = string.Empty;
	private string pendingSound = string.Empty;
	private double soundChangeBeat;
	private long mutedClick = -1;
	#endregion

	#region Constructors

	public MetronomeRenderer(MetronomeSettings settings, int sampleRate)
	{
		BookApplicationSession.ValidateMetronome(settings);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
		this.sampleRate = sampleRate;
		foreach ((string name, float[] values) in Samples)
		{
			this.sounds.Add(name, (Resample(values, sampleRate, 1), Resample(values, (int)(sampleRate / AccentPitch), AccentGain)));
		}

		this.sounds.Add("KickHiHat", (this.sounds["HiHat"].Normal, this.sounds["Kick"].Normal));
		this.sound = settings.Sound;
		this.Update(settings, 0);
	}

	#endregion

	#region Public Methods

	/// <summary>Gets the continuous beat position, shared by audio and visual observations.</summary>
	public double GetBeatPosition(long samplePosition)
		=> this.originBeat + ((samplePosition - this.originSample) / this.samplesPerBeat);

	/// <summary>Aligns a newly opened audio device with an already running visual clock.</summary>
	public void SetBeatPosition(double beat, long samplePosition)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(beat);
		ArgumentOutOfRangeException.ThrowIfNegative(samplePosition);
		this.originBeat = beat;
		this.originSample = samplePosition;
	}

	/// <summary>Changes tempo without resetting beat phase. A sound change begins at the next whole beat.</summary>
	public void Update(MetronomeSettings settings, long samplePosition)
	{
		BookApplicationSession.ValidateMetronome(settings);
		ArgumentOutOfRangeException.ThrowIfNegative(samplePosition);
		double beat = this.samplesPerBeat > 0 ? this.GetBeatPosition(samplePosition) : 0;
		this.originBeat = beat;
		this.originSample = samplePosition;
		this.samplesPerBeat = this.sampleRate * (double)SecondsPerMinute / settings.BeatsPerMinute;
		this.subdivision = settings.Subdivision;
		this.beatsPerMeasure = settings.BeatsPerMeasure;
		this.accentFirstBeat = settings.AccentFirstBeat;
		this.volume = settings.AudioEnabled ? (float)settings.Volume : 0;
		if (settings.Sound != this.pendingSound)
		{
			this.pendingSound = settings.Sound;
			this.soundChangeBeat = Math.Floor(beat) + 1;
		}
	}

	/// <summary>Fills a buffer. After a clock discontinuity, the expired click is silent until the next deadline.</summary>
	public void Render(Span<float> destination, long samplePosition, bool discontinuity = false)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(samplePosition);
		long click = (long)Math.Floor(this.GetBeatPosition(samplePosition) * this.subdivision);
		if (discontinuity && samplePosition > this.GetClickSample(click))
		{
			this.mutedClick = click;
		}

		int written = 0;
		while (written < destination.Length)
		{
			long start = this.GetClickSample(click);
			long end = this.GetClickSample(click + 1);
			int count = (int)Math.Min(destination.Length - written, end - samplePosition);
			if ((double)click / this.subdivision >= this.soundChangeBeat)
			{
				this.sound = this.pendingSound;
			}

			(float[] normal, float[] accented) = this.sounds[this.sound];
			ReadOnlySpan<float> source = this.accentFirstBeat && click % (this.beatsPerMeasure * this.subdivision) == 0 ? accented : normal;
			Span<float> target = destination.Slice(written, count);
			target.Clear();
			long offset = samplePosition - start;
			if (click != this.mutedClick && offset >= 0 && offset < source.Length && this.volume > 0)
			{
				int available = Math.Min(count, source.Length - (int)offset);
				for (int index = 0; index < available; index++)
				{
					target[index] = source[(int)offset + index] * this.volume;
				}
			}

			written += count;
			samplePosition += count;
			click++;
		}
	}

	#endregion

	#region Private Methods

	private static Dictionary<string, float[]> LoadSamples()
	{
		Dictionary<string, float[]> result = new(StringComparer.Ordinal);
		foreach (string name in new[] { "Kick", "HiHat", "Woodblock", "Cowbell", "Click" })
		{
			using Stream stream = typeof(MetronomeRenderer).Assembly.GetManifestResourceStream(
				$"Menees.Chords.Book.Application.Sounds.{name}.pcm") ?? throw new InvalidOperationException($"Missing metronome sample: {name}.");
			using BinaryReader reader = new(stream);
			float[] values = new float[stream.Length / sizeof(float)];
			for (int index = 0; index < values.Length; index++)
			{
				values[index] = reader.ReadSingle();
			}

			result.Add(name, values);
		}

		return result;
	}

	private static float[] Resample(float[] source, int rate, float gain)
	{
		float[] result = new float[(int)Math.Ceiling(source.Length * (double)rate / SourceSampleRate)];
		for (int index = 0; index < result.Length; index++)
		{
			double position = index * (double)SourceSampleRate / rate;
			int left = (int)position;
			float first = source[left];
			float second = left + 1 < source.Length ? source[left + 1] : 0;
			result[index] = gain * (first + ((second - first) * (float)(position - left)));
		}

		return result;
	}

	private long GetClickSample(long click)
		=> (long)Math.Ceiling(this.originSample + ((((double)click / this.subdivision) - this.originBeat) * this.samplesPerBeat));

	#endregion
}
