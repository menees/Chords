namespace Menees.Chords;

/// <summary>Identifies the built-in ordinary and delegated ChordPro environments.</summary>
internal enum ChordProEnvironmentKind
{
	/// <summary>An arbitrary application-defined environment.</summary>
	Generic,

	/// <summary>The bridge environment.</summary>
	Bridge,

	/// <summary>The chorus environment.</summary>
	Chorus,

	/// <summary>The chord grid environment.</summary>
	Grid,

	/// <summary>The tablature environment.</summary>
	Tab,

	/// <summary>The verse environment.</summary>
	Verse,

	/// <summary>The delegated ABC music notation environment.</summary>
	Abc,

	/// <summary>The delegated LilyPond music notation environment.</summary>
	LilyPond,

	/// <summary>The delegated SVG environment.</summary>
	Svg,

	/// <summary>The delegated textblock environment.</summary>
	TextBlock,
}
