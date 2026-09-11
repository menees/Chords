namespace Menees.Chords.Book.Cli;

internal sealed record MobileSheetsSetlistImportResult(
	int SetlistCount,
	int EntryCount,
	int ImportedSongCount,
	int AddedSetlistCount,
	int ExistingSetlistCount,
	bool Applied);
