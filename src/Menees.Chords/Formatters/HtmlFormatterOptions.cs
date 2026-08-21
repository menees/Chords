namespace Menees.Chords.Formatters;

#region Using Directives

using System.Globalization;
using System.Text;
using Menees.Chords.Formatters.Html;

#endregion

/// <summary>
/// Provides convenient overrides for the formatter's default styles.
/// Unset properties retain the formatter's default CSS value.
/// </summary>
public sealed class HtmlFormatterOptions
{
	#region Private Data Members

	private readonly SortedDictionary<string, EnvironmentStyle> environmentStyles = new(StringComparer.OrdinalIgnoreCase);

	#endregion

	#region Public Properties

	/// <summary>Gets the default style inherited by rendered text unless a category has its own default or override.</summary>
	public TextStyle DefaultTextStyle { get; } = new();

	/// <summary>Gets the title text overrides.</summary>
	public TextStyle TitleStyle { get; } = new();

	/// <summary>Gets the subtitle text overrides.</summary>
	public TextStyle SubtitleStyle { get; } = new();

	/// <summary>Gets the metadata text overrides.</summary>
	public TextStyle MetadataStyle { get; } = new();

	/// <summary>Gets the lyric text overrides.</summary>
	public TextStyle LyricStyle { get; } = new();

	/// <summary>Gets the chord text overrides.</summary>
	public TextStyle ChordStyle { get; } = new();

	/// <summary>Gets the comment and annotation text overrides.</summary>
	public TextStyle CommentStyle { get; } = new();

	/// <summary>Gets the section header text overrides.</summary>
	public TextStyle SectionHeaderStyle { get; } = new();

	/// <summary>Gets the tablature text overrides.</summary>
	public TextStyle TablatureStyle { get; } = new();

	/// <summary>Gets the grid text overrides.</summary>
	public TextStyle GridStyle { get; } = new();

	/// <summary>Gets the chord diagram caption overrides.</summary>
	public TextStyle DiagramCaptionStyle { get; } = new();

	/// <summary>
	/// Gets environment-specific layout and text overrides keyed by the name after <c>start_of_</c>.
	/// </summary>
	public IDictionary<string, EnvironmentStyle> EnvironmentStyles => this.environmentStyles;

	/// <summary>Gets or sets the CSS font family used by tablature and grids unless their styles override it.</summary>
	public CssFontFamily? MonospaceFontFamily { get; set; }

	/// <summary>Gets or sets the unitless line-height multiplier for the chord sheet.</summary>
	public double? LineSpacing { get; set; }

	/// <summary>Gets or sets the unitless line-height multiplier between chords and their lyrics.</summary>
	public double? ChordLineSpacing { get; set; }

	/// <summary>Gets or sets the gap between consecutive musical lines.</summary>
	public CssSize? MusicLineGap { get; set; }

	/// <summary>Gets or sets the section indentation.</summary>
	public CssSize? SectionIndent { get; set; }

	/// <summary>Gets or sets the grid line thickness.</summary>
	public CssSize? GridLineThickness { get; set; }

	/// <summary>Gets or sets the thickness of lyric extension lines within words.</summary>
	public CssSize? LyricExtensionThickness { get; set; }

	/// <summary>Gets or sets the gap between consecutive chords within a word.</summary>
	public CssSize? ConsecutiveChordGap { get; set; }

	/// <summary>Gets or sets the chord diagram size.</summary>
	public CssSize? DiagramSize { get; set; }

	/// <summary>Gets or sets the chord diagram line color.</summary>
	public CssColor? DiagramLineColor { get; set; }

	/// <summary>Gets or sets the chord diagram dot color.</summary>
	public CssColor? DiagramDotColor { get; set; }

	/// <summary>Gets or sets the block size of each responsive page.</summary>
	public CssSize? PageBlockSize { get; set; }

	/// <summary>Gets or sets the padding within each responsive page.</summary>
	public CssSize? PagePadding { get; set; }

	/// <summary>Gets or sets the gap between responsive columns.</summary>
	public CssSize? ColumnGap { get; set; }

	/// <summary>Gets or sets the minimum width of a responsive column.</summary>
	public CssSize? ColumnMinimumWidth { get; set; }

	#endregion

	#region Internal Methods

	internal string ToCss()
	{
		StringBuilder variables = new();
		Append(variables, "monospace-font-family", this.MonospaceFontFamily);
		Append(variables, "line-spacing", this.LineSpacing);
		Append(variables, "chord-line-spacing", this.ChordLineSpacing);
		Append(variables, "music-line-gap", this.MusicLineGap);
		Append(variables, "section-indent", this.SectionIndent);
		Append(variables, "grid-line-thickness", this.GridLineThickness);
		Append(variables, "lyric-extension-thickness", this.LyricExtensionThickness);
		Append(variables, "consecutive-chord-gap", this.ConsecutiveChordGap);
		Append(variables, "diagram-size", this.DiagramSize);
		Append(variables, "diagram-line-color", this.DiagramLineColor);
		Append(variables, "diagram-dot-color", this.DiagramDotColor);
		Append(variables, "page-block-size", this.PageBlockSize);
		Append(variables, "page-padding", this.PagePadding);
		Append(variables, "column-gap", this.ColumnGap);
		Append(variables, "column-min-width", this.ColumnMinimumWidth);

		StringBuilder result = new();
		if (variables.Length > 0)
		{
			result.AppendLine(":root {");
			result.Append(variables);
			result.AppendLine("}");
		}

		this.DefaultTextStyle.AppendCss(result, ".chord-sheet");
		this.TitleStyle.AppendCss(result, ".title");
		this.SubtitleStyle.AppendCss(result, ".subtitle");
		this.MetadataStyle.AppendCss(result, ".metadata-entry");
		this.LyricStyle.AppendCss(result, ".lyric, .lyric-line");
		this.ChordStyle.AppendCss(result, ".chord, .chord-only-line");
		this.CommentStyle.AppendCss(result, ".comment, .annotation");
		this.SectionHeaderStyle.AppendCss(result, ".section-header");
		this.TablatureStyle.AppendCss(result, ".tablature-line");
		this.GridStyle.AppendCss(result, ".grid-line");
		this.DiagramCaptionStyle.AppendCss(result, ".chord-diagram-name");
		foreach (KeyValuePair<string, EnvironmentStyle> pair in this.environmentStyles)
		{
			pair.Value.AppendCss(result, pair.Key);
		}

		return result.ToString().TrimEnd();
	}

	#endregion

	#region Private Methods

	private static void Append(StringBuilder builder, string name, double? value)
	{
		if (value.HasValue)
		{
			Append(builder, name, value.Value.ToString(CultureInfo.InvariantCulture));
		}
	}

	private static void Append(StringBuilder builder, string name, object? value)
	{
		if (value is not null)
		{
			string text = value.ToString()!;
			if (!CssValueValidator.IsStructurallyValid(text))
			{
				throw new FormatException($"'{text}' is not a valid CSS declaration value.");
			}

			builder.Append("\t--");
			builder.Append(name);
			builder.Append(": ");
			builder.Append(text.Trim());
			builder.AppendLine(";");
		}
	}

	#endregion
}
