namespace Menees.Chords.Book.Application;

/// <summary>An editor snapshot; a null text buffer indicates metadata-only editing.</summary>
public sealed record SongEditDocument(
	Guid SongId,
	long Revision,
	string Title,
	IReadOnlyList<string> Artists,
	IReadOnlyList<string> Tags,
	Guid? FileId,
	string? ContentHash,
	string? Text)
{
	/// <summary>Gets detached effective scalar metadata for authoring and explicit directive import.</summary>
	public IReadOnlyDictionary<string, IReadOnlyList<string>> Metadata { get; init; }
		= new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
}
