namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

/// <summary>Describes a paired ChordPro environment contained by a <see cref="Section"/>.</summary>
internal sealed class ChordProEnvironment
{
	#region Internal Constants

	internal const string BridgeName = "bridge";
	internal const string ChorusName = "chorus";
	internal const string EndPrefix = "end_of_";
	internal const string GridName = "grid";
	internal const string StartPrefix = "start_of_";
	internal const string TabName = "tab";
	internal const string VerseName = "verse";

	#endregion

	#region Private Data Members

	private const string AbcName = "abc";
	private const string LilyPondName = "ly";
	private const string SvgName = "svg";
	private const string TextBlockName = "textblock";

	#endregion

	#region Constructors

	private ChordProEnvironment(string name, ChordProDirectiveLine start, ChordProDirectiveLine? end)
	{
		this.Name = name;
		this.Start = start;
		this.End = end;
		this.Kind = GetKind(name);
	}

	#endregion

	#region Internal Properties

	/// <summary>Gets the matching end directive, or null if the environment was not closed.</summary>
	internal ChordProDirectiveLine? End { get; }

	/// <summary>Gets whether this environment delegates its opaque content to a specialized renderer.</summary>
	internal bool IsDelegated => IsDelegatedKind(this.Kind);

	/// <summary>Gets the environment's built-in kind.</summary>
	internal ChordProEnvironmentKind Kind { get; }

	/// <summary>Gets the optional environment label.</summary>
	internal string? Label => this.Start.Args.Attributes.TryGetValue("label", out string? label)
		? label
		: this.Start.Args.Attributes.Count == 0 ? this.Start.Argument : null;

	/// <summary>Gets the environment name without the <c>start_of_</c> prefix.</summary>
	internal string Name { get; }

	/// <summary>Gets the opening directive.</summary>
	internal ChordProDirectiveLine Start { get; }

	#endregion

	#region Internal Methods

	internal static ChordProEnvironment? TryCreate(IReadOnlyList<Entry> entries)
	{
		ChordProEnvironment? result = null;
		if (entries.Count > 0 && entries[0] is ChordProDirectiveLine start
			&& start.LongName.StartsWith(StartPrefix, ChordParser.Comparison))
		{
			string name = start.LongName.Substring(StartPrefix.Length);
			ChordProDirectiveLine? end = entries[^1] as ChordProDirectiveLine;
			if (end is not null && !end.LongName.Equals(EndPrefix + name, ChordParser.Comparison))
			{
				end = null;
			}

			result = new(name, start, end);
		}

		return result;
	}

	internal static bool IsChorus(string? environmentName)
		=> environmentName?.Equals(ChorusName, ChordParser.Comparison) == true;

	internal static bool TryGetDelegatedName(string directiveName, out string? environmentName)
	{
		environmentName = null;
		if (directiveName.StartsWith(StartPrefix, ChordParser.Comparison))
		{
			string candidate = directiveName.Substring(StartPrefix.Length);
			if (IsDelegatedKind(GetKind(candidate)))
			{
				environmentName = candidate;
			}
		}

		return environmentName is not null;
	}

	/// <summary>Gets the environment name produced when a section header is converted to ChordPro.</summary>
	internal static string GetHeaderEnvironmentName(HeaderLine header)
	{
		(ChordProDirectiveLine start, _) = ChordProDirectiveLine.Convert(header, preferLongNames: true);
		return start.LongName.Substring(StartPrefix.Length);
	}

	#endregion

	#region Private Methods

	private static ChordProEnvironmentKind GetKind(string name)
		=> name.ToLowerInvariant() switch
		{
			BridgeName => ChordProEnvironmentKind.Bridge,
			ChorusName => ChordProEnvironmentKind.Chorus,
			GridName => ChordProEnvironmentKind.Grid,
			TabName => ChordProEnvironmentKind.Tab,
			VerseName => ChordProEnvironmentKind.Verse,
			AbcName => ChordProEnvironmentKind.Abc,
			LilyPondName => ChordProEnvironmentKind.LilyPond,
			SvgName => ChordProEnvironmentKind.Svg,
			TextBlockName => ChordProEnvironmentKind.TextBlock,
			_ => ChordProEnvironmentKind.Generic,
		};

	private static bool IsDelegatedKind(ChordProEnvironmentKind kind)
		=> kind is ChordProEnvironmentKind.Abc
			or ChordProEnvironmentKind.LilyPond
			or ChordProEnvironmentKind.Svg
			or ChordProEnvironmentKind.TextBlock;

	#endregion
}
