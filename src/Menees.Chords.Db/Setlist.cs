namespace Menees.Chords.Db;

/// <summary>Represents an ordered setlist.</summary>
public sealed class Setlist
{
	/// <summary>Gets or sets the setlist identifier.</summary>
	public Guid Id { get; set; }

	/// <summary>Gets or sets the setlist name.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Gets or sets the notes.</summary>
	public string? Notes { get; set; }

	/// <summary>Gets or sets the performance date.</summary>
	public DateOnly? Date { get; set; }

	/// <summary>Gets or sets whether the setlist is archived.</summary>
	public bool IsArchived { get; set; }

	/// <summary>Gets or sets the ordered entries.</summary>
	public List<SetlistEntry> Entries { get; set; } = [];

	/// <summary>Gets or sets the revision stamp.</summary>
	public RevisionStamp Revision { get; set; } = new();
}
