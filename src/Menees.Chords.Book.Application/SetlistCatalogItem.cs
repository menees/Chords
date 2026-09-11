namespace Menees.Chords.Book.Application;

/// <summary>Describes an ordered setlist without exposing its mutable database entity.</summary>
public sealed record SetlistCatalogItem(
	Guid Id,
	string Name,
	DateOnly? Date,
	string? Notes,
	bool IsArchived,
	int EntryCount,
	int KnownDurationCount,
	int TotalDurationSeconds);
