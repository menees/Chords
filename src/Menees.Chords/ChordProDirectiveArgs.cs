namespace Menees.Chords;

#region Using Directives

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// The parsed arguments from a ChordPro directive with the single legacy <see cref="Value"/>
/// and with v6+ key=value pairs in <see cref="Attributes"/>.
/// </summary>
public sealed class ChordProDirectiveArgs
{
	#region Private Data Members

	private const string KeyValuePattern = """(?in)^\s*(((?<key>\w+?)\s*=\s*(("(?<value>[^"]*?)")|('(?<value>[^']*?)')))\s*)+$""";

	private static readonly StringComparer Comparer = ChordParser.Comparer;
	private static readonly Regex KeyValueRegex = new(KeyValuePattern, RegexOptions.Compiled);
	private static readonly ReadOnlyDictionary<string, string> EmptyAttributes = new(new Dictionary<string, string>(Comparer));

	#endregion

	#region Constructors

	internal ChordProDirectiveArgs(string? value, bool tryParse = true)
	{
		this.Value = value;
		this.Attributes = (tryParse ? TryParseKeyValuePairs(value) : null) ?? EmptyAttributes;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the directive's optional argument (i.e., the part after the separator).
	/// </summary>
	/// <seealso cref="Attributes"/>
	public string? Value { get; }

	/// <summary>
	/// Gets the directive's key=value attribute pairs in declaration order.
	/// </summary>
	/// <remarks>
	/// ChordPro v6+ says attributes can be in key="value" or key='value' format.
	/// </remarks>
	/// <seealso cref="Value"/>
	public IReadOnlyDictionary<string, string> Attributes { get; }

	/// <summary>
	/// Gets the first <see cref="Attributes"/> value if one exists; otherwise, gets <see cref="Value"/>.
	/// </summary>
	/// <remarks>
	/// This allows you to get a directive's primary value if ChordPro v6+'s key=value syntax is used
	/// or if older syntax is used. For example, "Verse 1" is returned from both of these variations:
	/// {start_of_verse: label="Verse 1"} and {start_of_verse: Verse 1}.
	/// </remarks>
	public string? FirstValue => this.Attributes.Count > 0 // TODO: Name this better. [Bill, 5/25/2025]
		? this.Attributes.First().Value // TODO: Make more efficient. [Bill, 5/25/2025]
		: this.Value;

	#endregion

	#region Private Methods

	private static IReadOnlyDictionary<string, string>? TryParseKeyValuePairs(string? text)
	{
		IReadOnlyDictionary<string, string>? result = null;

		if (!string.IsNullOrEmpty(text))
		{
			Match match = KeyValueRegex.Match(text);
			Group keyGroup, valueGroup;
			if (match.Success
				&& (keyGroup = match.Groups["key"]).Success
				&& (valueGroup = match.Groups["value"]).Success
				&& keyGroup.Captures.Count == valueGroup.Captures.Count)
			{
				// TODO: Use OrderedDictionary. [Bill, 5/24/2025]
				Dictionary<string, string> keyValuePairs = new(Comparer);
				for (int i = 0; i < keyGroup.Captures.Count; i++)
				{
					string key = keyGroup.Captures[i].Value;
					string value = valueGroup.Captures[i].Value;

					// TODO: start_of_ label attribute can contain \n per https://www.chordpro.org/chordpro/directives-env/. [Bill, 5/25/2025]
					// TODO: Also decode &apos; &quot; &amp; &lt; &gt; https://stackoverflow.com/a/1091953/1882616 [Bill, 5/25/2025]
					keyValuePairs[key] = value;
				}

				result = keyValuePairs;
			}
		}

		return result;
	}

	#endregion
}
