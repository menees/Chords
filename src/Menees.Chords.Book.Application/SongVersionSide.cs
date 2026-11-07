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
	DateTimeOffset? SavedUtc = null,
	bool UsedBySetlists = false,
	bool UsedByInstruments = false)
{
	/// <summary>Gets whether this side is a selected import source rather than a sheet already in the book.</summary>
	public bool IsIncoming { get; init; }

	/// <summary>Gets the sheet name without its internal identity suffix.</summary>
	public string DisplayName => this.Name.Replace($" [{this.Id:D}]", string.Empty, StringComparison.OrdinalIgnoreCase);
}
