#region Using Directives

using System.Text.RegularExpressions;

#endregion

namespace Menees.Chords.Db;

internal static partial class BookSearchQuery
{
	#region Public Methods

	public static IReadOnlyList<Term> Parse(string query)
	{
		List<Term> result = [];
		foreach (Match token in Tokens().Matches(query))
		{
			string value = token.Value;
			Match prefix = FieldPrefix().Match(value);
			string? field = prefix.Success ? prefix.Groups["field"].Value : null;
			bool excluded = prefix.Success && prefix.Groups["excluded"].Success;
			result.Add(new(field, new(value[prefix.Length..], contains: field is null), excluded));
		}

		return result;
	}

	#endregion

	#region Private Methods

	// Quotes group whitespace; escapes keep quotes/whitespace in a token. An unfinished quote/backslash remains editable.
	[GeneratedRegex("(?:\\\\[\\s\\S]|\"(?:\\\\[\\s\\S]|[^\"\\\\])*\"?|[^\\s\"\\\\]|\\\\)+", RegexOptions.CultureInvariant)]
	private static partial Regex Tokens();

	[GeneratedRegex(@"\A(?<field>title|artist|tag|key|genre|capo|duration|album|time|archived|display|metronome|"
		+ @"multiple|recovery|archivedfiles):(?<excluded>!)?", RegexOptions.CultureInvariant)]
	private static partial Regex FieldPrefix();

	#endregion

	#region Internal Types

	internal sealed record Term(string? Field, BookSearchPattern Pattern, bool Excluded);

	#endregion
}
