namespace Menees.Chords.Formatters.Html;

#region Using Directives

using System.Text;

#endregion

/// <summary>Represents a CSS font-family list or variable expression.</summary>
public sealed class CssFontFamily : IEquatable<CssFontFamily>, IParsable<CssFontFamily>, ISpanParsable<CssFontFamily>, IUtf8SpanParsable<CssFontFamily>
{
	#region Private Data Members

	private static readonly HashSet<string> GenericFamilies = new(StringComparer.OrdinalIgnoreCase)
	{
		"cursive", "emoji", "fangsong", "fantasy", "math", "monospace", "sans-serif", "serif", "system-ui",
		"ui-monospace", "ui-rounded", "ui-sans-serif", "ui-serif",
	};

	private readonly string value;

	#endregion

	#region Constructors

	private CssFontFamily(string value) => this.value = value;

	#endregion

	#region Public Methods

	/// <summary>Creates a font-family value from one literal family name.</summary>
	/// <param name="familyName">The family name or a recognized CSS generic family.</param>
	/// <returns>The CSS font-family value.</returns>
	public static CssFontFamily FromName(string familyName) => FromNames(familyName);

	/// <summary>Creates a prioritized font-family list from literal family names.</summary>
	/// <param name="familyNames">The family names, optionally ending with a recognized CSS generic family.</param>
	/// <returns>The CSS font-family list.</returns>
	public static CssFontFamily FromNames(params string[] familyNames)
		=> FromNames((IEnumerable<string>)familyNames);

	/// <summary>Creates a prioritized font-family list from literal family names.</summary>
	/// <param name="familyNames">The family names, optionally ending with a recognized CSS generic family.</param>
	/// <returns>The CSS font-family list.</returns>
	public static CssFontFamily FromNames(IEnumerable<string> familyNames)
	{
		Conditions.RequireNonNull(familyNames);
		List<string> formattedNames = [];
		foreach (string familyName in familyNames)
		{
			if (string.IsNullOrWhiteSpace(familyName))
			{
				throw new ArgumentException("Font family names cannot be empty.", nameof(familyNames));
			}

			string name = familyName.Trim();
			formattedNames.Add(GenericFamilies.Contains(name) ? name : Quote(name));
		}

		if (formattedNames.Count == 0)
		{
			throw new ArgumentException("At least one font family name is required.", nameof(familyNames));
		}

		return new(string.Join(", ", formattedNames));
	}

	/// <summary>Parses a CSS font-family list or variable expression.</summary>
	/// <param name="value">The CSS value.</param>
	/// <returns>The parsed font-family value.</returns>
	/// <exception cref="FormatException">The value is not a supported CSS font-family value.</exception>
	public static CssFontFamily Parse(string value)
		=> TryParse(value, out CssFontFamily? result)
			? result!
			: throw new FormatException($"'{value}' is not a supported CSS font-family value.");

	/// <inheritdoc/>
	public static CssFontFamily Parse(string value, IFormatProvider? provider) => Parse(value);

	/// <inheritdoc/>
	public static CssFontFamily Parse(ReadOnlySpan<char> value, IFormatProvider? provider) => Parse(value.ToString());

	/// <inheritdoc/>
	public static CssFontFamily Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
		=> CssValueValidator.TryDecodeUtf8(utf8Text, out string? value) && TryParse(value, out CssFontFamily? result)
			? result!
			: throw new FormatException("The UTF-8 value is not a supported CSS font-family value.");

	/// <summary>Attempts to parse a CSS font-family list or variable expression.</summary>
	/// <param name="value">The CSS value.</param>
	/// <param name="result">The parsed value, if successful.</param>
	/// <returns>True if the value was parsed successfully.</returns>
	public static bool TryParse(string? value, out CssFontFamily? result)
	{
		result = null;
		bool supported = CssValueValidator.IsStructurallyValid(value) && HasValidStructure(value!);
		if (supported)
		{
			result = new(value!.Trim());
		}

		return supported;
	}

	/// <inheritdoc/>
	public static bool TryParse(
		string? value,
		IFormatProvider? provider,
		[System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out CssFontFamily result)
	{
		bool success = TryParse(value, out CssFontFamily? parsed);
		result = parsed!;
		return success;
	}

	/// <inheritdoc/>
	public static bool TryParse(
		ReadOnlySpan<char> value,
		IFormatProvider? provider,
		[System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out CssFontFamily result)
		=> TryParse(value.ToString(), provider, out result);

	/// <inheritdoc/>
	public static bool TryParse(
		ReadOnlySpan<byte> utf8Text,
		IFormatProvider? provider,
		[System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out CssFontFamily result)
	{
		bool decoded = CssValueValidator.TryDecodeUtf8(utf8Text, out string? value);
		return TryParse(decoded ? value : null, provider, out result);
	}

	/// <inheritdoc/>
	public bool Equals(CssFontFamily? other)
		=> other is not null && this.value.Equals(other.value, StringComparison.OrdinalIgnoreCase);

	/// <inheritdoc/>
	public override bool Equals(object? obj) => this.Equals(obj as CssFontFamily);

	/// <inheritdoc/>
	public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(this.value);

	/// <inheritdoc/>
	public override string ToString() => this.value;

	#endregion

	#region Private Methods

	private static bool HasValidStructure(string value)
	{
		bool escaped = false;
		char quote = '\0';
		int parentheses = 0;
		bool hasContent = false;
		bool result = true;
		foreach (char character in value.Trim())
		{
			if (escaped)
			{
				escaped = false;
			}
			else if (quote != '\0')
			{
				if (character == '\\')
				{
					escaped = true;
				}
				else if (character == quote)
				{
					quote = '\0';
				}
			}
			else if (character is '\'' or '"')
			{
				quote = character;
				hasContent = true;
			}
			else if (character == '(')
			{
				parentheses++;
				hasContent = true;
			}
			else if (character == ')')
			{
				parentheses--;
			}
			else if (character == ',' && parentheses == 0)
			{
				result &= hasContent;
				hasContent = false;
			}
			else
			{
				hasContent |= !char.IsWhiteSpace(character);
			}
		}

		return result && hasContent && quote == '\0' && !escaped;
	}

	private static string Quote(string familyName)
	{
		StringBuilder result = new(familyName.Length + 2);
		result.Append('"');
		foreach (char character in familyName)
		{
			if (character is '\\' or '"')
			{
				result.Append('\\');
			}

			result.Append(character);
		}

		result.Append('"');
		return result.ToString();
	}

	#endregion
}
