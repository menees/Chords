namespace Menees.Chords.Formatters.Html;

#region Using Directives

using System.Text;
using System.Xml.Linq;

#endregion

/// <summary>Formats chord symbols without changing the parsed chord or its source representation.</summary>
internal static class ChordSymbolFormatter
{
	#region Internal Methods

	internal static void AppendTo(XElement element, Chord chord, bool isParenthesized)
	{
		if (isParenthesized)
		{
			element.Add("(");
		}

		if (chord.Notation == Notation.Nashville)
		{
			AppendNashville(element, chord);
		}
		else
		{
			element.Add(chord.Name);
		}

		if (isParenthesized)
		{
			element.Add(")");
		}
	}

	#endregion

	#region Private Methods

	private static void AppendNashville(XElement element, Chord chord)
	{
		element.Add(Accidentals(chord.Root));
		StringBuilder? extension = null;
		IReadOnlyList<string> modifiers = chord.Modifiers;
		for (int index = 0; index < modifiers.Count; index++)
		{
			string modifier = modifiers[index];
			if (index == 0 && MusicTheory.IsMinor(modifiers))
			{
				element.Add("-");
			}
			else if (index == 0 && IsMajor(modifier)
				&& (index + 1) < modifiers.Count && modifiers[index + 1] is "7" or "9" or "11" or "13")
			{
				(extension ??= new()).Append('△');
				if (modifiers[index + 1] == "7")
				{
					index++;
				}
			}
			else
			{
				(extension ??= new()).Append(modifier.ToLowerInvariant() switch
				{
					"dim" => "°",
					"aug" => "+",
					_ => Accidentals(modifier),
				});
			}
		}

		if (extension is { Length: > 0 })
		{
			if (modifiers.Count == 1 && modifiers[0] == "7")
			{
				element.Add(new XElement("sup", new XAttribute("class", "chord-extension"), extension.ToString()));
			}
			else
			{
				element.Add(extension.ToString());
			}
		}

		if (chord.Bass is not null)
		{
			element.Add("/" + Accidentals(chord.Bass));
		}

		element.Add(chord.Annotation);
	}

	private static bool IsMajor(string modifier)
		=> modifier is "M" or "^" || modifier.Equals("maj", StringComparison.OrdinalIgnoreCase);

	private static string Accidentals(string value) => value.Replace("#", "♯", StringComparison.Ordinal).Replace("b", "♭", StringComparison.Ordinal);

	#endregion
}
