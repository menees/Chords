namespace Menees.Chords.Formatters.Html;

#region Using Directives

using System.Text;
using System.Text.RegularExpressions;

#endregion

/// <summary>Defines layout and text overrides for one ChordPro environment.</summary>
public sealed partial class EnvironmentStyle
{
	#region Private Data Members

	private static readonly Regex NamePattern = CreateNamePattern();

	#endregion

	#region Public Properties

	/// <summary>Gets or sets the environment's start-border color.</summary>
	public CssColor? BorderColor { get; set; }

	/// <summary>Gets or sets the environment's start-border width. Use <see cref="CssSize.Zero"/> for no border.</summary>
	public CssSize? BorderWidth { get; set; }

	/// <summary>Gets text overrides inherited by the environment's content.</summary>
	public TextStyle ContentStyle { get; } = new();

	/// <summary>Gets text overrides for the environment's section header.</summary>
	public TextStyle HeaderStyle { get; } = new();

	/// <summary>Gets or sets the environment's inline-start margin.</summary>
	public CssSize? Indent { get; set; }

	/// <summary>Gets or sets the space between the start border and the environment content.</summary>
	public CssSize? Padding { get; set; }

	#endregion

	#region Internal Methods

	internal void AppendCss(StringBuilder builder, string environmentName)
	{
		if (!NamePattern.IsMatch(environmentName))
		{
			throw new FormatException($"'{environmentName}' is not a valid ChordPro environment name.");
		}

		string selector = $".section[data-environment=\"{environmentName}\"]";
		StringBuilder declarations = new();
		Append(declarations, "margin-inline-start", this.Indent);
		Append(declarations, "padding-inline-start", this.Padding);
		Append(declarations, "border-inline-start-width", this.BorderWidth);
		Append(declarations, "border-inline-start-color", this.BorderColor);
		if (this.BorderWidth is not null || this.BorderColor is not null)
		{
			Append(declarations, "border-inline-start-style", "solid");
		}

		if (declarations.Length > 0)
		{
			builder.Append(selector);
			builder.AppendLine(" {");
			builder.Append(declarations);
			builder.AppendLine("}");
		}

		this.ContentStyle.AppendCss(builder, selector);
		this.HeaderStyle.AppendCss(builder, selector + " > .section-header");
	}

	#endregion

	#region Private Methods

	[GeneratedRegex(@"^[a-z0-9_]+$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex CreateNamePattern();

	private static void Append(StringBuilder builder, string name, object? value)
	{
		if (value is not null)
		{
			builder.Append('\t');
			builder.Append(name);
			builder.Append(": ");
			builder.Append(value);
			builder.AppendLine(";");
		}
	}

	#endregion
}
