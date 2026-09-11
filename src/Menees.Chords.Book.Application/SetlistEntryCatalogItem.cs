namespace Menees.Chords.Book.Application;

/// <summary>Describes one occurrence of a song in an ordered setlist.</summary>
public sealed record SetlistEntryCatalogItem(
	Guid EntryId,
	Guid SongId,
	string DisplayText);
