namespace Menees.Chords.Formatters;

/// <summary>
/// Specifies how an HTML chord diagram is rendered.
/// </summary>
public enum ChordDiagramMode
{
	/// <summary>Omits the diagram.</summary>
	None,

	/// <summary>Renders the diagram graphically.</summary>
	Image,

	/// <summary>Renders the chord name and positions as compact text.</summary>
	CompactText,
}
