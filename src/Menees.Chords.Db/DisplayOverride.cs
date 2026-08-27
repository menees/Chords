using System.Text.Json.Serialization;

namespace Menees.Chords.Db;

/// <summary>Represents optional song-specific display overrides.</summary>
public sealed class DisplayOverride
{
	/// <summary>Gets or sets the theme override.</summary>
	public string? Theme { get; set; }

	/// <summary>Gets or sets the font-size override.</summary>
	public double? FontSize { get; set; }

	/// <summary>Gets or sets the line-spacing override.</summary>
	public double? LineSpacing { get; set; }

	/// <summary>Gets or sets the column-count override.</summary>
	public int? Columns { get; set; }

	/// <summary>Gets or sets whether chords are shown.</summary>
	public bool? ShowChords { get; set; }

	/// <summary>Gets or sets the notation-system override.</summary>
	public string? NotationSystem { get; set; }

	/// <summary>Gets whether at least one override has a value.</summary>
	[JsonIgnore]
	public bool HasValues => this.Theme is not null || this.FontSize is not null || this.LineSpacing is not null
		|| this.Columns is not null || this.ShowChords is not null || this.NotationSystem is not null;
}
