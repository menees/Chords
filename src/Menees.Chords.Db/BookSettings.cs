namespace Menees.Chords.Db;

/// <summary>Represents book-wide settings.</summary>
public sealed class BookSettings
{
	/// <summary>Gets or sets the default display profile.</summary>
	public DisplayProfile DefaultDisplayProfile { get; set; } = new();

	/// <summary>Gets or sets the default metronome settings.</summary>
	public MetronomeSettings DefaultMetronome { get; set; } = new();

	/// <summary>Gets or sets whether clicks stop when moving to another setlist entry.</summary>
	public bool StopMetronomeOnSetlistTransition { get; set; } = true;

	/// <summary>Gets or sets portable keyboard and HID pedal command bindings.</summary>
	public List<PerformanceKeyBinding> InputBindings { get; set; } = PerformanceKeyBinding.CreateDefaults();

	/// <summary>Gets or sets the song-title template.</summary>
	public string TitleTemplate { get; set; } = "{title}";

	/// <summary>Gets or sets the song-subtitle template.</summary>
	public string SubtitleTemplate { get; set; } = "{artists}";

	/// <summary>Gets or sets the revision stamp.</summary>
	public RevisionStamp Revision { get; set; } = new();
}
