namespace Menees.Chords;

#region Using Directives

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
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

	private static readonly Regex KeyValueRegex = new(KeyValuePattern, RegexOptions.Compiled);
	private static readonly StringComparer Comparer = ChordParser.Comparer;
	private static readonly AttributeDictionary EmptyAttributes = new();

	private readonly AttributeDictionary attributes;

	#endregion

	#region Constructors

	internal ChordProDirectiveArgs(string? value, bool tryParse = true)
	{
		this.Value = value;
		this.attributes = (tryParse ? TryParseKeyValuePairs(value) : null) ?? EmptyAttributes;
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
	public IReadOnlyDictionary<string, string> Attributes => this.attributes;

	/// <summary>
	/// Gets the first <see cref="Attributes"/> value if one exists; otherwise, gets <see cref="Value"/>.
	/// </summary>
	/// <remarks>
	/// This allows you to get a directive's primary value if ChordPro v6+'s key=value syntax is used
	/// or if older syntax is used. For example, "Verse 1" is returned from both of these variations:
	/// {start_of_verse: label="Verse 1"} and {start_of_verse: Verse 1}.
	/// </remarks>
	public string? FirstValue => this.Attributes.Count > 0
		? this.attributes.FirstValue
		: this.Value;

	#endregion

	#region Private Methods

	private static AttributeDictionary? TryParseKeyValuePairs(string? text)
	{
		AttributeDictionary? result = null;

		if (!string.IsNullOrEmpty(text))
		{
			Match match = KeyValueRegex.Match(text);
			Group keyGroup, valueGroup;
			if (match.Success
				&& (keyGroup = match.Groups["key"]).Success
				&& (valueGroup = match.Groups["value"]).Success
				&& keyGroup.Captures.Count == valueGroup.Captures.Count)
			{
				result = new();
				for (int i = 0; i < keyGroup.Captures.Count; i++)
				{
					string key = keyGroup.Captures[i].Value;
					string value = Unescape(valueGroup.Captures[i].Value);

					// If there's a duplicate attribute key, then we can't return a dictionary.
					if (!result.TryAdd(key, value))
					{
						result = null;
						break;
					}
				}
			}
		}

		return result;
	}

	private static string Unescape(string escaped)
	{
		// A start_of_ label attribute can contain \n for multi-line per https://www.chordpro.org/chordpro/directives-env/.
		// The docs don't address other C-style or HTML-style escape sequences (https://stackoverflow.com/a/1091953/1882616).
		string result = escaped.Replace(@"\n", "\n");
		return result;
	}

	#endregion

	#region Private Types

	private sealed class AttributeDictionary : IReadOnlyDictionary<string, string>
	{
		#region Private Data Members

		private readonly OrderedDictionary dictionary;

		#endregion

		#region Constructors

		public AttributeDictionary()
		{
			this.dictionary = new(Comparer);
		}

		#endregion

		#region Public Properties

		public string? FirstValue
			=> this.Count > 0 ? (string?)this.dictionary[0] : null;

		public int Count
			=> this.dictionary.Count;

		public IEnumerable<string> Keys
			=> this.dictionary.Keys.Cast<string>();

		public IEnumerable<string> Values
			=> this.dictionary.Values.Cast<string>();

		public string this[string key]
			=> this.dictionary[key] as string ?? throw new KeyNotFoundException($"Attribute '{key}' was not found.");

		#endregion

		#region Public Methods

		public bool ContainsKey(string key)
			=> this.dictionary.Contains(key);

		public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
		{
			foreach (DictionaryEntry entry in this.dictionary)
			{
				yield return new KeyValuePair<string, string>((string)entry.Key, (string?)entry.Value ?? string.Empty);
			}
		}

		public bool TryGetValue(string key, out string value)
		{
			bool result = false;
			value = string.Empty;

			if (this.dictionary[key] is string text)
			{
				value = text;
				result = true;
			}

			return result;
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return this.GetEnumerator();
		}

		#endregion

		#region Internal Methods

		internal bool TryAdd(string key, string value)
		{
			int countBefore = this.dictionary.Count;
			this.dictionary[key] = value;
			bool result = this.Count > countBefore;
			return result;
		}

		#endregion
	}

	#endregion
}
