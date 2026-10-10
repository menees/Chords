namespace Menees.Chords.Db;

/// <summary>Identifies the previous authored bytes retained by an explicit text save.</summary>
public sealed class SavedVersionProvenance
{
	/// <summary>Gets or sets the edited sheet whose prior bytes were retained.</summary>
	public Guid SourceFileId { get; set; }

	/// <summary>Gets or sets when the save retained this copy.</summary>
	public DateTimeOffset SavedUtc { get; set; }

	/// <summary>Gets or sets the original sheet's edit time before saving.</summary>
	public DateTimeOffset OriginalModifiedUtc { get; set; }
}
