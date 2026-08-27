namespace Menees.Chords.Db;

/// <summary>Identifies a song file's source format.</summary>
public enum SourceFormat
{
	/// <summary>The source format is unknown.</summary>
	Unknown,

	/// <summary>The source uses ChordPro syntax.</summary>
	ChordPro,

	/// <summary>The source uses chord-over-text syntax.</summary>
	ChordOverText,

	/// <summary>The source mixes supported text formats.</summary>
	Mixed,

	/// <summary>The source uses OpenSong XML.</summary>
	OpenSongXml,
}
