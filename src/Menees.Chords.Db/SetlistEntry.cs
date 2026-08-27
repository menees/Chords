namespace Menees.Chords.Db;

/// <summary>Represents one entry in a setlist.</summary>
public sealed class SetlistEntry
{
	/// <summary>Gets or sets the entry identifier.</summary>
	public Guid Id { get; set; }

	/// <summary>Gets or sets the song identifier.</summary>
	public Guid SongId { get; set; }

	/// <summary>Gets or sets the preferred song-file identifier.</summary>
	public Guid? PreferredSongFileId { get; set; }

	/// <summary>Gets or sets the instrument-profile identifier.</summary>
	public Guid? InstrumentProfileId { get; set; }

	/// <summary>Gets or sets the transposition in semitones.</summary>
	public int? TransposeSemitones { get; set; }
}
