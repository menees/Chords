using System.Text.Json.Serialization;

namespace Menees.Chords.Db;

/// <summary>Represents a managed file associated with a song.</summary>
public sealed class SongFile
{
	/// <summary>Gets or sets the file identifier.</summary>
	public Guid Id { get; set; }

	/// <summary>Gets or sets the owning song identifier.</summary>
	public Guid SongId { get; set; }

	/// <summary>Gets or sets the path relative to the book root.</summary>
	public string RelativePath { get; set; } = string.Empty;

	/// <summary>Gets or sets the media kind.</summary>
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public MediaKind MediaKind { get; set; }

	/// <summary>Gets or sets the source format.</summary>
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public SourceFormat SourceFormat { get; set; }

	/// <summary>Gets or sets the text encoding name.</summary>
	public string? TextEncoding { get; set; }

	/// <summary>Gets or sets the byte-order mark kind.</summary>
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public ByteOrderMarkKind ByteOrderMark { get; set; }

	/// <summary>Gets or sets the display priority.</summary>
	public int DisplayPriority { get; set; }

	/// <summary>Gets or sets whether the file is archived.</summary>
	public bool IsArchived { get; set; }

	/// <summary>Gets or sets the content hash.</summary>
	public string ContentHash { get; set; } = string.Empty;

	/// <summary>Gets or sets the last observed file length.</summary>
	public long? ObservedLength { get; set; }

	/// <summary>Gets or sets the last observed write time.</summary>
	public DateTimeOffset? ObservedWriteUtc { get; set; }

	/// <summary>Gets or sets the content revision.</summary>
	public long ContentRevision { get; set; }

	/// <summary>Gets or sets the version of <see cref="SongFileAnalyzer"/> used to populate catalog metadata.</summary>
	public int AnalysisVersion { get; set; }

	/// <summary>Gets or sets recovery-version provenance.</summary>
	public RecoveryVersionProvenance? RecoveryVersion { get; set; }

	/// <summary>Gets or sets the revision stamp.</summary>
	public RevisionStamp Revision { get; set; } = new();
}
