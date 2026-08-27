namespace Menees.Chords.Db;

/// <summary>Represents a portable chord-book database.</summary>
public sealed class ChordDatabase
{
	/// <summary>Gets the current database schema version.</summary>
	public const int CurrentSchemaVersion = 1;

	/// <summary>Gets or sets the database identifier.</summary>
	public Guid Id { get; set; }

	/// <summary>Gets or sets the database name.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Gets or sets the schema version.</summary>
	public int SchemaVersion { get; set; } = CurrentSchemaVersion;

	/// <summary>Gets or sets the book-wide settings.</summary>
	public BookSettings BookSettings { get; set; } = new();

	/// <summary>Gets or sets the songs.</summary>
	public List<Song> Songs { get; set; } = [];

	/// <summary>Gets or sets the song files.</summary>
	public List<SongFile> SongFiles { get; set; } = [];

	/// <summary>Gets or sets the instrument profiles.</summary>
	public List<InstrumentProfile> InstrumentProfiles { get; set; } = [];

	/// <summary>Gets or sets the per-song instrument settings.</summary>
	public List<SongInstrumentSetting> SongInstrumentSettings { get; set; } = [];

	/// <summary>Gets or sets the setlists.</summary>
	public List<Setlist> Setlists { get; set; } = [];

	/// <summary>Gets or sets the custom tabs.</summary>
	public List<CustomTab> CustomTabs { get; set; } = [];

	/// <summary>Gets or sets the deletion tombstones.</summary>
	public List<Tombstone> Tombstones { get; set; } = [];

	/// <summary>Gets or sets the database revision stamp.</summary>
	public RevisionStamp Revision { get; set; } = new();

	/// <summary>Creates a new database.</summary>
	/// <param name="name">The database name.</param>
	/// <param name="deviceId">The creating device identifier.</param>
	/// <param name="now">The optional creation time.</param>
	/// <returns>A newly initialized database.</returns>
	public static ChordDatabase Create(string name, Guid deviceId, DateTimeOffset? now = null) => new()
	{
		Id = now is DateTimeOffset timestamp ? Guid.CreateVersion7(timestamp) : Guid.CreateVersion7(),
		Name = name,
		Revision = RevisionStamp.Initial(deviceId, now),
	};
}
