namespace Menees.Chords.Formatters;

#region Using Directives

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;
using Menees.Chords.Formatters.Html;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// Formats an <see cref="IEntryContainer"/> as a standalone HTML chord sheet.
/// </summary>
public sealed class HtmlFormatter : ContainerFormatter
{
	#region Private Data Members

	private const string DefaultScriptResourceName = "Menees.Chords.Formatters.Html.Formatter.js";
	private const string DefaultStylesResourceName = "Menees.Chords.Formatters.Html.Formatter.css";

	private static readonly Lazy<string> DefaultScript = new(() => LoadEmbeddedResource(DefaultScriptResourceName));
	private static readonly Lazy<string> DefaultStyles = new(() => LoadEmbeddedResource(DefaultStylesResourceName));

	private static readonly HashSet<string> VoidElementNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
	};

	private static readonly HashSet<string> ChordDiagramKeywords = new(ChordParser.Comparer)
	{
		"base-fret", "base_fret", "copy", "copyall", "diagram", "display", "fingers", "format", "frets", "keys",
	};

	private readonly Dictionary<string, ChordDiagram> chordDiagrams = new(ChordParser.Comparer);
	private readonly HtmlFormatterOptions? options;
	private readonly Stack<TransposeSetting> transposeSettings = new();
	private XDocument? document;
	private XElement? currentContainer;
	private XElement? lastChorus;

	#endregion

	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="container">The container to format.</param>
	public HtmlFormatter(IEntryContainer container)
		: this(container, null)
	{
	}

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="container">The container to format.</param>
	/// <param name="options">The optional default-style overrides.</param>
	public HtmlFormatter(IEntryContainer container, HtmlFormatterOptions? options)
		: base(container)
	{
		this.options = options;
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Serializes a formatted HTML document without adding whitespace between inline elements.
	/// </summary>
	/// <param name="document">The document to serialize.</param>
	/// <returns>The HTML text.</returns>
	public static string Serialize(XDocument document)
	{
		Conditions.RequireNonNull(document);
		StringBuilder result = new();
		foreach (XNode node in document.Nodes())
		{
			AppendNode(result, node);
		}

		return result.ToString();
	}

	/// <inheritdoc/>
	public override string ToString()
	{
		this.EnsureDocument();
		return Serialize(this.document);
	}

	/// <summary>
	/// Gets the formatted HTML document.
	/// </summary>
	/// <returns>A cloned document that callers can safely customize.</returns>
	public XDocument ToXDocument()
	{
		this.EnsureDocument();
		return new(this.document);
	}

	#endregion

	#region Protected Methods

	/// <inheritdoc/>
	protected override bool ShouldFormatChildren(IEntryContainer container, IReadOnlyCollection<IEntryContainer> hierarchy)
	{
		base.ShouldFormatChildren(container, hierarchy);
		return container is not ChordLyricPair
			&& container is not Section { Environment.IsDelegated: true };
	}

	/// <inheritdoc/>
	protected override void Format(Entry entry, IReadOnlyCollection<IEntryContainer> hierarchy)
	{
		if (this.currentContainer is null && entry is IEntryContainer rootContainer)
		{
			this.InitializeDocument(rootContainer);
		}

		Conditions.RequireNonNull(this.currentContainer);

		XElement? element = null;
		IReadOnlyList<Entry> annotations = entry.Annotations;
		switch (entry)
		{
			case Section { Environment.IsDelegated: true } section:
				element = FormatDelegatedEnvironment(section);
				break;

			case BlankLine:
				element = Element("div", "blank-line", "\u00A0");
				element.SetAttributeValue("aria-hidden", "true");
				break;

			case ChordLyricPair pair:
				ChordProLyricLine converted = ChordProLyricLine.Convert(pair);
				element = FormatChordProLyricLine(converted, normalizeTrailingChords: true, this.Transpose);
				annotations = converted.Annotations;
				break;

			case ChordProLyricLine chordProLyrics:
				element = FormatChordProLyricLine(chordProLyrics, normalizeTrailingChords: false, this.Transpose);
				break;

			case TitleLine title:
				element = FormatTitle(title);
				break;

			case MetadataEntry metadata:
				element = FormatMetadata(metadata.Name, metadata.Argument);
				break;

			case HeaderLine header:
				element = Element("h2", "section-header", header.Text);
				break;

			case Comment comment:
				if (comment.Prefix?.TrimStart().StartsWith('#') != true)
				{
					element = Element("aside", "comment", comment.Text);
					element.SetAttributeValue("role", "note");
				}

				break;

			case ChordProRemarkLine:
				break;

			case ChordProDirectiveLine directive:
				element = this.FormatDirective(directive);
				break;

			case ChordDefinitions definitions:
				element = this.FormatChordDefinitions(definitions.Definitions);
				break;

			case ChordProGridLine grid:
				element = FormatSegments(grid.Segments, "div", "grid-line", "grid-chord", this.Transpose);
				break;

			case ChordLine chords:
				element = FormatSegments(chords.Segments, "div", "chord-only-line", "chord-only", this.Transpose);
				break;

			case LyricLine lyrics:
				element = Element("div", "lyric-line", lyrics.Text);
				break;

			case TablatureLine tablature:
				element = Element("pre", "tablature-line", tablature.Text);
				break;

			case UriLine uri:
				element = FormatUri(uri);
				break;

			default:
				element = Element("div", $"entry {GetEntryClass(entry)}", entry.ToString(includeAnnotations: false));
				break;
		}

		if (element is not null)
		{
			AddAnnotations(element, annotations);
			this.currentContainer.Add(element);
		}
	}

	/// <inheritdoc/>
	protected override void BeginContainer(IEntryContainer container, IReadOnlyCollection<IEntryContainer> hierarchy)
	{
		base.BeginContainer(container, hierarchy);

		if (this.document is null)
		{
			this.InitializeDocument(container);
		}
		else
		{
			Conditions.RequireNonNull(this.currentContainer);
			string className = "section";
			string? environmentName = container is Section section ? GetEnvironmentName(section) : null;
			if (environmentName is not null)
			{
				className += $" environment environment-{environmentName.Replace('_', '-')}";
			}

			XElement child = new("section", new XAttribute("class", className));
			child.SetAttributeValue("data-container", container.GetType().Name);
			child.SetAttributeValue("data-environment", environmentName);
			this.currentContainer.Add(child);
			this.currentContainer = child;
		}
	}

	/// <inheritdoc/>
	protected override void EndContainer(IEntryContainer container, IReadOnlyCollection<IEntryContainer> hierarchy)
	{
		base.EndContainer(container, hierarchy);
		Conditions.RequireNonNull(this.currentContainer);

		if (container is Entry entry)
		{
			AddAnnotations(this.currentContainer, entry.Annotations);
		}

		if (hierarchy.Count > 0)
		{
			XElement completed = this.currentContainer;
			if (container is Section section && IsChorus(section))
			{
				this.lastChorus = new(completed);
			}

			this.currentContainer = completed.Parent;
			Conditions.RequireNonNull(this.currentContainer);
			if (!completed.Nodes().Any())
			{
				completed.Remove();
			}
		}
	}

	#endregion

	#region Private Methods

	private static void AppendElement(StringBuilder builder, XElement element)
	{
		string name = element.Name.LocalName;
		builder.Append('<');
		builder.Append(name);
		foreach (XAttribute attribute in element.Attributes())
		{
			builder.Append(' ');
			builder.Append(attribute);
		}

		if (VoidElementNames.Contains(name))
		{
			builder.Append(" />");
		}
		else
		{
			builder.Append('>');
			foreach (XNode node in element.Nodes())
			{
				AppendNode(builder, node);
			}

			builder.Append("</");
			builder.Append(name);
			builder.Append('>');
		}
	}

	private static void AppendNode(StringBuilder builder, XNode node)
	{
		switch (node)
		{
			case XDocumentType:
				builder.Append("<!DOCTYPE html>");
				break;

			case XElement element:
				AppendElement(builder, element);
				break;

			case XText text when text.Parent?.Name.LocalName is "style" or "script":
				builder.Append(text.Value);
				break;

			default:
				builder.Append(node.ToString(SaveOptions.DisableFormatting));
				break;
		}
	}

	private static void AddAnnotations(XElement element, IReadOnlyList<Entry> annotations)
	{
		foreach (Entry annotation in annotations)
		{
			XElement annotationElement = Element("span", "annotation", annotation.ToString(includeAnnotations: false));
			annotationElement.SetAttributeValue("role", "note");
			element.Add(annotationElement);
		}
	}

	private static XElement Element(string name, string className, object content)
		=> new(name, new XAttribute("class", className), content);

	private static XElement? FormatDelegatedEnvironment(Section section)
	{
		ChordProEnvironment environment = section.Environment!;
		return environment.Kind switch
		{
			ChordProEnvironmentKind.Svg => FormatSvgEnvironment(section, environment),
			ChordProEnvironmentKind.TextBlock => FormatTextBlockEnvironment(section, environment),
			_ => null,
		};
	}

	private static XElement FormatSvgEnvironment(Section section, ChordProEnvironment environment)
	{
		string source = GetDelegatedSource(section, environment);
		string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(source));
		XElement image = new(
			"img",
			new XAttribute("class", "delegated-svg"),
			new XAttribute("src", "data:image/svg+xml;base64," + base64),
			new XAttribute("alt", environment.Label ?? "Embedded SVG"));
		XElement result = CreateDelegatedElement(environment, image);
		ApplyAlignment(result, environment.Start.Args.Attributes);
		return result;
	}

	private static XElement FormatTextBlockEnvironment(Section section, ChordProEnvironment environment)
	{
		XElement content = Element("pre", "textblock-content", GetDelegatedSource(section, environment));
		XElement result = CreateDelegatedElement(environment, content);
		IReadOnlyDictionary<string, string> attributes = environment.Start.Args.Attributes;
		ApplyAlignment(result, attributes);
		List<string> styles = [];
		AppendTextBlockSize(styles, attributes, "width", "inline-size");
		AppendTextBlockSize(styles, attributes, "height", "block-size");
		AppendTextBlockSize(styles, attributes, "padding", "padding");
		AppendTextBlockSize(styles, attributes, "textsize", "font-size");
		AppendTextBlockColor(styles, attributes, "textcolor", "color");
		AppendTextBlockColor(styles, attributes, "background", "background-color");
		if (attributes.TryGetValue("flush", out string? flush)
			&& flush is "left" or "center" or "right")
		{
			styles.Add("text-align: " + flush);
		}

		if (attributes.TryGetValue("textspacing", out string? spacing))
		{
			if (spacing.Equals("flex", StringComparison.OrdinalIgnoreCase))
			{
				styles.Add("line-height: normal");
			}
			else if (double.TryParse(spacing, NumberStyles.Float, CultureInfo.InvariantCulture, out double lineHeight)
				&& lineHeight > 0)
			{
				styles.Add("line-height: " + lineHeight.ToString(CultureInfo.InvariantCulture));
			}
		}

		if (styles.Count > 0)
		{
			content.SetAttributeValue("style", string.Join("; ", styles));
		}

		return result;
	}

	private static XElement CreateDelegatedElement(ChordProEnvironment environment, XElement content)
	{
		string name = environment.Name.Replace('_', '-');
		XElement result = new(
			"section",
			new XAttribute("class", $"section environment environment-{name} delegated-environment"),
			new XAttribute("data-environment", environment.Name));
		string? label = environment.Label;
		if (label is not null && !string.IsNullOrWhiteSpace(label))
		{
			result.Add(Element("h2", $"section-header {name}", label));
		}

		result.Add(content);
		return result;
	}

	private static string GetDelegatedSource(Section section, ChordProEnvironment environment)
	{
		int count = section.Entries.Count - 1 - (environment.End is null ? 0 : 1);
		IEnumerable<string> lines = section.Entries.Skip(1).Take(count).Select(entry => entry.ToString(includeAnnotations: false));
		return string.Join(System.Environment.NewLine, lines);
	}

	private static void ApplyAlignment(XElement element, IReadOnlyDictionary<string, string> attributes)
	{
		if (attributes.TryGetValue("align", out string? align)
			&& align is "left" or "center" or "right")
		{
			element.SetAttributeValue("class", (string)element.Attribute("class")! + " align-" + align);
		}
	}

	private static void AppendTextBlockSize(
		List<string> styles,
		IReadOnlyDictionary<string, string> attributes,
		string attributeName,
		string propertyName)
	{
		if (attributes.TryGetValue(attributeName, out string? value))
		{
			CssSize? size = CssSize.TryParse(value, out CssSize? parsed)
				? parsed
				: double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double points)
					? CssSize.Points(points)
					: null;
			if (size is not null)
			{
				styles.Add(propertyName + ": " + size);
			}
		}
	}

	private static void AppendTextBlockColor(
		List<string> styles,
		IReadOnlyDictionary<string, string> attributes,
		string attributeName,
		string propertyName)
	{
		if (attributes.TryGetValue(attributeName, out string? value) && CssColor.TryParse(value, out CssColor? color))
		{
			styles.Add(propertyName + ": " + color);
		}
	}

	private static XElement FormatChordProLyricLine(
		ChordProLyricLine line,
		bool normalizeTrailingChords,
		Func<Chord, Chord> transpose)
	{
		XElement result = new("div", new XAttribute("class", "chord-line"));
		List<RenderToken> tokens = [];
		foreach (TextSegment segment in line.Segments)
		{
			if (segment is ChordSegment chord)
			{
				tokens.Add(new(transpose(chord.Chord)));
			}
			else if (segment is ChordAnnotationSegment annotation)
			{
				tokens.Add(new(annotation));
			}
			else
			{
				string text = segment.Text;
				int start = 0;
				while (start < text.Length)
				{
					bool whiteSpace = char.IsWhiteSpace(text[start]);
					int end = start + 1;
					while (end < text.Length && char.IsWhiteSpace(text[end]) == whiteSpace)
					{
						end++;
					}

					tokens.Add(new(text.Substring(start, end - start), whiteSpace));
					start = end;
				}
			}
		}

		if (normalizeTrailingChords)
		{
			NormalizeTrailingChords(tokens);
		}

		List<WordRun> words = [];
		WordRun? word = null;
		foreach (RenderToken token in tokens)
		{
			if (!token.IsWhiteSpace)
			{
				word ??= new();
				word.Tokens.Add(token);
			}
			else if (word is null)
			{
				result.Add(new XText(token.Text));
			}
			else
			{
				word.SeparatorAfter += token.Text;
				words.Add(word);
				word = null;
			}
		}

		if (word is not null)
		{
			words.Add(word);
		}

		MergeFollowingUnchordedWords(words);

		foreach (WordRun current in words)
		{
			if (current.Tokens.All(token => !token.IsAboveLyric))
			{
				result.Add(new XText(string.Concat(current.Tokens.Select(token => token.Text))));
			}
			else
			{
				XElement wordElement = new("span", new XAttribute("class", "word"));
				for (int index = 0; index < current.Tokens.Count; index++)
				{
					RenderToken token = current.Tokens[index];
					if (!token.IsAboveLyric)
					{
						wordElement.Add(new XText(token.Text));
					}
					else
					{
						string lyric = string.Empty;
						while ((index + 1) < current.Tokens.Count && !current.Tokens[index + 1].IsAboveLyric)
						{
							lyric += current.Tokens[++index].Text;
						}

						XElement chordElement = Element(
							"span",
							token.IsAnnotation ? "chord chord-annotation" : "chord",
							token.Text);
						chordElement.SetAttributeValue("aria-hidden", token.IsAnnotation ? null : "true");
						chordElement.SetAttributeValue("role", token.IsAnnotation ? "note" : null);
						XElement lyricElement = Element("span", "lyric", lyric.Length == 0 ? "\u00A0" : lyric);
						wordElement.Add(Element("span", "chord-lyric", new object[] { chordElement, lyricElement }));
					}
				}

				result.Add(wordElement);
			}

			if (current.SeparatorAfter.Length > 0)
			{
				result.Add(new XText(current.SeparatorAfter));
			}
		}

		return result;
	}

	private static void MergeFollowingUnchordedWords(List<WordRun> words)
	{
		// Everything up to the next chord belongs to the current chord's lyric region.
		// Keeping the whole region in one cell lets short syllables (e.g., Em over "If")
		// use the chord's otherwise empty width without relying on font-specific guesses.
		for (int index = 0; index < words.Count; index++)
		{
			WordRun current = words[index];
			while (current.HasAboveLyric && (index + 1) < words.Count && !words[index + 1].HasAboveLyric)
			{
				current.Append(words[index + 1]);
				words.RemoveAt(index + 1);
			}
		}
	}

	private static void NormalizeTrailingChords(List<RenderToken> tokens)
	{
		for (int index = 1; index < tokens.Count; index++)
		{
			RenderToken token = tokens[index];
			if (token.IsAboveLyric
				&& !tokens[index - 1].IsWhiteSpace
				&& !tokens[index - 1].IsAboveLyric
				&& ((index + 1) == tokens.Count || tokens[index + 1].IsWhiteSpace))
			{
				tokens.RemoveAt(index);
				tokens.Insert(index - 1, token);
			}
		}
	}

	private XElement? FormatDirective(ChordProDirectiveLine directive)
	{
		const StringComparison Comparison = ChordParser.Comparison;
		string name = directive.LongName;
		string? argument = directive.Argument;
		XElement? result;

		if (name.Equals("title", Comparison))
		{
			result = Element("h1", "title", argument ?? string.Empty);
		}
		else if (name.Equals("subtitle", Comparison) || name.Equals("artist", Comparison))
		{
			result = Element("div", $"subtitle {name}", argument ?? string.Empty);
		}
		else if (name.Equals("comment", Comparison)
			|| name.Equals("comment_italic", Comparison)
			|| name.Equals("comment_box", Comparison))
		{
			string className = name.Equals("comment", Comparison) ? "comment" : $"comment {name.Replace('_', '-')}";
			result = Element("aside", className, argument ?? string.Empty);
			result.SetAttributeValue("role", "note");
		}
		else if (name.Equals(ChordProEnvironment.ChorusName, Comparison))
		{
			result = this.FormatChorusRecall(directive);
		}
		else if (name.Equals("define", Comparison) || name.Equals("chord", Comparison))
		{
			ChordDiagram? diagram = this.ParseChordDiagram(directive);
			result = diagram?.Show == true ? FormatChordDiagrams([diagram]) : null;
		}
		else if (name.StartsWith(ChordProEnvironment.StartPrefix, Comparison))
		{
			string suffix = name.Substring(ChordProEnvironment.StartPrefix.Length);
			if (suffix.Equals(ChordProEnvironment.GridName, Comparison)
				|| suffix.Equals(ChordProEnvironment.TabName, Comparison))
			{
				result = null;
			}
			else
			{
				string label = directive.Args.Attributes.TryGetValue("label", out string? explicitLabel)
					? explicitLabel
					: directive.Args.Attributes.Count == 0 && argument is not null
						? argument
						: TitleLine.ToTitleCase(suffix.Replace('_', ' '));
				result = Element("h2", $"section-header {suffix.Replace('_', '-')}", label);
			}
		}
		else if (name.StartsWith(ChordProEnvironment.EndPrefix, Comparison))
		{
			result = null;
		}
		else if (name.Equals("new_page", Comparison) || name.Equals("new_physical_page", Comparison))
		{
			result = Element("div", "page-break", "\u00A0");
			result.SetAttributeValue("aria-hidden", "true");
		}
		else if (name.Equals("column_break", Comparison))
		{
			result = Element("div", "column-break", "\u00A0");
			result.SetAttributeValue("aria-hidden", "true");
		}
		else if (name.Equals("transpose", Comparison))
		{
			this.UpdateTranspose(argument);
			result = null;
		}
		else if (MetadataEntry.TryParse(directive) is MetadataEntry metadata)
		{
			result = FormatMetadata(metadata.Name, metadata.Argument);
		}
		else
		{
			result = Element("div", "directive", directive.ToString(includeAnnotations: false));
			result.SetAttributeValue("data-directive", name);
		}

		return result;
	}

	#pragma warning disable SA1204 // Static diagram helpers are kept next to the instance directive formatter that uses them.
	#pragma warning disable MEN010 // SVG coordinates are clearer as drawing literals than as dozens of one-use constants.
	private static XElement FormatChordDiagram(ChordDiagram diagram)
	{
		XNamespace svg = "http://www.w3.org/2000/svg";
		XElement image = new(
			svg + "svg",
			new XAttribute("role", "img"),
			new XAttribute("aria-label", $"{diagram.DisplayName} chord diagram"));

		if (diagram.Keys is not null)
		{
			image.SetAttributeValue("viewBox", "0 0 90 82");
			FormatKeyboardDiagram(image, svg, diagram.Keys);
		}
		else if (diagram.Frets is not null)
		{
			FormatFretDiagram(image, svg, diagram);
		}

		return new XElement(
			"figure",
			new XAttribute("class", "chord-diagram"),
			Element("figcaption", "chord-diagram-name", diagram.DisplayName),
			image);
	}

	private static XElement FormatChordDiagrams(IEnumerable<ChordDiagram> diagrams)
		=> new("div", new XAttribute("class", "chord-diagrams"), diagrams.Select(FormatChordDiagram));

	private static void FormatFretDiagram(XElement image, XNamespace svg, ChordDiagram diagram)
	{
		IReadOnlyList<int?> frets = diagram.Frets!;
		int fretCount = Math.Max(1, frets.Where(fret => fret > 0).DefaultIfEmpty(0).Max() ?? 0);
		const double Left = 16;
		const double Right = 84;
		const double Top = 18;
		const double FretHeight = 14;
		double bottom = Top + (fretCount * FretHeight);
		image.SetAttributeValue("viewBox", $"0 0 90 {bottom + 4}");

		for (int fret = 0; fret <= fretCount; fret++)
		{
			double y = Top + (fret * FretHeight);
			string className = fret == 0 && diagram.BaseFret == 1 ? "diagram-line diagram-nut" : "diagram-line";
			image.Add(SvgLine(svg, Left, y, Right, y, className));
		}

		for (int index = 0; index < frets.Count; index++)
		{
			double x = frets.Count == 1 ? 50 : Left + ((Right - Left) * index / (frets.Count - 1));
			image.Add(SvgLine(svg, x, Top, x, bottom, "diagram-line"));
			int? position = frets[index];
			if (position is null || position == 0)
			{
				image.Add(SvgText(svg, x, Top - 4, position is null ? "×" : "○", "diagram-text diagram-string-state"));
			}
			else
			{
				double y = Top + ((position.Value - 0.5) * FretHeight);
				image.Add(new XElement(
					svg + "circle",
					new XAttribute("class", "diagram-dot"),
					new XAttribute("cx", x),
					new XAttribute("cy", y),
					new XAttribute("r", 5)));
				if (diagram.Fingers is not null && index < diagram.Fingers.Count && diagram.Fingers[index] is int finger)
				{
					image.Add(SvgText(svg, x, y + 2.8, finger.ToString(), "diagram-finger"));
				}
			}
		}

		if (diagram.BaseFret > 1)
		{
			image.Add(SvgText(svg, 7, Top + 10, diagram.BaseFret.ToString(), "diagram-text"));
		}
	}

	private static void FormatKeyboardDiagram(XElement image, XNamespace svg, IReadOnlyList<int> keys)
	{
		int[] whiteNotes = [0, 2, 4, 5, 7, 9, 11];
		HashSet<int> selected = [.. keys.Select(key => ((key % 12) + 12) % 12)];
		const double WhiteWidth = 11;
		for (int index = 0; index < whiteNotes.Length; index++)
		{
			string className = selected.Contains(whiteNotes[index]) ? "diagram-key selected" : "diagram-key";
			image.Add(new XElement(
				svg + "rect",
				new XAttribute("class", className),
				new XAttribute("x", 7 + (index * WhiteWidth)),
				new XAttribute("y", 8),
				new XAttribute("width", WhiteWidth),
				new XAttribute("height", 68)));
		}

		(int Note, int WhiteIndex)[] blackNotes = [(1, 0), (3, 1), (6, 3), (8, 4), (10, 5)];
		foreach ((int note, int whiteIndex) in blackNotes)
		{
			string className = selected.Contains(note) ? "diagram-key black selected" : "diagram-key black";
			image.Add(new XElement(
				svg + "rect",
				new XAttribute("class", className),
				new XAttribute("x", 7 + ((whiteIndex + 1) * WhiteWidth) - 3.5),
				new XAttribute("y", 8),
				new XAttribute("width", 7),
				new XAttribute("height", 40)));
		}
	}

	private static XElement SvgLine(XNamespace svg, double x1, double y1, double x2, double y2, string className)
		=> new(
			svg + "line",
			new XAttribute("class", className),
			new XAttribute("x1", x1),
			new XAttribute("y1", y1),
			new XAttribute("x2", x2),
			new XAttribute("y2", y2));

	private static XElement SvgText(XNamespace svg, double x, double y, string text, string className)
		=> new(svg + "text", new XAttribute("class", className), new XAttribute("x", x), new XAttribute("y", y), text);
	#pragma warning restore MEN010

	private XElement? FormatChordDefinitions(IEnumerable<ChordDefinition> definitions)
	{
		List<ChordDiagram> diagrams = [];
		foreach (ChordDefinition definition in definitions)
		{
			IReadOnlyList<int?> absoluteFrets = [.. definition.Definition.Select(fret => (int?)fret)];
			int baseFret = Math.Max(1, absoluteFrets.Where(fret => fret > 0).DefaultIfEmpty(1).Min() ?? 1);
			IReadOnlyList<int?> relativeFrets = [.. absoluteFrets.Select(fret => fret > 0 ? fret - (baseFret - 1) : fret)];
			ChordDiagram diagram = new(
				definition.Chord.Name,
				baseFret,
				relativeFrets,
				definition.Fingering?.Select(finger => (int?)finger).ToArray(),
				null);
			this.chordDiagrams[diagram.Name] = diagram;
			diagrams.Add(diagram);
		}

		return diagrams.Count > 0 ? FormatChordDiagrams(diagrams) : null;
	}

	private XElement? FormatChorusRecall(ChordProDirectiveLine directive)
	{
		XElement? result = this.lastChorus is null ? null : new(this.lastChorus);
		if (result is not null)
		{
			result.SetAttributeValue("class", ((string?)result.Attribute("class") + " recalled-chorus").Trim());
			result.SetAttributeValue("data-recalled-chorus", "true");
			XElement? header = result.Descendants().FirstOrDefault(element => HasClass(element, "section-header"));
			string? label = directive.Args.FirstValue;
			if (header is not null && !string.IsNullOrWhiteSpace(label))
			{
				header.Value = label;
			}
		}

		return result;
	}

	private ChordDiagram? ParseChordDiagram(ChordProDirectiveLine directive)
	{
		List<string> tokens = TokenizeDirectiveArgument(directive.Argument);
		ChordDiagram? result = null;
		if (tokens.Count > 0)
		{
			string name = tokens[0].Trim('[', ']');
			ChordDiagram diagram = this.chordDiagrams.TryGetValue(name, out ChordDiagram? known) ? new(known) : new(name);
			for (int index = 1; index < tokens.Count; index++)
			{
				string token = tokens[index];
				if ((token.Equals("copy", ChordParser.Comparison) || token.Equals("copyall", ChordParser.Comparison))
					&& (index + 1) < tokens.Count
					&& this.chordDiagrams.TryGetValue(tokens[++index].Trim('[', ']'), out ChordDiagram? copied))
				{
					diagram.CopyFrom(copied);
				}
				else if ((token.Equals("base-fret", ChordParser.Comparison) || token.Equals("base_fret", ChordParser.Comparison))
					&& (index + 1) < tokens.Count
					&& int.TryParse(tokens[++index], out int baseFret))
				{
					diagram.BaseFret = Math.Max(1, baseFret);
				}
				else if (token.Equals("frets", ChordParser.Comparison))
				{
					diagram.Frets = ParsePositions(tokens, ref index);
				}
				else if (token.Equals("fingers", ChordParser.Comparison))
				{
					diagram.Fingers = ParsePositions(tokens, ref index);
				}
				else if (token.Equals("keys", ChordParser.Comparison))
				{
					diagram.Keys = [.. ParsePositions(tokens, ref index)
						.Where(value => value is not null)
						.Select(value => value!.Value)];
				}
				else if (token.Equals("diagram", ChordParser.Comparison) && (index + 1) < tokens.Count)
				{
					diagram.Show = !tokens[++index].Equals("off", ChordParser.Comparison);
				}
				else if (token.Equals("display", ChordParser.Comparison) && (index + 1) < tokens.Count)
				{
					diagram.DisplayName = tokens[++index];
				}
			}

			if (directive.LongName.Equals("define", ChordParser.Comparison) || diagram.Frets is not null || diagram.Keys is not null)
			{
				this.chordDiagrams[name] = new(diagram);
			}

			result = diagram.Frets is not null || diagram.Keys is not null ? diagram : null;
		}

		return result;
	}

	private static List<int?> ParsePositions(List<string> tokens, ref int index)
	{
		List<int?> result = [];
		while ((index + 1) < tokens.Count && !ChordDiagramKeywords.Contains(tokens[index + 1]))
		{
			string value = tokens[++index];
			result.Add(ChordDefinition.IsUnplayedString(value) ? null : int.TryParse(value, out int number) ? number : null);
		}

		return result;
	}

	private static List<string> TokenizeDirectiveArgument(string? argument)
	{
		List<string> result = [];
		if (!string.IsNullOrWhiteSpace(argument))
		{
			StringBuilder token = new();
			char quote = '\0';
			foreach (char character in argument!)
			{
				if ((character == '\'' || character == '"') && (quote == '\0' || quote == character))
				{
					quote = quote == '\0' ? character : '\0';
				}
				else if (char.IsWhiteSpace(character) && quote == '\0')
				{
					if (token.Length > 0)
					{
						result.Add(token.ToString());
						token.Clear();
					}
				}
				else
				{
					token.Append(character);
				}
			}

			if (token.Length > 0)
			{
				result.Add(token.ToString());
			}
		}

		return result;
	}
	#pragma warning restore SA1204

	private static XElement FormatMetadata(string name, string argument)
	{
		XElement result;
		if (name.Equals("comment", ChordParser.Comparison))
		{
			result = Element("aside", "comment", argument);
			result.SetAttributeValue("role", "note");
		}
		else if (name.Equals("title", ChordParser.Comparison))
		{
			result = Element("h1", "title", argument);
		}
		else if (name.Equals("subtitle", ChordParser.Comparison) || name.Equals("artist", ChordParser.Comparison))
		{
			result = Element("div", $"subtitle {name}", argument);
		}
		else
		{
			result = new(
				"div",
				new XAttribute("class", $"metadata-entry metadata-{name}"),
				Element("span", "metadata-label", TitleLine.ToTitleCase(MetadataEntry.Untranslate(name)) + ":"),
				new XText(" "),
				Element("span", "metadata-value", argument));
		}

		return result;
	}

	private static XElement FormatSegments(
		IReadOnlyList<TextSegment> segments,
		string elementName,
		string className,
		string chordClassName,
		Func<Chord, Chord> transpose)
	{
		XElement result = new(elementName, new XAttribute("class", className));
		foreach (TextSegment segment in segments)
		{
			if (segment is ChordSegment chord)
			{
				result.Add(Element("span", chordClassName, transpose(chord.Chord).Name));
			}
			else
			{
				result.Add(new XText(segment.Text));
			}
		}

		return result;
	}

	private static XElement FormatTitle(TitleLine title)
	{
		XElement result = new("header", new XAttribute("class", "song-header"));
		MetadataEntry? titleMetadata = title.Metadata.FirstOrDefault(metadata => metadata.Name == "title");
		result.Add(Element("h1", "title", titleMetadata?.Argument ?? title.Text));

		foreach (MetadataEntry metadata in title.Metadata)
		{
			if (metadata != titleMetadata)
			{
				result.Add(FormatMetadata(metadata.Name, metadata.Argument));
			}
		}

		return result;
	}

	private static XElement FormatUri(UriLine uri)
	{
		const StringComparison Comparison = StringComparison.OrdinalIgnoreCase;
		string scheme = uri.Uri.Scheme;
		bool safe = scheme.Equals(Uri.UriSchemeHttp, Comparison)
			|| scheme.Equals(Uri.UriSchemeHttps, Comparison)
			|| scheme.Equals(Uri.UriSchemeMailto, Comparison);
		return safe
			? new XElement("a", new XAttribute("class", "source-link"), new XAttribute("href", uri.Uri.AbsoluteUri), uri.Text)
			: Element("span", "source-link", uri.Text);
	}

	private static string GetDocumentTitle(IEntryContainer container)
	{
		string? result = null;
		foreach (Entry entry in EnumerateEntries(container))
		{
			if (entry is TitleLine title)
			{
				result = title.Metadata.FirstOrDefault(metadata => metadata.Name == "title")?.Argument ?? title.Text;
			}
			else if (entry is MetadataEntry metadata && metadata.Name == "title")
			{
				result = metadata.Argument;
			}
			else if (entry is ChordProDirectiveLine directive
				&& directive.LongName.Equals("title", ChordParser.Comparison)
				&& !string.IsNullOrWhiteSpace(directive.Argument))
			{
				result = directive.Argument;
			}

			if (result is not null)
			{
				break;
			}
		}

		if (result is null && container is Document document && !string.IsNullOrWhiteSpace(document.FileName))
		{
			result = Path.GetFileNameWithoutExtension(document.FileName);
		}

		return result ?? "Chord Sheet";
	}

	private static IEnumerable<Entry> EnumerateEntries(IEntryContainer container)
	{
		foreach (Entry entry in container.Entries)
		{
			yield return entry;
			if (entry is IEntryContainer child)
			{
				foreach (Entry descendant in EnumerateEntries(child))
				{
					yield return descendant;
				}
			}
		}
	}

	private static string GetEntryClass(Entry entry)
		=> entry.GetType().Name.ToLowerInvariant();

	private static bool HasClass(XElement element, string className)
		=> ((string?)element.Attribute("class"))?.Split(' ').Contains(className) == true;

	private static string? GetEnvironmentName(Section section)
		=> section.Environment?.Name
			?? (section.Entries.Count > 0 && section.Entries[0] is HeaderLine header
				? ChordProEnvironment.GetHeaderEnvironmentName(header)
				: null);

	private static bool IsChorus(Section section)
		=> ChordProEnvironment.IsChorus(GetEnvironmentName(section));

	private static string LoadEmbeddedResource(string resourceName)
	{
		using Stream stream = typeof(HtmlFormatter).Assembly.GetManifestResourceStream(resourceName)
			?? throw new InvalidOperationException($"The embedded resource '{resourceName}' was not found.");
		using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
		return reader.ReadToEnd();
	}

	private Chord Transpose(Chord chord)
	{
		Chord result = this.transposeSettings.Count == 0 ? chord : chord.Transpose(
			this.transposeSettings.Peek().HalfSteps,
			this.transposeSettings.Peek().AccidentalPreference);
		return result;
	}

	private void UpdateTranspose(string? argument)
	{
		if (string.IsNullOrWhiteSpace(argument))
		{
			if (this.transposeSettings.Count > 0)
			{
				this.transposeSettings.Pop();
			}
		}
		else
		{
			string value = argument!.Trim();
			AccidentalPreference preference = AccidentalPreference.Default;
			char suffix = char.ToLowerInvariant(value[^1]);
			if (suffix is 's' or 'f')
			{
				preference = suffix == 's' ? AccidentalPreference.Sharps : AccidentalPreference.Flats;
				value = value.Substring(0, value.Length - 1);
			}

			if (sbyte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte halfSteps))
			{
				this.transposeSettings.Push(new(halfSteps, preference));
			}
		}
	}

	private void InitializeDocument(IEntryContainer container)
	{
		string title = GetDocumentTitle(container);
		XElement content = new("article", new XAttribute("class", "chord-sheet"));
		XElement head = new(
			"head",
			new XElement("meta", new XAttribute("charset", "utf-8")),
			new XElement("meta", new XAttribute("name", "viewport"), new XAttribute("content", "width=device-width, initial-scale=1")),
			new XElement("title", title),
			new XElement("style", new XAttribute("id", "menees-chords-defaults"), DefaultStyles.Value));
		string? optionStyles = this.options?.ToCss();
		if (!string.IsNullOrEmpty(optionStyles))
		{
			head.Add(new XElement("style", new XAttribute("id", "menees-chords-options"), optionStyles));
		}

		XElement html = new(
			"html",
			head,
			new XElement(
				"body",
				content,
				new XElement("script", new XAttribute("id", "menees-chords-pagination"), DefaultScript.Value)));

		this.document = new(new XDocumentType("html", null, null, null), html);
		this.currentContainer = content;

		if (container is Document document && !string.IsNullOrWhiteSpace(document.FileName))
		{
			content.SetAttributeValue("data-source-file", document.FileName);
		}
	}

	[MemberNotNull(nameof(document))]
	private void EnsureDocument()
	{
		if (this.document is null)
		{
			this.Format();
		}

		Conditions.RequireNonNull(this.document);
	}

	private sealed class ChordDiagram
	{
		public ChordDiagram(string name)
		{
			this.Name = name;
			this.DisplayName = name;
		}

		public ChordDiagram(
			string name,
			int baseFret,
			IReadOnlyList<int?>? frets,
			IReadOnlyList<int?>? fingers,
			IReadOnlyList<int>? keys)
			: this(name)
		{
			this.BaseFret = baseFret;
			this.Frets = frets;
			this.Fingers = fingers;
			this.Keys = keys;
		}

		public ChordDiagram(ChordDiagram source)
			: this(source.Name)
		{
			this.CopyFrom(source);
		}

		public int BaseFret { get; set; } = 1;

		public string DisplayName { get; set; }

		public IReadOnlyList<int?>? Fingers { get; set; }

		public IReadOnlyList<int?>? Frets { get; set; }

		public IReadOnlyList<int>? Keys { get; set; }

		public string Name { get; }

		public bool Show { get; set; } = true;

		public void CopyFrom(ChordDiagram source)
		{
			this.BaseFret = source.BaseFret;
			this.DisplayName = source.DisplayName;
			this.Fingers = source.Fingers;
			this.Frets = source.Frets;
			this.Keys = source.Keys;
			this.Show = source.Show;
		}
	}

	private sealed class RenderToken
	{
		public RenderToken(Chord chord)
		{
			this.Chord = chord;
			this.Text = chord.Name;
		}

		public RenderToken(ChordAnnotationSegment annotation)
		{
			this.IsAnnotation = true;
			this.Text = annotation.Annotation;
		}

		public RenderToken(string text, bool isWhiteSpace)
		{
			this.Text = text;
			this.IsWhiteSpace = isWhiteSpace;
		}

		public Chord? Chord { get; }

		public bool IsAboveLyric => this.Chord is not null || this.IsAnnotation;

		public bool IsAnnotation { get; }

		public bool IsWhiteSpace { get; }

		public string Text { get; }
	}

	private sealed class TransposeSetting
	{
		public TransposeSetting(sbyte halfSteps, AccidentalPreference accidentalPreference)
		{
			this.HalfSteps = halfSteps;
			this.AccidentalPreference = accidentalPreference;
		}

		public AccidentalPreference AccidentalPreference { get; }

		public sbyte HalfSteps { get; }
	}

	private sealed class WordRun
	{
		public bool HasAboveLyric => this.Tokens.Any(token => token.IsAboveLyric);

		public string SeparatorAfter { get; set; } = string.Empty;

		public List<RenderToken> Tokens { get; } = [];

		public void Append(WordRun next)
		{
			this.Tokens.Add(new(" ", isWhiteSpace: false));
			this.Tokens.AddRange(next.Tokens);
			this.SeparatorAfter = next.SeparatorAfter;
		}
	}

	#endregion
}
