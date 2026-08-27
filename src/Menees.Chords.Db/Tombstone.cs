namespace Menees.Chords.Db;

/// <summary>Represents a synchronized entity deletion.</summary>
public sealed class Tombstone
{
	/// <summary>Gets or sets the deleted entity identifier.</summary>
	public Guid EntityId { get; set; }

	/// <summary>Gets or sets the deleted entity type.</summary>
	public string EntityType { get; set; } = string.Empty;

	/// <summary>Gets or sets the deletion revision stamp.</summary>
	public RevisionStamp Revision { get; set; } = new();
}
