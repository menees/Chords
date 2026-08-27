namespace Menees.Chords.Db;

/// <summary>Describes one native chord-book validation failure.</summary>
/// <param name="Kind">The failure category.</param>
/// <param name="Message">A human-readable explanation.</param>
/// <param name="SongFileId">The affected song-file identity, when applicable.</param>
/// <param name="RelativePath">The affected managed path, when applicable.</param>
public sealed record BookValidationIssue(
	BookValidationIssueKind Kind,
	string Message,
	Guid? SongFileId = null,
	string? RelativePath = null);
