namespace Menees.Chords.Formatters.Html;

#region Using Directives

using System.Globalization;
using System.Text.RegularExpressions;

#endregion

/// <summary>Represents a CSS named, hexadecimal, functional, or variable color.</summary>
public sealed partial class CssColor : IEquatable<CssColor>, IParsable<CssColor>, ISpanParsable<CssColor>, IUtf8SpanParsable<CssColor>
{
	#region Private Data Members

	private static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase)
	{
		"color", "color-mix", "hsl", "hsla", "hwb", "lab", "lch", "light-dark", "oklab", "oklch", "rgb", "rgba", "var",
	};

	private static readonly Regex HexPattern = CreateHexPattern();

	private static readonly Regex NamePattern = CreateNamePattern();

	private readonly string value;

	#endregion

	#region Constructors

	private CssColor(string value) => this.value = value;

	#endregion

	#region Public Properties

	/// <summary>Gets the CSS <c>currentColor</c> value.</summary>
	public static CssColor CurrentColor { get; } = new("currentColor");

	/// <summary>Gets the CSS <c>transparent</c> value.</summary>
	public static CssColor Transparent { get; } = new("transparent");

	#endregion

	#region Public Methods

	/// <summary>Creates an opaque RGB color.</summary>
	public static CssColor FromRgb(byte red, byte green, byte blue) => new($"rgb({red} {green} {blue})");

	/// <summary>Creates an RGB color with an alpha value from zero through one.</summary>
	public static CssColor FromRgba(byte red, byte green, byte blue, double alpha)
	{
		if (alpha < 0 || alpha > 1 || double.IsNaN(alpha))
		{
			throw new ArgumentOutOfRangeException(nameof(alpha));
		}

		return new($"rgb({red} {green} {blue} / {alpha.ToString("0.################", CultureInfo.InvariantCulture)})");
	}

	/// <summary>Parses a CSS color.</summary>
	/// <param name="value">The CSS value.</param>
	/// <returns>The parsed color.</returns>
	/// <exception cref="FormatException">The value is not a supported CSS color.</exception>
	public static CssColor Parse(string value)
		=> TryParse(value, out CssColor? result) ? result! : throw new FormatException($"'{value}' is not a supported CSS color.");

	/// <inheritdoc/>
	public static CssColor Parse(string value, IFormatProvider? provider) => Parse(value);

	/// <inheritdoc/>
	public static CssColor Parse(ReadOnlySpan<char> value, IFormatProvider? provider) => Parse(value.ToString());

	/// <inheritdoc/>
	public static CssColor Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
		=> CssValueValidator.TryDecodeUtf8(utf8Text, out string? value) && TryParse(value, out CssColor? result)
			? result!
			: throw new FormatException("The UTF-8 value is not a supported CSS color.");

	/// <summary>Attempts to parse a CSS color.</summary>
	public static bool TryParse(string? value, out CssColor? result)
	{
		result = null;
		bool supported = CssValueValidator.IsStructurallyValid(value);
		if (supported)
		{
			string candidate = value!.Trim();
			supported = HexPattern.IsMatch(candidate) || NamePattern.IsMatch(candidate);
			if (!supported)
			{
				int parenthesis = candidate.IndexOf('(');
				supported = parenthesis > 0 && candidate[^1] == ')'
					&& Functions.Contains(candidate.Substring(0, parenthesis).Trim());
			}

			if (supported)
			{
				result = new(candidate);
			}
		}

		return supported;
	}

	/// <inheritdoc/>
	public static bool TryParse(
		string? value,
		IFormatProvider? provider,
		[System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out CssColor result)
	{
		bool success = TryParse(value, out CssColor? parsed);
		result = parsed!;
		return success;
	}

	/// <inheritdoc/>
	public static bool TryParse(
		ReadOnlySpan<char> value,
		IFormatProvider? provider,
		[System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out CssColor result)
		=> TryParse(value.ToString(), provider, out result);

	/// <inheritdoc/>
	public static bool TryParse(
		ReadOnlySpan<byte> utf8Text,
		IFormatProvider? provider,
		[System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out CssColor result)
	{
		bool decoded = CssValueValidator.TryDecodeUtf8(utf8Text, out string? value);
		return TryParse(decoded ? value : null, provider, out result);
	}

	/// <inheritdoc/>
	public bool Equals(CssColor? other) => other is not null && this.value.Equals(other.value, StringComparison.OrdinalIgnoreCase);

	/// <inheritdoc/>
	public override bool Equals(object? obj) => this.Equals(obj as CssColor);

	/// <inheritdoc/>
	public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(this.value);

	/// <inheritdoc/>
	public override string ToString() => this.value;

	#endregion

	#region Private Methods

	[GeneratedRegex(@"^\#(?:[0-9a-f]{3}|[0-9a-f]{4}|[0-9a-f]{6}|[0-9a-f]{8})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex CreateHexPattern();

	[GeneratedRegex(@"^-?[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex CreateNamePattern();

	#endregion
}
