namespace Menees.Chords.Db;

/// <summary>Describes optional behavior exposed by a book store.</summary>
[Flags]
public enum BookStoreCapabilities
{
	/// <summary>No optional capabilities.</summary>
	None = 0,

	/// <summary>Staged content can replace existing content atomically.</summary>
	AtomicReplace = 1,

	/// <summary>The store can detect changes made outside the application.</summary>
	ExternalChangeDetection = 2,

	/// <summary>The store location can be shown to the user.</summary>
	UserVisibleLocation = 4,

	/// <summary>The store can report its available capacity.</summary>
	AvailableSpaceReporting = 8,
}
