namespace Menees.Chords.Db;

/// <summary>Identifies a native chord-book validation failure.</summary>
public enum BookValidationIssueKind
{
	/// <summary>The canonical database JSON could not be read or validated.</summary>
	InvalidDatabase,

	/// <summary>A database-referenced managed asset is absent.</summary>
	MissingAsset,

	/// <summary>The store exposed an asset that is not referenced by the database.</summary>
	UnexpectedManagedAsset,

	/// <summary>The stored path differs from the database path.</summary>
	PathMismatch,

	/// <summary>The observed byte length differs from recorded metadata.</summary>
	LengthMismatch,

	/// <summary>The independently computed content hash differs from recorded metadata.</summary>
	HashMismatch,

	/// <summary>The managed asset could not be read.</summary>
	UnreadableAsset,
}
