using System.Text.Json.Serialization;

namespace Menees.Chords.Db;

/// <summary>Represents instrument-specific settings for a song.</summary>
public sealed class SongInstrumentSetting
{
	/// <summary>Gets or sets the setting identifier.</summary>
	public Guid Id { get; set; }

	/// <summary>Gets or sets the song identifier.</summary>
	public Guid SongId { get; set; }

	/// <summary>Gets or sets the instrument-profile identifier.</summary>
	public Guid InstrumentProfileId { get; set; }

	/// <summary>Gets or sets the transposition in semitones.</summary>
	public int TransposeSemitones { get; set; }

	/// <summary>Gets or sets the capo fret.</summary>
	public int? CapoFret { get; set; }

	/// <summary>Gets or sets the capo behavior.</summary>
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public CapoBehavior CapoBehavior { get; set; }

	/// <summary>Gets or sets the chord-spelling preference.</summary>
	public string? ChordSpellingPreference { get; set; }

	/// <summary>Gets or sets the preferred song-file identifier.</summary>
	public Guid? PreferredSongFileId { get; set; }

	/// <summary>Gets or sets the revision stamp.</summary>
	public RevisionStamp Revision { get; set; } = new();
}
