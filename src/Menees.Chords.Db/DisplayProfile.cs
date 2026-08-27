namespace Menees.Chords.Db;

/// <summary>Represents a complete song-display profile.</summary>
public sealed class DisplayProfile
{
	/// <summary>Gets or sets the theme name.</summary>
	public string Theme { get; set; } = "Default";

	/// <summary>Gets or sets the font size.</summary>
	public double FontSize { get; set; } = 18;

	/// <summary>Gets or sets the line-spacing multiplier.</summary>
	public double LineSpacing { get; set; } = 1;

	/// <summary>Gets or sets the column count.</summary>
	public int Columns { get; set; } = 1;

	/// <summary>Gets or sets whether chords are shown.</summary>
	public bool ShowChords { get; set; } = true;

	/// <summary>Gets or sets the notation system.</summary>
	public string NotationSystem { get; set; } = "Letter";
}
