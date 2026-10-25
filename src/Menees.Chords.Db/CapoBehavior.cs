namespace Menees.Chords.Db;

/// <summary>Identifies how capo settings affect displayed chords.</summary>
public enum CapoBehavior
{
	/// <summary>None: record the capo without changing shown chords.</summary>
	DisplayOnly,

	/// <summary>Down: lower shown chords by the capo fret.</summary>
	AffectsShownChords,

	/// <summary>Up: raise shown chords by the capo fret.</summary>
	RaiseShownChords,
}
