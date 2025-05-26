namespace Menees.Chords;

#region Using Directives

using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A ChordPro directive in "{name}", "{name[:] argument}", or "{name[:] key='value'...}" format.
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

	private const string DirectiveLinePattern = """(?inx)^\s*\{\s* # Opening brace with optional ws"""
		+ "\n" + """(?<name>\w+?) # meta docs say, "name must be a single word but may include underscores"."""
		+ "\n" + """(\-(?<not>\!)?(?<selector>\w+))? # Operator and selector for conditional directive"""
		+ "\n" + """((\s*:\s*|\s+) # Colon or ws separator is required if args are present"""
		+ "\n" + """(?<argument>.*) # Single argument"""
		+ "\n" + """)?\s*}\s*$ # Closing brace with optional ws""";

	private static readonly Regex DirectiveRegex = new(DirectiveLinePattern, RegexOptions.Compiled);

	private static readonly StringComparer Comparer = ChordParser.Comparer;

	private static readonly Dictionary<string, string> LongNameToShortNameMap = new(Comparer)
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

	private ChordProDirectiveLine(ChordProDirectiveName name, ChordProDirectiveArgs args)
	{
		this.QualifiedName = name;
		this.Arguments = args;

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
	/// Gets the directive's full name including any optional selector and inversion operator.
	/// </summary>
	public ChordProDirectiveName QualifiedName { get; }

	/// <summary>
	/// Gets the directive's simple name without any optional selector or inversion operator.
	/// </summary>
	public string Name => this.QualifiedName.Name;

	/// <summary>
	/// Gets the directive's optional argument (i.e., the part after the separator).
	/// </summary>
	/// <seealso cref="Arguments"/>
	public string? Argument => this.Arguments.Value;

	/// <summary>
	/// Gets the directive's parsed arguments, e.g., key=value attribute pairs in declaration order.
	/// </summary>
	/// <seealso cref="Argument"/>
	public ChordProDirectiveArgs Arguments { get; }

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
		Conditions.RequireNonNull(context);

		ChordProDirectiveLine? result = null;

		Lexer lexer = context.CreateLexer();
		if (lexer.Read(skipLeadingWhiteSpace: true)
			&& lexer.Token.Type == TokenType.Text
			&& lexer.Token.Text[0] == '{')
		{
			string line = lexer.ReadToEnd(skipTrailingWhiteSpace: true);
			Match match;
			if (line.Length > 2 && line[^1] == '}' && (match = DirectiveRegex.Match(line)).Success)
			{
				Group selectorGroup = match.Groups["selector"];
				string? selector = selectorGroup.Success ? selectorGroup.Value : null;
				bool invertSelection = match.Groups["not"].Success;
				ChordProDirectiveName name = new(match.Groups["name"].Value, selector, invertSelection);

				string? argument = null;
				Group argumentGroup = match.Groups[nameof(argument)];
				if (argumentGroup.Success)
				{
					argument = argumentGroup.Value;
				}

				result = Create(name, argument?.Trim());

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
		}

		return result;
	}

	/// <summary>
	/// Converts a chord defintion into a ChordPro {chord} or {define} directive.
	/// </summary>
	/// <param name="definition">The definition to convert.</param>
	/// <param name="inline">Pass false to create a {define} directive.
	/// Pass true to create a {chord} directive to "display the chord
	/// immediately in the song where the directive occurs".</param>
	/// <returns>A new {chord} or {define} directive instance.</returns>
	/// <seealso href="https://www.chordpro.org/chordpro/directives-define/"/>
	public static ChordProDirectiveLine Convert(ChordDefinition definition, bool inline = true)
	{
		Conditions.RequireNonNull(definition);

		IReadOnlyList<byte?> frets = definition.Definition;
		byte baseFret = Math.Max((byte)1, frets.Where(value => value != null).Select(value => value!.Value).Min());

		// {define: NAME base-fret OFFSET frets POS POS … POS}
		StringBuilder arg = new();
		arg.Append(definition.Chord.Name);
		arg.Append(" base-fret ");
		arg.Append(baseFret);
		arg.Append(" frets ");
		arg.AppendJoin(' ', frets.Select(fret => fret?.ToString() ?? "x"));

		string name = inline ? "chord" : "define";
		string argument = arg.ToString();
		ChordProDirectiveLine result = Create(name, argument, false);
		return result;
	}

	/// <summary>
	/// Converts a header line to a pair of ChordPro start/end environment directives.
	/// </summary>
	/// <param name="header">The header to convert.</param>
	/// <param name="preferLongNames">How ChordPro directive names should be converted to text.
	/// If null, then <see cref="Name"/> will be used if available or a long name will be generated.
	/// If true, then <see cref="LongName"/> will be used.
	/// If false, then <see cref="ShortName"/> will be used.</param>
	/// <returns>A pair of new start and end directive instances.</returns>
	/// <seealso href="https://www.chordpro.org/chordpro/chordpro-directives/#environment-directives"/>
	public static (ChordProDirectiveLine Start, ChordProDirectiveLine End) Convert(HeaderLine header, bool? preferLongNames)
	{
		Conditions.RequireNonNull(header);

		const StringComparison comparison = ChordParser.Comparison;
		bool StartsWith(string text)
			=> header.Text.Equals(text, comparison) || header.Text.StartsWith(text + ' ', comparison);

		string suffix = StartsWith("Chorus") ? "chorus"
			: StartsWith("Verse") ? "verse"
			: "bridge";

		string startName = preferLongNames ?? true ? $"start_of_{suffix}" : $"so{suffix[0]}";
		string endName = preferLongNames ?? true ? $"end_of_{suffix}" : $"eo{suffix[0]}";
		string? startArgument = header.Text.Equals(suffix, comparison) ? null : header.Text;

		ChordProDirectiveLine start = Create(startName, startArgument);
		start.AddAnnotations(header.Annotations);
		ChordProDirectiveLine end = Create(endName, null, false);
		return (start, end);
	}

	/// <summary>
	/// Converts a metadata entry to a ChordPro directive.
	/// </summary>
	/// <param name="metadata">The metadata entry to convert.</param>
	/// <returns>A new directive instance using <paramref name="metadata"/>'s name and argument.</returns>
	public static ChordProDirectiveLine Convert(MetadataEntry metadata)
	{
		Conditions.RequireNonNull(metadata);
		ChordProDirectiveLine result = Create(metadata.Name, metadata.Argument);
		return result;
	}

	/// <summary>
	/// Gets the ChordPro directive in {<see cref="LongName"/>} or {<see cref="LongName"/>: <see cref="Argument"/>} format.
	/// </summary>
	public string ToLongString()
		=> this.ToString(this.LongName);

	/// <summary>
	/// Gets the ChordPro directive in {<see cref="ShortName"/>} or {<see cref="ShortName"/>: <see cref="Argument"/>} format.
	/// </summary>
	public string ToShortString()
		=> this.ToString(this.ShortName);

	#endregion

	#region Internal Methods

	internal static ChordProDirectiveLine Create(string name, string? argument, bool tryParseArgument = true)
		=> Create(new ChordProDirectiveName(name), new ChordProDirectiveArgs(argument, tryParseArgument));

	internal static ChordProDirectiveLine Create(ChordProDirectiveName name, string? argument, bool tryParseArgument = true)
		=> Create(name, new ChordProDirectiveArgs(argument, tryParseArgument));

	internal static ChordProDirectiveLine Create(ChordProDirectiveName name, ChordProDirectiveArgs args)
	{
		// Use a factory method here in case we ever want to create derived directive types.
		// Note: All this class's static methods will "leak" into the derived types though,
		// e.g., TryParse will be visible and return the base type. :-(
		ChordProDirectiveLine result = new(name, args);
		return result;
	}

	#endregion

	#region Protected Methods

	/// <summary>
	/// Writes the ChordPro directive in {<see cref="Name"/>} or {<see cref="Name"/>: <see cref="Argument"/>} format.
	/// </summary>
	/// <param name="writer">Used to write the output.</param>
	protected override void WriteWithoutAnnotations(TextWriter writer)
	{
		Conditions.RequireNonNull(writer);
		writer.Write(this.ToString(this.Name));
	}

	#endregion

	#region Private Methods

	private string ToString(string name)
	{
		StringBuilder sb = new();
		sb.Append('{');
		sb.Append(this.QualifiedName.Rename(name));
		if (!string.IsNullOrEmpty(this.Argument))
		{
			sb.Append(": ");
			sb.Append(this.Argument);
		}

		sb.Append('}');

		string result = sb.ToString();
		return result;
	}

	#endregion
}
