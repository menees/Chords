namespace Menees.Chords.Sync;

/// <summary>Provider-specific prior entity fingerprints and content hashes, never a copy of song assets.</summary>
public sealed class BookMergeBase
{
	public Guid BookId { get; set; }

	public Dictionary<string, string> Entities { get; set; } = new(StringComparer.Ordinal);

	public Dictionary<Guid, string> FileHashes { get; set; } = [];
}
