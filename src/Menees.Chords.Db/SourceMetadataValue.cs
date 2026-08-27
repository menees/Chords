namespace Menees.Chords.Db;

/// <summary>Represents a metadata value extracted from a source.</summary>
public sealed class SourceMetadataValue
{
	/// <summary>Gets or sets the metadata value.</summary>
	public string Value { get; set; } = string.Empty;

	/// <summary>Gets or sets the source name.</summary>
	public string? SourceName { get; set; }
}
