namespace Menees.Chords.Book.Application;

internal static class KeyDisplay
{
	internal static string Format(string value)
	{
		string result = value;
		if (value.Length > 1 && value[1] is '#' or 'b' && char.ToUpperInvariant(value[0]) is >= 'A' and <= 'G')
		{
			result = string.Concat(value.AsSpan(0, 1), value[1] == '#' ? "♯" : "♭", value.AsSpan(2));
		}

		return result;
	}
}
