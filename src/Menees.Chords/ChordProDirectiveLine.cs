namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A ChordPro directive in "{name}" or "{name: argument}" format.
/// </summary>
/// <seealso href="https://www.chordpro.org/chordpro/chordpro-directives/"/>
public sealed class ChordProDirectiveLine : Entry
{
	#region Internal Constants

	internal const string GridStateKey = nameof(ChordProDirectiveLine) + "." + "Grid";
	internal const string TabStateKey = nameof(ChordProDirectiveLine) + "." + "Tab";

	#endregion

	#region Private Data Members

	private const StringComparison Comparison = ChordParser.Comparison;

	private static readonly Dictionary<string, string> LongNameToShortNameMap = new(ChordParser.Comparer)
	{
		{ "chordfont", "cf" },
		{ "chordsize", "cs" },
		{ "column_break", "colb" },
		{ "columns", "col" },
		{ "comment", "c" },
		{ "comment_box", "cb" },
		{ "comment_italic", "ci" },
		{ "end_of_bridge", "eob" },
		{ "end_of_chorus", "eoc" },
		{ "end_of_grid", "eog" },
		{ "end_of_tab", "eot" },
		{ "end_of_verse", "eov" },
		{ "grid", "g" },
		{ "new_page", "np" },
		{ "new_physical_page", "npp" },
		{ "new_song", "ns" },
		{ "no_grid", "ng" },
		{ "start_of_bridge", "sob" },
		{ "start_of_chorus", "soc" },
		{ "start_of_grid", "sog" },
		{ "start_of_tab", "sot" },
		{ "start_of_verse", "sov" },
		{ "subtitle", "st" },
		{ "textfont", "tf" },
		{ "textsize", "ts" },
		{ "title", "t" },
	};

	private static readonly Dictionary<string, string> ShortNameToLongNameMap = LongNameToShortNameMap
		.ToDictionary(pair => pair.Value, pair => pair.Key, LongNameToShortNameMap.Comparer);

	#endregion

	#region Constructors

	private ChordProDirectiveLine(string name, string? argument)
	{
		this.Name = name;
		this.Argument = argument;

		this.LongName = this.Name;
		this.ShortName = this.Name;
		if (LongNameToShortNameMap.TryGetValue(this.Name, out string? shortName))
		{
			this.ShortName = shortName;
		}
		else if (ShortNameToLongNameMap.TryGetValue(this.Name, out string? longName))
		{
			this.LongName = longName;
		}
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the directive's name.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets the directive's optional argument (i.e., the part after the colon separator).
	/// </summary>
	public string? Argument { get; }

	/// <summary>
	/// Gets the directive's long name form or <see cref="Name"/>.
	/// </summary>
	public string LongName { get; }

	/// <summary>
	/// Gets the directive's short name form or <see cref="Name"/>.
	/// </summary>
	public string ShortName { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse the current line as a ChordPro directive line.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if the line is in "{name}" or "{name: argument}" format.</returns>
	public static ChordProDirectiveLine? TryParse(LineContext context)
	{
		ChordProDirectiveLine? result = null;

		string line = context.LineText.Trim();
		if (line.Length > 2 && line[0] == '{' && line[^1] == '}')
		{
			string name;
			string? argument;
			int colonIndex = line.IndexOf(':');
			if (colonIndex >= 0)
			{
				name = line[1..colonIndex];
				argument = line[(colonIndex + 1)..^1];
			}
			else
			{
				name = line[1..^1];
				argument = null;
			}

			result = new(name.Trim(), argument?.Trim());

			// Push/pop the current grid or tab state to make parsing simpler for
			// ChordProGridLine and TablatureLine.
			if (result.ShortName.Equals("sog", Comparison))
			{
				context.State[GridStateKey] = result;
			}
			else if (result.ShortName.Equals("eog", Comparison))
			{
				context.State.Remove(GridStateKey);
			}
			else if (result.ShortName.Equals("sot", Comparison))
			{
				context.State[TabStateKey] = result;
			}
			else if (result.ShortName.Equals("eot", Comparison))
			{
				context.State.Remove(TabStateKey);
			}
		}

		return result;
	}

	/// <summary>
	/// Gets the ChordPro directive in {<see cref="Name"/>} or {<see cref="Name"/>: <see cref="Argument"/>} format.
	/// </summary>
	public override string ToString()
		=> "{" + this.Name + (string.IsNullOrEmpty(this.Argument) ? string.Empty : (": " + this.Argument)) + "}";

	#endregion
}
