namespace Menees.Chords.Db;

/// <summary>Describes a source file without changing its bytes.</summary>
public sealed record SongFileAnalysis
{
	/// <summary>Gets the detected media kind.</summary>
	public required MediaKind MediaKind { get; init; }

	/// <summary>Gets the detected song-text format.</summary>
	public required SourceFormat SourceFormat { get; init; }

	/// <summary>Gets the detected text encoding name, or null for binary media.</summary>
	public string? TextEncoding { get; init; }

	/// <summary>Gets the detected byte-order mark.</summary>
	public ByteOrderMarkKind ByteOrderMark { get; init; }

	/// <summary>Gets the best available title.</summary>
	public required string Title { get; init; }

	/// <summary>Gets the parsed artist names.</summary>
	public IReadOnlyList<string> Artists { get; init; } = [];

	/// <summary>Gets metadata exactly as extracted from the source.</summary>
	public IReadOnlyDictionary<string, IReadOnlyList<SourceMetadataValue>> Metadata { get; init; }

		= new SortedDictionary<string, IReadOnlyList<SourceMetadataValue>>(StringComparer.Ordinal);
}
