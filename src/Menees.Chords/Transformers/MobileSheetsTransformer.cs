namespace Menees.Chords.Transformers;

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
	#region Private Data Members

	private const StringComparison Comparison = ChordParser.Comparison;

	#endregion

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
				supportedInput.Add(ChordProDirectiveLine.Create("comment", definitions.ToString()));
			}
		}

		IReadOnlyList<Entry> chordPro = base.TransformEntries(supportedInput);
		List<Entry> result = new(chordPro.Count);

		foreach (Entry entry in chordPro)
		{
			if (entry is ChordProGridLine grid)
			{
				// MobileSheets pre-v3.9.0 tried to render chord grid lines with chords on a line above the separators,
				// so we'll convert all the separators to ChordPro annotations to get them on the same (upper) line.
				// In v3.9.0, MobileSheets chord grid support auto-sizes to the page width, which is still undesirable.
				List<TextSegment> brackted = [.. grid.Segments
					.Select(s => s is ChordSegment c ? new ChordSegment(c.Chord, $"[{c.Text}]")
						: s is WhiteSpaceSegment ws ? s
						: new TextSegment($"[*{s.Text}]"))];
				result.Add(new ChordLine(brackted));
			}
			else if (entry is not ChordProDirectiveLine directive)
			{
				result.Add(entry);
			}
			else if (directive.ShortName.Equals("sob", Comparison) || directive.ShortName.Equals("eob", Comparison))
			{
				// MobileSheets doesn't support start/end_of_bridge directives as of v3.8.12 (2023-08-15).
				// MobileSheets won't show a header at all for {sov}, so we'll make it {sov: Bridge}.
				ChordProDirectiveLine transformed = ReplaceWithVerseDirective(directive, "bridge", "Bridge");
				result.Add(transformed);
			}
			else if (directive.ShortName.Equals("sov", Comparison) && string.IsNullOrEmpty(directive.Argument))
			{
				// MobileSheets won't show a header at all for {sov}, so we'll make it {sov: Verse}.
				ChordProDirectiveLine transformed = ChordProDirectiveLine.Create(directive.Name, "Verse");
				result.Add(transformed);
			}
			else if (directive.ShortName.Equals("sog", Comparison) || directive.ShortName.Equals("eog", Comparison))
			{
				// MobileSheets v3.9.0 added support for chord grids, but they auto-size to the page width,
				// so it'll only show one column per page. Yuck! https://zubersoft.com/mobilesheets/forum/thread-12271.html
				ChordProDirectiveLine transformed = ReplaceWithVerseDirective(directive, "grid");
				result.Add(transformed);
			}
			else
			{
				result.Add(entry);
			}
		}

		return result;
	}

	#endregion

	#region Private Methods

	private static ChordProDirectiveLine ReplaceWithVerseDirective(ChordProDirectiveLine directive, string longSuffix, string? defaultArgument = null)
	{
		int suffixLength = directive.Name.EndsWith(longSuffix, Comparison) ? longSuffix.Length : 1;
		string newName = directive.Name[0..^suffixLength] + (suffixLength == 1 ? "v" : "verse");

		string? argument = directive.Name.StartsWith("s", Comparison) && string.IsNullOrEmpty(directive.Argument)
			? defaultArgument : directive.Argument;
		ChordProDirectiveLine result = ChordProDirectiveLine.Create(newName, argument);
		return result;
	}

	#endregion
}
