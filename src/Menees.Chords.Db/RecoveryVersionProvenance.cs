namespace Menees.Chords.Db;

/// <summary>Describes a conflict-recovery file version.</summary>
public sealed class RecoveryVersionProvenance
{
	/// <summary>Gets or sets when the version was recovered.</summary>
	public DateTimeOffset RecoveredFromSyncUtc { get; set; }

	/// <summary>Gets or sets the original conflicting file identifier.</summary>
	public Guid OriginalConflictingFileId { get; set; }

	/// <summary>Gets or sets the winning file identifier.</summary>
	public Guid WinningFileId { get; set; }
}
