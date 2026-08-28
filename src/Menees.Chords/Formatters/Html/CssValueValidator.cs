namespace Menees.Chords.Formatters.Html;

#region Using Directives

using System.Text;

#endregion

internal static class CssValueValidator
{
	#region Internal Methods

	internal static bool IsStructurallyValid(string? value)
	{
		bool result = !string.IsNullOrWhiteSpace(value);
		int parentheses = 0;
		foreach (char character in value ?? string.Empty)
		{
			switch (character)
			{
				case ';': case '{': case '}': case '<': case '>': case '\r': case '\n':
					result = false;
					break;

				case '(':
					parentheses++;
					break;

				case ')':
					if (--parentheses < 0)
					{
						result = false;
					}

					break;
			}
		}

		return result && parentheses == 0;
	}

	internal static bool TryDecodeUtf8(ReadOnlySpan<byte> utf8Text, out string? value)
	{
		bool result;
		try
		{
			value = new UTF8Encoding(false, true).GetString(utf8Text);
			result = true;
		}
		catch (DecoderFallbackException)
		{
			value = null;
			result = false;
		}

		return result;
	}

	#endregion
}
