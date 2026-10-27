namespace Menees.Chords.Db;

/// <summary>An explicit decision for one reviewed source.</summary>
public enum BookImportAction
{
	/// <summary>Retain the existing book without adding this source.</summary>
	Skip,

	/// <summary>Create a distinct song, even when related content exists.</summary>
	ImportNewSong,

	/// <summary>Append an active sheet while preserving the target song's metadata and default.</summary>
	AddSheet,
}