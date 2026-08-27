namespace Menees.Chords.Db;

/// <summary>Represents book-wide settings.</summary>
public sealed class BookSettings
{
	/// <summary>Gets or sets the default display profile.</summary>
	public DisplayProfile DefaultDisplayProfile { get; set; } = new();

	/// <summary>Gets or sets the default metronome settings.</summary>
	public MetronomeSettings DefaultMetronome { get; set; } = new();

	/// <summary>Gets or sets the song-title template.</summary>
	public string TitleTemplate { get; set; } = "{title}";

	/// <summary>Gets or sets the song-subtitle template.</summary>
	public string SubtitleTemplate { get; set; } = "{artist}";

	/// <summary>Gets or sets the revision stamp.</summary>
	public RevisionStamp Revision { get; set; } = new();
}
