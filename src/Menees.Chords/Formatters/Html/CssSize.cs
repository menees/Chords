namespace Menees.Chords.Formatters.Html;

#region Using Directives

using System.Globalization;
using System.Text.RegularExpressions;

#endregion

/// <summary>Represents a CSS size, length, percentage, keyword, or size-valued function.</summary>
public sealed partial class CssSize : IEquatable<CssSize>, IParsable<CssSize>, ISpanParsable<CssSize>, IUtf8SpanParsable<CssSize>
{
	#region Private Data Members

	private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
	{
		"auto", "contain", "fit-content", "inherit", "initial", "large", "larger", "math", "max-content",
		"medium", "min-content", "none", "revert", "revert-layer", "small", "smaller", "stretch", "unset",
		"x-large", "x-small", "xx-large", "xx-small", "xxx-large",
	};

	private static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase)
	{
		"calc", "clamp", "env", "fit-content", "max", "min", "var",
	};

	private static readonly Regex LengthPattern = CreateLengthPattern();

	private readonly string value;

	#endregion

	#region Constructors

	private CssSize(string value) => this.value = value;

	#endregion

	#region Public Methods

	/// <summary>Gets a unitless zero size.</summary>
	public static CssSize Zero { get; } = new("0");

	/// <summary>Creates a size in em units.</summary>
	public static CssSize Em(double value) => FromNumber(value, "em");

	/// <summary>Creates a size expressed as a percentage.</summary>
	public static CssSize Percent(double value) => FromNumber(value, "%");

	/// <summary>Creates a size in pixels.</summary>
	public static CssSize Pixels(double value) => FromNumber(value, "px");

	/// <summary>Creates a size in points.</summary>
	public static CssSize Points(double value) => FromNumber(value, "pt");

	/// <summary>Creates a size in rem units.</summary>
	public static CssSize Rem(double value) => FromNumber(value, "rem");

	/// <summary>Parses a CSS size.</summary>
	/// <param name="value">The CSS value.</param>
	/// <returns>The parsed size.</returns>
	/// <exception cref="FormatException">The value is not a supported CSS size.</exception>
	public static CssSize Parse(string value)
		=> TryParse(value, out CssSize? result) ? result! : throw new FormatException($"'{value}' is not a supported CSS size.");

	/// <inheritdoc/>
	public static CssSize Parse(string value, IFormatProvider? provider) => Parse(value);

	/// <inheritdoc/>
	public static CssSize Parse(ReadOnlySpan<char> value, IFormatProvider? provider) => Parse(value.ToString());

	/// <inheritdoc/>
	public static CssSize Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
		=> CssValueValidator.TryDecodeUtf8(utf8Text, out string? value) && TryParse(value, out CssSize? result)
			? result!
			: throw new FormatException("The UTF-8 value is not a supported CSS size.");

	/// <summary>Attempts to parse a CSS size.</summary>
	public static bool TryParse(string? value, out CssSize? result)
	{
		result = null;
		bool supported = CssValueValidator.IsStructurallyValid(value);
		if (supported)
		{
			string candidate = value!.Trim();
			supported = LengthPattern.IsMatch(candidate) || Keywords.Contains(candidate);
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
		[System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out CssSize result)
	{
		bool success = TryParse(value, out CssSize? parsed);
		result = parsed!;
		return success;
	}

	/// <inheritdoc/>
	public static bool TryParse(
		ReadOnlySpan<char> value,
		IFormatProvider? provider,
		[System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out CssSize result)
		=> TryParse(value.ToString(), provider, out result);

	/// <inheritdoc/>
	public static bool TryParse(
		ReadOnlySpan<byte> utf8Text,
		IFormatProvider? provider,
		[System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out CssSize result)
	{
		bool decoded = CssValueValidator.TryDecodeUtf8(utf8Text, out string? value);
		return TryParse(decoded ? value : null, provider, out result);
	}

	/// <inheritdoc/>
	public bool Equals(CssSize? other) => other is not null && this.value.Equals(other.value, StringComparison.OrdinalIgnoreCase);

	/// <inheritdoc/>
	public override bool Equals(object? obj) => this.Equals(obj as CssSize);

	/// <inheritdoc/>
	public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(this.value);

	/// <inheritdoc/>
	public override string ToString() => this.value;

	#endregion

	#region Private Methods

	[GeneratedRegex(
		@"^[+-]?(?:(?:\d+(?:\.\d*)?|\.\d+)" +
		@"(?:%|cap|ch|cm|dvh|dvw|em|ex|ic|in|lh|lvh|lvw|mm|pc|pt|px|q|rem|rlh|svh|svw|vb|vh|vi|vmax|vmin|vw|x)" +
		@"|0(?:\.0*)?)$",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex CreateLengthPattern();

	private static CssSize FromNumber(double value, string unit)
	{
		if (double.IsNaN(value) || double.IsInfinity(value))
		{
			throw new ArgumentOutOfRangeException(nameof(value));
		}

		return new(value.ToString("0.################", CultureInfo.InvariantCulture) + unit);
	}

	#endregion
}
