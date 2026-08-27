namespace Menees.Chords.Db;

/// <summary>Represents optional song-specific metronome overrides.</summary>
public sealed class SongMetronomeOverride
{
	/// <summary>Gets or sets the tempo override.</summary>
	public int? BeatsPerMinute { get; set; }

	/// <summary>Gets or sets the beats-per-measure override.</summary>
	public int? BeatsPerMeasure { get; set; }

	/// <summary>Gets or sets the beat-unit override.</summary>
	public int? BeatUnit { get; set; }

	/// <summary>Gets or sets the subdivision override.</summary>
	public int? Subdivision { get; set; }

	/// <summary>Gets or sets the sound override.</summary>
	public string? Sound { get; set; }

	/// <summary>Gets or sets the volume override.</summary>
	public double? Volume { get; set; }

	/// <summary>Gets or sets the first-beat accent override.</summary>
	public bool? AccentFirstBeat { get; set; }

	/// <summary>Gets or sets the audio-enabled override.</summary>
	public bool? AudioEnabled { get; set; }

	/// <summary>Gets or sets the visual-enabled override.</summary>
	public bool? VisualEnabled { get; set; }
}
