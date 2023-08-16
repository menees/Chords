﻿namespace Menees.Chords.Transformers;

#region Using Directives

using System.Collections.Generic;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// Transforms a <see cref="Document"/> into a ChordPro subset format that renders well in MobileSheets.
/// </summary>
/// <see href="https://www.zubersoft.com/mobilesheets/"/>
public sealed class MobileSheetsTransformer : ChordProTransformer
{
	#region Constructors

	/// <inheritdoc/>
	public MobileSheetsTransformer(Document document, bool? preferLongNames = null)
		: base(document, preferLongNames)
	{
	}

	#endregion

	#region Protected Methods

	/// <inheritdoc/>
	protected override IReadOnlyList<Entry> TransformEntries(IReadOnlyList<Entry> input)
	{
		List<Entry> supportedInput = new(input.Count);
		foreach (Entry entry in input)
		{
			if (entry is not ChordDefinitions definitions)
			{
				supportedInput.Add(entry);
			}
			else
			{
				// MobileSheets doesn't support {chord} or {define} directives as of v3.8.12 (2023-08-15).
				supportedInput.Add(new ChordProDirectiveLine("comment", definitions.ToString()));
			}
		}

		IReadOnlyList<Entry> chordPro = base.TransformEntries(supportedInput);
		List<Entry> result = new(chordPro.Count);

		const StringComparison Comparison = ChordParser.Comparison;
		foreach (Entry entry in chordPro)
		{
			if (entry is not ChordProDirectiveLine directive)
			{
				result.Add(entry);
			}
			else if (directive.ShortName.Equals("sob", Comparison) || directive.ShortName.Equals("eob", Comparison))
			{
				// MobileSheets doesn't support start/end_of_bridge directives as of v3.8.12 (2023-08-15).
				const string LongSuffix = "bridge";
				int suffixLength = directive.Name.EndsWith(LongSuffix, Comparison) ? LongSuffix.Length : 1;
				string newName = directive.Name[0..^suffixLength] + "verse";
				ChordProDirectiveLine transformed = new(newName, directive.Argument);
				result.Add(transformed);
			}
			else if (directive.Name.Equals("tempo", Comparison))
			{
				// MobileSheets doesn't display a {tempo} directive as of v3.8.12 (2023-08-15).
				result.Add(entry);
				result.Add(new ChordProDirectiveLine("comment", $"BPM: {directive.Argument}"));
			}
			else
			{
				result.Add(entry);
			}
		}

		return result;
	}

	#endregion
}
