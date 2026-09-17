namespace Menees.Chords.Db;

/// <summary>Versioned .mcbsettings payload. Deliberately excludes identity, history, paths and content.</summary>
public sealed class PortableBookSettings
{
	/// <summary>Gets the independent settings format version.</summary>
	public required int FormatVersion { get; init; }

	/// <summary>Gets portable display defaults.</summary>
	public required DisplayProfile Display { get; init; }

	/// <summary>Gets portable metronome defaults.</summary>
	public required MetronomeSettings Metronome { get; init; }

	/// <summary>Gets whether playback stops between setlist entries.</summary>
	public required bool StopMetronomeOnSetlistTransition { get; init; }

	/// <summary>Gets portable keyboard and HID bindings.</summary>
	public required List<PerformanceKeyBinding> InputBindings { get; init; }

	/// <summary>Gets the title template.</summary>
	public required string TitleTemplate { get; init; }

	/// <summary>Gets the subtitle template.</summary>
	public required string SubtitleTemplate { get; init; }
}
