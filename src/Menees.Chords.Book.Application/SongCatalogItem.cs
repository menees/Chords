namespace Menees.Chords.Book.Application;

/// <summary>Describes a song row without exposing mutable database entities to a client.</summary>
public sealed record SongCatalogItem(
	Guid Id,
	string Title,
	IReadOnlyList<string> Artists,
	string DisplayText,
	bool IsArchived,
	int ActiveFileCount,
	DateTimeOffset? LastAccessedUtc);
