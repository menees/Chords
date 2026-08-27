namespace Menees.Chords.Db;

/// <summary>Represents an instrument configuration profile.</summary>
public sealed class InstrumentProfile
{
	/// <summary>Gets or sets the profile identifier.</summary>
	public Guid Id { get; set; }

	/// <summary>Gets or sets the profile name.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Gets or sets the revision stamp.</summary>
	public RevisionStamp Revision { get; set; } = new();
}
