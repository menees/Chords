namespace Menees.Chords.Db;

/// <summary>Represents a song and its catalog metadata.</summary>
public sealed class Song
{
	/// <summary>Gets or sets the song identifier.</summary>
	public Guid Id { get; set; }

	/// <summary>Gets or sets the title.</summary>
	public string Title { get; set; } = string.Empty;

	/// <summary>Gets or sets the artists.</summary>
	public List<string> Artists { get; set; } = [];

	/// <summary>Gets or sets metadata extracted from source files.</summary>
	public SortedDictionary<string, List<SourceMetadataValue>> SourceMetadata { get; set; } = new(StringComparer.Ordinal);

	/// <summary>Gets or sets the duration in seconds.</summary>
	public int? DurationSeconds { get; set; }

	/// <summary>Gets or sets the tags.</summary>
	public List<string> Tags { get; set; } = [];

	/// <summary>Gets or sets the last access time.</summary>
	public DateTimeOffset? LastAccessedUtc { get; set; }

	/// <summary>Gets or sets whether the song is archived.</summary>
	public bool IsArchived { get; set; }

	/// <summary>Gets or sets the display overrides.</summary>
	public DisplayOverride? DisplayOverride { get; set; }

	/// <summary>Gets or sets the metronome overrides.</summary>
	public SongMetronomeOverride? MetronomeOverride { get; set; }

	/// <summary>Gets or sets the revision stamp.</summary>
	public RevisionStamp Revision { get; set; } = new();
}
