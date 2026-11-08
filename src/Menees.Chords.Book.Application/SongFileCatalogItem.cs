using Menees.Chords.Db;

namespace Menees.Chords.Book.Application;

/// <summary>A metadata-only snapshot of a song's sheet for management UI.</summary>
public sealed record SongFileCatalogItem(
	Guid Id,
	string Name,
	MediaKind MediaKind,
	SourceFormat SourceFormat,
	bool IsArchived,
	bool IsRecoveryVersion,
	bool IsDefault);
