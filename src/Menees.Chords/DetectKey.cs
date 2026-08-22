namespace Menees.Chords;

/// <summary>
/// Specifies how a song key should be detected when it has no key metadata.
/// </summary>
public enum DetectKey
{
	/// <summary>
	/// Do not detect a key. A key must be supplied by song metadata.
	/// </summary>
	MetadataOnly,

	/// <summary>
	/// Use the first named chord as the song key.
	/// </summary>
	FirstChord,

	/// <summary>
	/// Use the last named chord as the song key.
	/// </summary>
	LastChord,
}
