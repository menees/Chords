namespace Menees.Chords.Db;

/// <summary>Represents complete metronome settings.</summary>
public sealed class MetronomeSettings
{
	/// <summary>Gets or sets the tempo in beats per minute.</summary>
	public int BeatsPerMinute { get; set; } = 120;

	/// <summary>Gets or sets the beats per measure.</summary>
	public int BeatsPerMeasure { get; set; } = 4;

	/// <summary>Gets or sets the note value receiving one beat.</summary>
	public int BeatUnit { get; set; } = 4;

	/// <summary>Gets or sets the beat subdivision.</summary>
	public int Subdivision { get; set; } = 1;

	/// <summary>Gets or sets the sound name.</summary>
	public string Sound { get; set; } = "Click";

	/// <summary>Gets or sets the volume.</summary>
	public double Volume { get; set; } = 0.8;

	/// <summary>Gets or sets whether the first beat is accented.</summary>
	public bool AccentFirstBeat { get; set; } = true;

	/// <summary>Gets or sets whether audio is enabled.</summary>
	public bool AudioEnabled { get; set; } = true;

	/// <summary>Gets or sets whether visual feedback is enabled.</summary>
	public bool VisualEnabled { get; set; } = true;
}
