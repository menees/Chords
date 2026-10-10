using Menees.Chords.Db;

namespace Menees.Chords.Book.Application;

/// <summary>A read-only captured sheet identity, labels and optional decoded text.</summary>
public sealed record SongVersionSide(
	Guid Id,
	string Name,
	MediaKind MediaKind,
	DateTimeOffset ModifiedUtc,
	bool IsArchived,
	DateTimeOffset? RecoveredUtc,
	string ContentHash,
	string? Text,
	DateTimeOffset? SavedUtc = null);
