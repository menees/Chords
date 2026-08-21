namespace Menees.Chords.Formatters.Html;

#region Using Directives

using System.Text;

#endregion

/// <summary>
/// Defines optional font, emphasis, foreground, and highlight overrides for one category of rendered text.
/// </summary>
public sealed class TextStyle
{
	#region Public Properties

	/// <summary>Gets or sets whether the text is bold. Null retains the formatter default.</summary>
	public bool? Bold { get; set; }

	/// <summary>Gets or sets the text color. Null retains the formatter default.</summary>
	public CssColor? Color { get; set; }

	/// <summary>Gets or sets the CSS font family. Null retains the formatter default or inherited family.</summary>
	public CssFontFamily? FontFamily { get; set; }

	/// <summary>Gets or sets the font size. Null retains the formatter default.</summary>
	public CssSize? FontSize { get; set; }

	/// <summary>Gets or sets the highlight (background) color. Null retains the formatter default.</summary>
	public CssColor? HighlightColor { get; set; }

	/// <summary>Gets or sets whether the text is italic. Null retains the formatter default.</summary>
	public bool? Italic { get; set; }

	#endregion

	#region Internal Methods

	internal void AppendCss(StringBuilder builder, string selector)
	{
		StringBuilder declarations = new();
		Append(declarations, "font-family", this.FontFamily);
		Append(declarations, "font-size", this.FontSize);
		Append(declarations, "font-weight", this.Bold.HasValue ? this.Bold.Value ? "bold" : "normal" : null);
		Append(declarations, "font-style", this.Italic.HasValue ? this.Italic.Value ? "italic" : "normal" : null);
		Append(declarations, "color", this.Color);
		Append(declarations, "background-color", this.HighlightColor);
		if (declarations.Length > 0)
		{
			builder.Append(selector);
			builder.AppendLine(" {");
			builder.Append(declarations);
			builder.AppendLine("}");
		}
	}

	#endregion

	#region Private Methods

	private static void Append(StringBuilder builder, string name, object? value)
	{
		if (value is not null)
		{
			string text = value.ToString()!;
			if (!CssValueValidator.IsStructurallyValid(text))
			{
				throw new FormatException($"'{text}' is not a valid CSS declaration value.");
			}

			builder.Append('\t');
			builder.Append(name);
			builder.Append(": ");
			builder.Append(text.Trim());
			builder.AppendLine(";");
		}
	}

	#endregion
}
