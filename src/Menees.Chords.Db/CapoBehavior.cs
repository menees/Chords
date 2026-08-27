namespace Menees.Chords.Db;

/// <summary>Identifies how capo settings affect displayed chords.</summary>
public enum CapoBehavior
{
	/// <summary>The capo is displayed without changing shown chords.</summary>
	DisplayOnly,

	/// <summary>The capo affects the chords shown to the user.</summary>
	AffectsShownChords,
}
