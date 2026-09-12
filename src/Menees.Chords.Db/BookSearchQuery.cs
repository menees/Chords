using System.Text;

namespace Menees.Chords.Db;

internal static class BookSearchQuery
{
	public static IReadOnlyList<(string? Field, string Value)> Parse(string query)
	{
		List<(string? Field, string Value)> result = [];
		StringBuilder token = new();
		bool quoted = false;
		foreach (char character in query)
		{
			if (character == '"')
			{
				quoted = !quoted;
			}
			else if (char.IsWhiteSpace(character) && !quoted)
			{
				AddToken(token, result);
			}
			else
			{
				token.Append(character);
			}
		}

		AddToken(token, result);
		return result;
	}

	private static void AddToken(StringBuilder token, List<(string? Field, string Value)> result)
	{
		if (token.Length > 0)
		{
			string value = token.ToString();
			int colon = value.IndexOf(':', StringComparison.Ordinal);
			string? field = colon > 0 ? value[..colon] : null;
			if (field is "title" or "artist" or "tag" or "key" or "genre" or "capo" or "duration"
				or "archived" or "display" or "metronome" or "multiple" or "recovery" or "archivedfiles")
			{
				result.Add((field, value[(colon + 1)..]));
			}
			else
			{
				result.Add((null, value));
			}

			token.Clear();
		}
	}
}
