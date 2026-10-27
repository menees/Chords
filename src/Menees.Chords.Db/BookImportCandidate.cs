namespace Menees.Chords.Db;

/// <summary>A detached catalog or earlier incoming sheet that may be related.</summary>
/// <param name="SongId">The existing or planned song identity.</param>
/// <param name="SheetId">The existing sheet identity, or the incoming review item identity.</param>
/// <param name="Title">The catalog title.</param>
/// <param name="Artists">The catalog artists.</param>
/// <param name="SheetName">The sheet name.</param>
/// <param name="IsArchived">Whether the target song is archived.</param>
/// <param name="IsIncoming">Whether this candidate is an earlier selected source.</param>
/// <param name="ContentHash">The catalog or captured source hash.</param>
public sealed record BookImportCandidate(
	Guid SongId, Guid SheetId, string Title, IReadOnlyList<string> Artists, string SheetName,
	bool IsArchived, bool IsIncoming, string ContentHash)
{
	/// <summary>Gets whether a source exists for comparison; a catalog song can have no remaining sheets.</summary>
	public bool CanCompare => this.IsIncoming || this.SheetId != Guid.Empty;
}