#region Using Directives

using System.Text;

#endregion

namespace Menees.Chords.Book.Application;

/// <summary>Formats editable semicolon lists without splitting values containing delimiters or quotes.</summary>
public static class CatalogValueList
{
	#region Public Methods

	/// <summary>Formats values for catalog controls, quoting only when required.</summary>
	public static string Format(IEnumerable<string> values)
	{
		ArgumentNullException.ThrowIfNull(values);
		return string.Join("; ", values.Select(value =>
			value.Contains(';', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal) || value != value.Trim()
				? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value));
	}

	/// <summary>Parses semicolon lists with optional quoted values and doubled embedded quotes.</summary>
	public static IReadOnlyList<string> Parse(string? text)
	{
		List<string> values = [];
		StringBuilder value = new();
		bool quoted = false, closed = false, hasContent = false;
		text ??= string.Empty;
		for (int index = 0; index < text.Length; index++)
		{
			char character = text[index];
			if (quoted)
			{
				if (character != '"')
				{
					value.Append(character);
				}
				else if (index + 1 < text.Length && text[index + 1] == '"')
				{
					value.Append('"');
					index++;
				}
				else
				{
					quoted = false;
					closed = true;
				}
			}
			else if (character == ';')
			{
				AddValue(values, value, closed);
				value.Clear();
				closed = hasContent = false;
			}
			else if (closed)
			{
				if (!char.IsWhiteSpace(character))
				{
					throw new ArgumentException("Separate quoted values with a semicolon.");
				}
			}
			else if (character == '"' && !hasContent)
			{
				value.Clear();
				quoted = true;
			}
			else
			{
				value.Append(character);
				hasContent |= !char.IsWhiteSpace(character);
			}
		}

		if (quoted)
		{
			throw new ArgumentException("Close the quoted catalog value before saving.");
		}

		AddValue(values, value, closed);
		return values;
	}

	/// <summary>Adds one selected existing value while retaining an unchanged entry when it is already present.</summary>
	public static string Append(string? text, string selected)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(selected);
		IReadOnlyList<string> values = Parse(text);
		string normalized = selected.Trim().Normalize(NormalizationForm.FormC);
		bool exists = values.Any(value => StringComparer.OrdinalIgnoreCase.Equals(value.Normalize(NormalizationForm.FormC), normalized));
		return exists ? text ?? string.Empty : Format(values.Append(selected));
	}

	#endregion

	#region Private Methods

	private static void AddValue(List<string> values, StringBuilder value, bool wasQuoted)
	{
		string text = value.ToString();
		if (!wasQuoted)
		{
			text = text.Trim();
		}

		if (text.Length > 0)
		{
			values.Add(text);
		}
	}

	#endregion
}
