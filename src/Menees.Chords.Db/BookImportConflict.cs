namespace Menees.Chords.Db;

/// <summary>Explains why an incoming source needs review.</summary>
public enum BookImportConflict
{
	/// <summary>No catalog or earlier selected source appears related.</summary>
	NewSong,

	/// <summary>The same title and source bytes already occur.</summary>
	ExactDuplicate,

	/// <summary>Identical bytes occur with a different title.</summary>
	IdenticalContent,

	/// <summary>The title and artists suggest another version, or artists are missing.</summary>
	PossibleVersion,
}