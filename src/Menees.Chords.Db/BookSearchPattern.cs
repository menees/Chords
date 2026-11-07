#region Using Directives

using System.IO.Enumeration;
using System.Text;

#endregion

namespace Menees.Chords.Db;

/// <summary>One prepared simple wildcard expression, shared by every indexed value in a search.</summary>
internal sealed class BookSearchPattern
{
	#region Private Data

	private readonly string literal;
	private readonly string? expression;
	private readonly bool leadingStarOnly;
	private readonly bool contains;

	#endregion

	#region Constructors

	public BookSearchPattern(string source, bool contains)
	{
		this.contains = contains;
		StringBuilder text = new(source.Length);
		StringBuilder pattern = new(source.Length);
		int wildcards = 0;
		bool previousStar = false;
		foreach (var (character, escaped) in Characters(source))
		{
			if (!escaped && character is '*' or '?')
			{
				// Coalesce stars before matching, bounding redundant states for pasted patterns.
				if (character != '*' || !previousStar)
				{
					text.Append(character);
					pattern.Append(character);
					wildcards++;
				}

				previousStar = character == '*';
			}
			else
			{
				previousStar = false;
				text.Append(character);
				if (character is '*' or '?' or '\\')
				{
					pattern.Append('\\');
				}

				pattern.Append(character);
			}
		}

		this.literal = text.ToString();
		if (wildcards > 0)
		{
			this.expression = contains ? "*" + pattern + "*" : pattern.ToString();

			// Keep an allocation-free suffix fast path, including escaped backslashes and punctuation.
			this.leadingStarOnly = !contains && wildcards == 1 && pattern[0] == '*';
		}
	}

	#endregion

	#region Public Properties

	public bool IsEmpty => this.literal.Length == 0;

	#endregion

	#region Public Methods

	public bool IsMatch(string value, bool exact)
	{
		bool result = false;
		if (this.literal.Length > 0 && !string.IsNullOrWhiteSpace(value))
		{
			result = this.expression is null ? exact && !this.contains ? value == this.literal : value.Contains(this.literal, StringComparison.Ordinal)
				: this.leadingStarOnly ? value.AsSpan().EndsWith(this.literal.AsSpan(1), StringComparison.Ordinal)
				: FileSystemName.MatchesSimpleExpression(this.expression, value, ignoreCase: false);
		}

		return result;
	}

	#endregion

	#region Private Methods

	private static IEnumerable<(char Character, bool Escaped)> Characters(string source)
	{
		for (int index = 0; index < source.Length; index++)
		{
			char character = source[index];
			if (character == '\\' && index + 1 < source.Length)
			{
				yield return (source[++index], true);
			}
			else if (character != '"')
			{
				yield return (character, character == '\\');
			}
		}
	}

	#endregion
}
