namespace Menees.Chords.Formatters;

#region Using Directives

using System.IO;
using System.Text;
using System.Xml.Linq;
using Menees.Chords.Formatters.Html;
using Menees.Chords.Parsers;

#endregion

[TestClass]
public class HtmlFormatterTests
{
	#region Public Properties

	/// <summary>
	/// Gets or sets the current test context.
	/// </summary>
	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public void ChordLyricPairTest()
	{
		Document document = Document.Parse("      G\nhello you");
		ChordLyricPair pair = document.Entries.OfType<ChordLyricPair>().Single();
		HtmlFormatter formatter = new(pair);
		XDocument html = formatter.ToXDocument();

		GetClassElements(html, "chord-line").Count().ShouldBe(1);
		GetClassElements(html, "chord-only-line").ShouldBeEmpty();
		GetClassElements(html, "lyric-line").ShouldBeEmpty();
		GetClassElements(html, "lyric").Single().Value.ShouldBe("you");
		string plainText = string.Concat(GetClassElements(html, "chord-line").Single().Nodes().OfType<XText>().Select(text => text.Value));
		plainText.ShouldBe("hello ");
	}

	[TestMethod]
	public void ChordProWordsTest()
	{
		DocumentParser parser = new(DocumentParser.ChordProLineParsers, DocumentParser.Ungrouped);
		Document document = Document.Parse("[D]low [G#m7]chari[D]ot", parser);
		HtmlFormatter formatter = new(document);
		XDocument html = formatter.ToXDocument();

		GetClassElements(html, "word").Count().ShouldBe(2);
		GetClassElements(html, "chord").Select(element => element.Value).ShouldBe(["D", "G#m7", "D"]);
		GetClassElements(html, "lyric").Select(element => element.Value).ShouldBe(["low", "chari", "ot"]);
		GetClassElements(html, "chord").All(element => (string?)element.Attribute("aria-hidden") == "true").ShouldBeTrue();

		string text = formatter.ToString();
		text.ShouldContain("</span><span class=\"chord-lyric\">");
	}

	[TestMethod]
	public void ChordProChordAnnotationTest()
	{
		DocumentParser parser = new(DocumentParser.ChordProLineParsers, DocumentParser.Ungrouped);
		XDocument html = new HtmlFormatter(Document.Parse("[G] [C]    [*4x]", parser)).ToXDocument();

		XElement line = GetClassElements(html, "chord-line").Single();
		GetClassElements(line, "chord").Select(element => element.Value).ShouldBe(["G", "C", "4x"]);
		XElement annotation = GetClassElements(line, "chord-annotation").Single();
		annotation.Value.ShouldBe("4x");
		annotation.Attribute("aria-hidden").ShouldBeNull();
		((string?)annotation.Attribute("role")).ShouldBe("note");
		GetClassElements(html, "lyric-line").ShouldBeEmpty();
	}

	[TestMethod]
	public void ChordOverLyricFlowTest()
	{
		Document document = Document.Parse("    Cadd9\nIf I     could have let you know");
		HtmlFormatter formatter = new(document);
		XDocument html = formatter.ToXDocument();

		GetClassElements(html, "chord").Single().Value.ShouldBe("Cadd9");
		GetClassElements(html, "lyric").Single().Value.ShouldBe("I could have let you know");
		GetClassElements(html, "chord-line").Single().Value.ShouldContain("I could have let you know");
	}

	[TestMethod]
	public void ShortLyricFlowsUnderChordTest()
	{
		DocumentParser parser = new(DocumentParser.ChordProLineParsers, DocumentParser.Ungrouped);
		Document document = Document.Parse("[Em]If God had [G]known\nHear the D[G]J say love's", parser);
		XDocument html = new HtmlFormatter(document).ToXDocument();

		GetClassElements(html, "lyric")
			.Select(element => element.Value)
			.ShouldBe(["If God had", "known", "J say love's"]);
	}

	[TestMethod]
	public void ChorusRecallTest()
	{
		const string Text = """
			{start_of_chorus}
			[G]Sing it again
			{end_of_chorus}
			{chorus: label="Final Chorus"}
			""";
		DocumentParser parser = new(DocumentParser.ChordProLineParsers);
		XDocument html = new HtmlFormatter(Document.Parse(Text, parser)).ToXDocument();

		XElement recalled = GetClassElements(html, "recalled-chorus").Single();
		GetClassElements(recalled, "section-header").Single().Value.ShouldBe("Final Chorus");
		GetClassElements(recalled, "chord-line").Single().Value.ShouldContain("Sing it again");
		html.Descendants("section").Count(element => (string?)element.Attribute("data-environment") == "chorus")
			.ShouldBe(2); // The definition and recalled clone both retain chorus styling.
		GetDefaultStyles(html).ShouldContain(".section[data-environment=\"chorus\"]");
	}

	[TestMethod]
	public void PlainTextChorusHeaderUsesChorusEnvironmentStyleTest()
	{
		Document document = Document.Parse("[Chorus]\nG\nSing it again");
		XElement chorus = new HtmlFormatter(document).ToXDocument().Descendants("section").Single();

		((string?)chorus.Attribute("data-environment")).ShouldBe("chorus");
		((string?)chorus.Attribute("class") ?? string.Empty).ShouldContain("environment-chorus");
	}

	[TestMethod]
	public void CommentAndRemarkTest()
	{
		const string Text = """
			# This must not be displayed.
			{comment: Play softly}
			""";
		Document document = Document.Parse(Text);
		HtmlFormatter formatter = new(document);
		XDocument html = formatter.ToXDocument();

		GetClassElements(html, "comment").Single().Value.ShouldBe("Play softly");
		formatter.ToString().ShouldNotContain("This must not be displayed");
		GetDefaultStyles(html).ShouldContain("font-style: italic");
	}

	[TestMethod]
	public void PageAndColumnBreakTest()
	{
		const string Text = "{new_page}\n{np}\n{column_break}\n{colb}";
		DocumentParser parser = new(DocumentParser.ChordProLineParsers, DocumentParser.Ungrouped);
		XDocument html = new HtmlFormatter(Document.Parse(Text, parser)).ToXDocument();

		GetClassElements(html, "page-break").Count().ShouldBe(2);
		GetClassElements(html, "column-break").Count().ShouldBe(2);
		GetDefaultStyles(html).ShouldContain("break-after: column");
		GetDefaultScript(html).ShouldContain("forcePageBefore");
		GetDefaultScript(html).ShouldContain("breakType === \"page\"");
	}

	[TestMethod]
	public void DiagramTest()
	{
		const string Text = """
			{define: C7 base-fret 1 frets x 3 2 3 1 0 fingers x 3 2 4 1 x}
			{chord: C7}
			{chord: D keys 0 4 7}
			""";
		DocumentParser parser = new(DocumentParser.ChordProLineParsers, DocumentParser.Ungrouped);
		XDocument html = new HtmlFormatter(Document.Parse(Text, parser)).ToXDocument();

		GetClassElements(html, "chord-diagram").Count().ShouldBe(3);
		IEnumerable<XElement> images = html.Descendants().Where(element => element.Name.LocalName == "svg");
		images.Count().ShouldBe(3);
		images
			.Select(element => (string?)element.Attribute("aria-label"))
			.ShouldBe(["C7 chord diagram", "C7 chord diagram", "D chord diagram"]);
		images.First().Descendants()
			.Count(element => element.Name.LocalName == "line"
				&& (string?)element.Attribute("y1") == (string?)element.Attribute("y2"))
			.ShouldBe(4);
	}

	[TestMethod]
	public void DelegatedEnvironmentTest()
	{
		const string Svg = """<svg xmlns="http://www.w3.org/2000/svg"><circle r="4" /></svg>""";
		string text = $$"""
			{start_of_svg label="Alert" align="center"}
			{{Svg}}
			{end_of_svg}
			{start_of_textblock label="Aside" flush="right" textcolor="#123456"}
			Line one
			Line two
			{end_of_textblock}
			{start_of_abc}
			X:1
			K:C
			{end_of_abc}
			{start_of_ly}
			\relative { c' d' }
			{end_of_ly}
			""";
		DocumentParser parser = new(DocumentParser.ChordProLineParsers);
		XDocument html = new HtmlFormatter(Document.Parse(text, parser)).ToXDocument();

		XElement svgEnvironment = html.Descendants("section")
			.Single(element => (string?)element.Attribute("data-environment") == "svg");
		XElement image = svgEnvironment.Element("img")!;
		image.Attribute("alt")!.Value.ShouldBe("Alert");
		image.Attribute("class")!.Value.ShouldBe("delegated-svg");
		string source = image.Attribute("src")!.Value.Substring("data:image/svg+xml;base64,".Length);
		Encoding.UTF8.GetString(Convert.FromBase64String(source)).ShouldBe(Svg);

		XElement textBlock = html.Descendants("section")
			.Single(element => (string?)element.Attribute("data-environment") == "textblock");
		textBlock.Element("h2")!.Value.ShouldBe("Aside");
		textBlock.Element("pre")!.Value.ShouldBe("Line one" + Environment.NewLine + "Line two");
		textBlock.Element("pre")!.Attribute("style")!.Value.ShouldContain("text-align: right");
		html.Descendants().Any(element => (string?)element.Attribute("data-environment") is "abc" or "ly").ShouldBeFalse();
	}

	[TestMethod]
	public void Latin1SampleTest()
	{
		const int Latin1CodePage = 28591;
		string fileName = Path.GetTempFileName();
		try
		{
			Encoding latin1 = Encoding.GetEncoding(Latin1CodePage);
			File.WriteAllBytes(fileName, latin1.GetBytes("[Intro - Escape (Piña Colada)]"));
			Document document = Document.Load(fileName);
			new HtmlFormatter(document).ToString().ShouldContain("Piña Colada");
		}
		finally
		{
			File.Delete(fileName);
		}
	}

	[TestMethod]
	public void GenerateSampleFilesTest()
	{
		#if NETFRAMEWORK
		const string TargetFramework = "net48";
		#else
		const string TargetFramework = "net8.0";
		#endif
		string outputDirectory = Path.Combine(
			Path.GetTempPath(),
			"TestResults",
			"Menees.Chords Html Samples",
			TargetFramework);
		Directory.CreateDirectory(outputDirectory);

		foreach (string inputFileName in TestUtility.GetSampleFileNames())
		{
			Document document = Document.Load(inputFileName);
			HtmlFormatter formatter = new(document);
			string html = formatter.ToString();
			formatter.ToXDocument().Root!.Name.LocalName.ShouldBe("html");

			string outputFileName = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputFileName) + ".html");
			File.WriteAllText(outputFileName, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			this.TestContext.AddResultFile(outputFileName);
		}

		this.TestContext.WriteLine($"HTML samples: {outputDirectory}");
	}

	[TestMethod]
	public void SerializeTest()
	{
		Document document = TestUtility.LoadSwingLowSweetChariot();
		HtmlFormatter formatter = new(document);
		string text = formatter.ToString();

		text.ShouldStartWith("<!DOCTYPE html>");
		text.ShouldContain("<title>Swing Low Sweet Chariot</title>");
		text.ShouldContain("class=\"chord-sheet\"");
		text.ShouldContain("id=\"menees-chords-pagination\"");
		text.ShouldContain("const columns = measureColumns(metrics.height);");
		text.ShouldContain("usedWidth + additionalWidth > metrics.width");
		text.ShouldNotContain("usedWidth + additionalWidth &gt; metrics.width");
		text.ShouldContain(".word > .chord-lyric:not(:last-child) .chord");
		text.ShouldContain(".word > .chord-lyric:not(:last-child) .lyric::after");
		text.ShouldNotContain("\n\t\t.lyric::after");
		text.ShouldContain("white-space: normal");
		text.ShouldNotContain("<section class=\"section\" data-container=\"Section\" />");
	}

	[TestMethod]
	public void SerializeEncodesDomTextTest()
	{
		XDocument document = new(new XElement("div", "A < B & C"));

		document.Root!.Value.ShouldBe("A < B & C");
		HtmlFormatter.Serialize(document).ShouldBe("<div>A &lt; B &amp; C</div>");
	}

	[TestMethod]
	public void ResponsivePaginationDefaultsTest()
	{
		XDocument html = new HtmlFormatter(TestUtility.LoadSwingLowSweetChariot()).ToXDocument();
		XElement script = html.Root!.Element("body")!.Element("script")!;
		string defaultStyles = GetDefaultStyles(html);
		string defaultScript = script.Value;

		((string?)script.Attribute("id")).ShouldBe("menees-chords-pagination");
		defaultScript.ShouldContain("measureColumns(metrics.height)");
		defaultStyles.ShouldContain("--column-min-width: 18em");
		defaultStyles.ShouldContain("inline-size: max-content");
		defaultStyles.ShouldContain("--chord-color: #3045c7");
		defaultStyles.ShouldContain("--section-header-size: 1.1em");
		defaultStyles.ShouldContain("--music-line-gap: 0.2em");
		defaultStyles.ShouldContain("--chorus-border-width: 2px");
		defaultStyles.ShouldContain(":is(.chord-line, .chord-only-line, .lyric-line)");
		defaultStyles.ShouldContain(".title {");
		defaultStyles.ShouldContain("font-weight: normal");
		defaultStyles.ShouldContain(".section-header {");
		defaultStyles.ShouldContain("font-weight: bold");
		defaultStyles.ShouldNotContain("text-transform: uppercase");
		int sectionHeaderStart = defaultStyles.IndexOf(".section-header {", StringComparison.Ordinal);
		int sectionHeaderEnd = defaultStyles.IndexOf('}', sectionHeaderStart);
		defaultStyles.Substring(sectionHeaderStart, sectionHeaderEnd - sectionHeaderStart)
			.ShouldNotContain("border-inline-start");
	}

	[TestMethod]
	public void OptionsTest()
	{
		HtmlFormatterOptions options = new()
		{
			MonospaceFontFamily = CssFontFamily.FromNames("Cascadia Mono", "monospace"),
			LineSpacing = 1.25,
			ChordLineSpacing = 1.05,
			MusicLineGap = CssSize.Em(0.3),
			SectionIndent = CssSize.Em(1),
			GridLineThickness = CssSize.Em(0.05),
			LyricExtensionThickness = CssSize.Pixels(1),
			ConsecutiveChordGap = CssSize.Em(0.25),
			DiagramSize = CssSize.Em(7),
			DiagramLineColor = CssColor.Parse("black"),
			DiagramDotColor = CssColor.Parse("navy"),
			PageBlockSize = CssSize.Parse("100dvh"),
			PagePadding = CssSize.Em(2),
			ColumnGap = CssSize.Em(4),
			ColumnMinimumWidth = CssSize.Em(20),
		};
		options.DefaultTextStyle.FontFamily = CssFontFamily.FromNames("Inter", "sans-serif");
		options.DefaultTextStyle.FontSize = CssSize.Pixels(15);
		options.TitleStyle.FontSize = CssSize.Pixels(24);
		options.ChordStyle.Color = CssColor.Parse("blue");
		options.CommentStyle.Italic = false;
		options.SectionHeaderStyle.Bold = false;
		options.SectionHeaderStyle.HighlightColor = CssColor.Parse("#ffff80");
		EnvironmentStyle chorusStyle = new()
		{
			Indent = CssSize.Zero,
			Padding = CssSize.Em(0.25),
			BorderWidth = CssSize.Pixels(2),
			BorderColor = CssColor.Parse("purple"),
		};
		chorusStyle.HeaderStyle.Italic = true;
		options.EnvironmentStyles.Add("chorus", chorusStyle);
		XDocument html = new HtmlFormatter(TestUtility.LoadSwingLowSweetChariot(), options).ToXDocument();
		XElement optionStyle = html.Root!.Element("head")!.Elements("style")
			.Single(element => (string?)element.Attribute("id") == "menees-chords-options");
		string css = optionStyle.Value;

		string[] expectedDeclarations =
		[
			"--monospace-font-family: \"Cascadia Mono\", monospace;",
			"--line-spacing: 1.25;",
			"--chord-line-spacing: 1.05;",
			"--music-line-gap: 0.3em;",
			"--section-indent: 1em;",
			"--grid-line-thickness: 0.05em;",
			"--lyric-extension-thickness: 1px;",
			"--consecutive-chord-gap: 0.25em;",
			"--diagram-size: 7em;",
			"--diagram-line-color: black;",
			"--diagram-dot-color: navy;",
			"--page-block-size: 100dvh;",
			"--page-padding: 2em;",
			"--column-gap: 4em;",
			"--column-min-width: 20em;",
		];
		foreach (string declaration in expectedDeclarations)
		{
			css.ShouldContain(declaration);
		}

		css.ShouldContain(".chord-sheet {");
		css.ShouldContain("font-family: \"Inter\", sans-serif;");
		css.ShouldContain("font-size: 15px;");
		css.ShouldContain(".title {");
		css.ShouldContain("font-size: 24px;");
		css.ShouldContain(".chord, .chord-only-line {");
		css.ShouldContain("color: blue;");
		css.ShouldContain(".comment, .annotation {");
		css.ShouldContain("font-style: normal;");
		css.ShouldContain(".section-header {");
		css.ShouldContain("font-weight: normal;");
		css.ShouldContain("background-color: #ffff80;");
		css.ShouldContain(".section[data-environment=\"chorus\"] {");
		css.ShouldContain("margin-inline-start: 0;");
		css.ShouldContain("padding-inline-start: 0.25em;");
		css.ShouldContain("border-inline-start-width: 2px;");
		css.ShouldContain("border-inline-start-color: purple;");
		css.ShouldContain(".section[data-environment=\"chorus\"] > .section-header {");
	}

	[TestMethod]
	public void ToXDocumentReturnsCloneTest()
	{
		Document document = TestUtility.LoadSwingLowSweetChariot();
		HtmlFormatter formatter = new(document);
		XDocument first = formatter.ToXDocument();
		XElement firstHead = first.Root!.Element("head")!;
		firstHead.Add(new XElement("style", ".chord { color: red; }"));

		XDocument second = formatter.ToXDocument();
		second.Root!.Element("head")!.Elements("style").Count().ShouldBe(1);
		firstHead.Elements("style").Count().ShouldBe(2);
	}

	[TestMethod]
	public void ScopedTransposeTest()
	{
		DocumentParser parser = new(DocumentParser.ChordProLineParsers);
		Document document = Document.Parse(
			"""
			[C]Base
			{transpose: 14}
			[C]Sharp
			{transpose: 1f}
			[C]Flat
			{transpose}
			[C]Sharp again
			{transpose}
			[C]Base again
			""",
			parser);
		XDocument html = new HtmlFormatter(document).ToXDocument();
		GetClassElements(html, "chord").Select(element => element.Value)
			.ShouldBe(["C", "D", "Db", "D", "C"]);
		GetClassElements(html, "directive").ShouldBeEmpty();
	}

	#endregion

	#region Private Methods

	private static string GetDefaultScript(XDocument document)
		=> document.Root!.Element("body")!.Elements("script")
			.Single(element => (string?)element.Attribute("id") == "menees-chords-pagination").Value;

	private static string GetDefaultStyles(XDocument document)
		=> document.Root!.Element("head")!.Elements("style")
			.Single(element => (string?)element.Attribute("id") == "menees-chords-defaults").Value;

	private static IEnumerable<XElement> GetClassElements(XContainer container, string className)
		=> container.Descendants().Where(element
			=> ((string?)element.Attribute("class"))?.Split(' ').Contains(className) == true);

	#endregion
}
