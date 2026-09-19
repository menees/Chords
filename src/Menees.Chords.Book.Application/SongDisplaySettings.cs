#region Using Directives

using Menees.Chords.Db;
using Menees.Chords.Formatters;
using Menees.Chords.Formatters.Html;
using Menees.Chords.Transformers;

#endregion

namespace Menees.Chords.Book.Application;

public static class SongDisplaySettings
{
	#region Public Methods

	public static DisplayProfile Resolve(DisplayProfile defaults, DisplayOverride? patch) => new()
	{
		Theme = patch?.Theme ?? defaults.Theme,
		FontSize = patch?.FontSize ?? defaults.FontSize,
		LineSpacing = patch?.LineSpacing ?? defaults.LineSpacing,
		Columns = patch?.Columns ?? defaults.Columns,
		AutoColumns = patch?.AutoColumns ?? defaults.AutoColumns,
		ShowChords = patch?.ShowChords ?? defaults.ShowChords,
		NotationSystem = patch?.NotationSystem ?? defaults.NotationSystem,
	};

	public static void Validate(DisplayProfile profile)
	{
		ArgumentNullException.ThrowIfNull(profile);
		const int MinimumSize = 8;
		const int MaximumSize = 72;
		const int MaximumColumns = 4;
		const double MinimumSpacing = 0.5;
		const double MaximumSpacing = 3;
		if (!double.IsFinite(profile.FontSize) || profile.FontSize is < MinimumSize or > MaximumSize
			|| !double.IsFinite(profile.LineSpacing) || profile.LineSpacing is < MinimumSpacing or > MaximumSpacing
			|| profile.Columns is < 1 or > MaximumColumns || profile.Theme is not ("Default" or "Light" or "Dark")
			|| profile.NotationSystem is not ("Letter" or "Nashville" or "Roman"))
		{
			throw new ArgumentException("Use 8–72 px text, 0.5–3 spacing, 1–4 columns, Default/Light/Dark theme and Letter/Nashville/Roman notation.");
		}
	}

	public static DisplayOverride CreatePatch(DisplayProfile profile, DisplayProfile defaults) => new()
	{
		Theme = profile.Theme == defaults.Theme ? null : profile.Theme,
		FontSize = profile.FontSize == defaults.FontSize ? null : profile.FontSize,
		LineSpacing = profile.LineSpacing == defaults.LineSpacing ? null : profile.LineSpacing,
		Columns = profile.Columns == defaults.Columns ? null : profile.Columns,
		AutoColumns = profile.AutoColumns == defaults.AutoColumns ? null : profile.AutoColumns,
		ShowChords = profile.ShowChords == defaults.ShowChords ? null : profile.ShowChords,
		NotationSystem = profile.NotationSystem == defaults.NotationSystem ? null : profile.NotationSystem,
	};

	public static string RenderPreview(DisplayProfile profile)
	{
		const string Sample = "{key: C}\n{start_of_verse: Verse}\n[C]Sample words with [F]chords above\n"
			+ "[G7]Second line shows the [C]spacing\n{end_of_verse}";
		return Render(Document.Parse(Sample), profile);
	}

	public static string Render(Document document, DisplayProfile profile, bool responsivePages = true)
	{
		Validate(profile);
		if (profile.NotationSystem != "Letter")
		{
			Notation notation = profile.NotationSystem == "Nashville" ? Notation.Nashville : Notation.Roman;
			document = new NotationTransformer(document, notation).Transform().Document;
		}

		HtmlFormatterOptions options = new() { LineSpacing = profile.LineSpacing, ResponsivePages = responsivePages };
		options.DefaultTextStyle.FontSize = CssSize.Parse(FormattableString.Invariant($"{profile.FontSize}px"));
		string theme = profile.Theme switch { "Light" => "light", "Dark" => "dark", _ => "light dark" };
		int maximumColumns = profile.AutoColumns ? 0 : profile.Columns;
		string css = FormattableString.Invariant($"<style>:root {{color-scheme:{theme}; --maximum-columns:{maximumColumns};}}");
		if (!profile.ShowChords)
		{
			css += ".chord,.chord-only-line,.chord-diagram,.compact-chord-diagram {display:none !important;}";
		}

		css += "</style>";
		return new HtmlFormatter(document, options).ToString().Replace("</head>", css + "</head>", StringComparison.Ordinal);
	}

	#endregion
}
