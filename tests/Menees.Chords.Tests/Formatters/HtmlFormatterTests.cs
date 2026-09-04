namespace Menees.Chords.Formatters;

#region Using Directives

using System.IO;
using System.Text;
using System.Xml.Linq;
using Menees.Chords.Formatters.Html;
using Menees.Chords.Parsers;
using Menees.Chords.Transformers;

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
	public void ChordProTimingSpacesTest()
	{
		DocumentParser parser = new(DocumentParser.ChordProLineParsers, DocumentParser.Ungrouped);
		const string Text = "[G]  I know I could have saved a love that night If I'd [Cadd9]known what to say";
		XDocument html = new HtmlFormatter(Document.Parse(Text, parser)).ToXDocument();

		GetClassElements(html, "lyric").Select(element => element.Value)
			.ShouldBe(["  I know I could have saved a love that night If I'd", "known what to say"]);
		GetClassElements(html, "lyric-text").Select(element => element.Value)
			.ShouldBe(["  I know I could have saved a love that night If I'd", "known what to say"]);
		GetDefaultStyles(html).ShouldContain(".lyric-text {\n\twhite-space: pre;");
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
	public void ParenthesizedChordTest()
	{
		DocumentParser parser = new(DocumentParser.ChordProLineParsers, DocumentParser.Ungrouped);
		Document document = Document.Parse("[*(][Db][*)]", parser);
		Document transposed = new TransposeTransformer(document, 2, Key.Parse("Db"), AccidentalPreference.Flats).Transform().Document;
		XDocument html = new HtmlFormatter(transposed).ToXDocument();

		transposed.Entries.Single().ToString().ShouldBe("[*(][Eb][*)]");
		GetClassElements(html, "chord").Single().Value.ShouldBe("(Eb)");
		GetClassElements(html, "chord-annotation").ShouldBeEmpty();
		GetClassElements(html, "chord-lyric").Count().ShouldBe(1);
	}

	[TestMethod]
	public void MusicLineIndentationTest()
	{
		DocumentParser parser = new(DocumentParser.ChordProLineParsers, DocumentParser.Ungrouped);
		XDocument html = new HtmlFormatter(Document.Parse("  [G]Indented\n  Plain", parser)).ToXDocument();

		GetClassElements(html, "chord-line").Single().Nodes().OfType<XText>().First().Value.ShouldBe("  ");
		GetClassElements(html, "lyric-line").Single().Value.ShouldBe("  Plain");
		string styles = GetDefaultStyles(html);
		styles.ShouldContain(".song-column .lyric-line {\n\twhite-space: pre;");
		styles.ShouldContain(".lyric-line,\n.chord-line {\n\twhite-space: pre-wrap;");
	}

	[TestMethod]
	public void RehearsalIdentifierTest()
	{
		const string Text = """
			(IN__TRO)
			(TUR__N)
			  (OU__TRO)__
			(TUSSENSPEL)__
			(X1)___
			(X12)____
			(X)___
			(VERSE__ONE)
			____(Backing lyrics)
			Before ____(alternate lyrics) after
			C
			Paired __(harmony)
			""";
		Document document = Document.Parse(Text);
		XDocument html = new HtmlFormatter(document).ToXDocument();

		GetClassElements(html, "comment").Select(element => element.Value)
			.ShouldBe(["INTRO", "TURN", "VERSE__ONE"]);
		GetClassElements(html, "lyric-line").Select(element => element.Value)
			.ShouldBe(["  (OUTRO)", "(TUSSENSPEL)", "(X1)", "(X12)", "(X)___", "(Backing lyrics)", "Before (alternate lyrics) after"]);
		GetClassElements(html, "chord-line").Single().Value.ShouldContain("Paired (harmony)");

		DocumentTransformer.Flatten(document.Entries).Select(entry => entry.ToString())
			.ShouldContain("Paired __(harmony)");
	}

	[TestMethod]
	public void TransformedOpenSongRehearsalIdentifierTest()
	{
		const string Xml = "<song><title>Test</title><lyrics>[P1]\n.C / G\n (IN__TRO)\n[P2]\n.C / G\n (OU__TRO)</lyrics></song>";
		Document transformed = new ChordProTransformer(Document.Parse(Xml)).Transform().Document;
		IReadOnlyList<ChordProDirectiveLine> comments = [.. DocumentTransformer.Flatten(transformed.Entries)
			.OfType<ChordProDirectiveLine>()
			.Where(directive => directive.LongName == "comment")];
		XDocument html = new HtmlFormatter(transformed).ToXDocument();

		comments.Select(comment => comment.Argument).ShouldBe(["IN__TRO", "OU__TRO"]);
		GetClassElements(html, "comment").Select(element => element.Value).ShouldBe(["INTRO", "OUTRO"]);
	}

	[TestMethod]
	public void ChordOverLyricFlowTest()
	{
		Document document = Document.Parse("    Cadd9\nIf I     could have let you know");
		HtmlFormatter formatter = new(document);
		XDocument html = formatter.ToXDocument();

		GetClassElements(html, "chord").Single().Value.ShouldBe("Cadd9");
		GetClassElements(html, "lyric").Single().Value.ShouldBe("I     could have let you know");
		GetClassElements(html, "chord-line").Single().Value.ShouldContain("I     could have let you know");
	}

	[TestMethod]
	public void ChordOverLyricTimingSpacesTest()
	{
		const string Text = """
			G                                                    Cadd9
			  I know I could have saved a love that night If I'd known what to say
			""";
		Document document = Document.Parse(Text);
		XElement line = GetClassElements(new HtmlFormatter(document).ToXDocument(), "chord-line").Single();

		line.FirstNode.ShouldBeOfType<XElement>();
		GetClassElements(line, "lyric-text").Select(element => element.Value)
			.ShouldBe(["  I know I could have saved a love that night If I'd", "known what to say"]);
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
	public void HeaderAnnotationTest()
	{
		Document document = Document.Parse("[Chorus] (a cappella)");
		XDocument html = new HtmlFormatter(document).ToXDocument();
		XElement row = GetClassElements(html, "entry-with-annotations").Single();
		XElement header = GetClassElements(row, "section-header").Single();
		XElement annotation = GetClassElements(row, "entry-annotation").Single();

		header.Value.ShouldBe("Chorus");
		annotation.Value.ShouldBe("(a cappella)");
		annotation.Parent.ShouldBeSameAs(row);
		((string?)annotation.Attribute("class") ?? string.Empty).ShouldContain("comment");
		((string?)annotation.Attribute("role")).ShouldBe("note");
	}

	[TestMethod]
	public void ChordDefinitionAnnotationTest()
	{
		Document document = Document.Parse("D       D* = x57775");
		XDocument html = new HtmlFormatter(document).ToXDocument();
		XElement row = GetClassElements(html, "entry-with-annotations").Single();

		GetClassElements(row, "chord-only-line").Single().Value.ShouldBe("D       ");
		XElement diagrams = GetClassElements(row, "chord-diagrams").Single();
		((string?)diagrams.Attribute("class") ?? string.Empty).ShouldContain("entry-annotation");
		GetClassElements(diagrams, "chord-diagram-name").Single().Value.ShouldBe("D*");
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
	public void BracketedChordGridTest()
	{
		const string Text = "||:  /[A]___/   [Asus2] /___/[A]  /__[Asus2]_/     [A]/  |  [D]/___/  /__[Dsus2]_/    /   [D]/  :||[x2]";
		XDocument html = new HtmlFormatter(Document.Parse(Text)).ToXDocument();
		XElement grid = GetClassElements(html, "grid-line").Single();

		GetClassElements(grid, "grid-chord").Select(element => element.Value)
			.ShouldBe(["A", "Asus2", "A", "Asus2", "A", "D", "Dsus2", "D"]);
		grid.Value.ShouldBe("||:  /A___/   Asus2 /___/A  /__Asus2_/     A/  |  D/___/  /__Dsus2_/    /   D/  :||[x2]");
	}

	[TestMethod]
	public void DiagramTest()
	{
		const string Text = """
			{define: C7 base-fret 1 frets x 3 2 3 1 0 fingers x 3 2 4 1 x}
			{chord: C7}
			{chord: D keys 0 4 7}
			{chord: G keys 0 4 7}
			{define: D² keys 7 12 16}
			{chord: [C7]}
			{define: [D]}
			""";
		DocumentParser parser = new(DocumentParser.ChordProLineParsers, DocumentParser.Ungrouped);
		XDocument html = new HtmlFormatter(Document.Parse(Text, parser)).ToXDocument();

		GetClassElements(html, "chord-diagram").Count().ShouldBe(5);
		GetClassElements(html, "chord-diagrams").Count().ShouldBe(1);
		IEnumerable<XElement> images = html.Descendants().Where(element => element.Name.LocalName == "svg");
		images.Count().ShouldBe(5);
		images
			.Select(element => (string?)element.Attribute("aria-label"))
			.ShouldBe(["C7 chord diagram", "C7 chord diagram", "D chord diagram", "G chord diagram", "D² chord diagram"]);
		GetClassElements(images.First(), "diagram-fret").Count().ShouldBe(6);
		((string?)images.First().Attribute("viewBox")).ShouldBe("-10 0 80 72");
		images.ElementAt(2).Descendants()
			.Where(element => element.Name.LocalName == "rect" && HasClass(element, "selected"))
			.Select(element => (string?)element.Attribute("x"))
			.OrderBy(value => value)
			.ShouldBe(["18", "47.5", "62"]);
		((string?)images.ElementAt(2).Attribute("viewBox")).ShouldBe("0 0 91 82");
		((string?)images.ElementAt(3).Attribute("viewBox")).ShouldBe("0 0 91 82");
		images.ElementAt(3).Descendants()
			.Where(element => element.Name.LocalName == "rect" && HasClass(element, "selected"))
			.Select(element => (string?)element.Attribute("x"))
			.OrderBy(value => value)
			.ShouldBe(["18", "40", "62"]);
		((string?)images.ElementAt(4).Attribute("viewBox")).ShouldBe("0 0 135 82");
		images.ElementAt(4).Descendants()
			.Where(element => element.Name.LocalName == "rect" && HasClass(element, "selected"))
			.Select(element => (string?)element.Attribute("x"))
			.OrderBy(value => value)
			.ShouldBe(["29", "62", "91.5"]);
		GetDefaultStyles(html).ShouldContain("--diagram-key-color: #d9d9d9");
		GetDefaultStyles(html).ShouldNotContain(".diagram-key.black.selected");
	}

	[TestMethod]
	public void FretDiagramLayoutTest()
	{
		const string Text = """
			{chord: D/F# base-fret 2 frets 1 4 3 1 2 1 fingers 1 4 3 1 2 1}
			{chord: F base-fret 1 frets 1 3 3 2 1 1 fingers 1 3 4 2 1 1}
			{chord: D7 base-fret 3 frets x 3 2 3 1 x fingers x 4 2 3 1 x}
			{chord: C base-fret 1 frets x 3 2 0 1 0 fingers x 3 2 x 1 x}
			""";
		DocumentParser parser = new(DocumentParser.ChordProLineParsers, DocumentParser.Ungrouped);
		IEnumerable<XElement> images = new HtmlFormatter(Document.Parse(Text, parser)).ToXDocument()
			.Descendants()
			.Where(element => element.Name.LocalName == "svg");

		XElement shifted = images.First();
		GetClassElements(shifted, "diagram-fret-label").Single().Value.ShouldBe("2fr");
		GetClassElements(shifted, "diagram-finger-position").Select(element => element.Value)
			.ShouldBe(["4", "3", "2"]);
		GetClassElements(shifted, "diagram-barre").Count().ShouldBe(1);
		GetClassElements(shifted, "diagram-dot").Count().ShouldBe(3);
		GetClassElements(shifted, "diagram-string-state").ShouldBeEmpty();

		XElement barre = GetClassElements(images.ElementAt(1), "diagram-barre").Single();
		barre.Name.LocalName.ShouldBe("rect");
		((string?)barre.Attribute("x")).ShouldBe("8");
		((string?)barre.Attribute("width")).ShouldBe("44");
		GetClassElements(images.Last(), "diagram-string-state").Select(element => element.Value)
			.ShouldBe(["×"]);
		GetClassElements(images.Last(), "diagram-dot").Count().ShouldBe(3);
		GetClassElements(images.ElementAt(2), "diagram-fret-label").Single().Value.ShouldBe("3fr");
		GetClassElements(images.ElementAt(2), "diagram-fret").Count().ShouldBe(6);
		GetClassElements(images.ElementAt(2), "diagram-string").Count().ShouldBe(6);
		IEnumerable<XElement> edges = GetClassElements(images.ElementAt(2), "diagram-edge");
		edges.Count().ShouldBe(2);
		edges.ShouldAllBe(element => element.Name.LocalName == "rect");
		edges.Select(element => (string?)element.Attribute("x")).ShouldAllBe(value => value == "9.5");
		edges.Select(element => (string?)element.Attribute("width")).ShouldAllBe(value => value == "41");
		IEnumerable<XElement> gridLines = images.ElementAt(2).Elements().Where(element => HasClass(element, "diagram-line"));
		gridLines.Take(6).ShouldAllBe(element => HasClass(element, "diagram-fret"));
		gridLines.Skip(6).ShouldAllBe(element => HasClass(element, "diagram-string"));
	}

	[TestMethod]
	public void DiagramDirectiveOptionsTest()
	{
		const string Text = """
			{define: C base-fret 1 frets x 3 2 0 1 0 diagram off display Hidden}
			{chord: C}
			{chord: C diagram on display Shown}
			{chord: C diagram compact display Compact}
			""";
		DocumentParser parser = new(DocumentParser.ChordProLineParsers, DocumentParser.Ungrouped);
		XDocument html = new HtmlFormatter(Document.Parse(Text, parser)).ToXDocument();

		GetClassElements(html, "chord-diagram").Count().ShouldBe(1);
		GetClassElements(html, "chord-diagram-name").Single().Value.ShouldBe("Shown");
		GetClassElements(html, "compact-chord-diagram").Single().Value.ShouldBe("Compact x32010");
		GetClassElements(html, "chord-diagrams").Count().ShouldBe(1);
	}

	[TestMethod]
	public void DiagramRunsTest()
	{
		const string Text = """
			{chord: C frets x 3 2 0 1 0}
			{chord: D frets x x 0 2 3 2}

			{chord: E frets 0 2 2 1 0 0}
			Lyrics break the run
			{chord: F frets 1 3 3 2 1 1}
			""";
		DocumentParser parser = new(DocumentParser.ChordProLineParsers, DocumentParser.Ungrouped);
		XDocument html = new HtmlFormatter(Document.Parse(Text, parser)).ToXDocument();
		IEnumerable<XElement> runs = GetClassElements(html, "chord-diagrams");

		runs.Count().ShouldBe(3);
		runs.Select(run => GetClassElements(run, "chord-diagram").Count()).ShouldBe([2, 1, 1]);
	}

	[TestMethod]
	public void DiagramModesTest()
	{
		const string Text = """
			{chord: C base-fret 1 frets x 3 2 0 1 0 fingers x 3 2 x 1 x display Cshape}
			{chord: G keys 0 4 7 display Gshape}
			{define: D² keys 7 12 16}
			""";
		DocumentParser parser = new(DocumentParser.ChordProLineParsers, DocumentParser.Ungrouped);
		Document document = Document.Parse(Text, parser);

		HtmlFormatterOptions options = new()
		{
			FretDiagramMode = ChordDiagramMode.CompactText,
			KeyboardDiagramMode = ChordDiagramMode.None,
		};
		XDocument html = new HtmlFormatter(document, options).ToXDocument();
		GetClassElements(html, "compact-chord-diagram").Select(element => element.Value)
			.ShouldBe(["Cshape x32010 x32x1x"]);
		GetClassElements(html, "chord-diagram").ShouldBeEmpty();

		Document compactDirective = Document.Parse(
			"{chord: C frets x 3 2 0 1 0 diagram compact}",
			parser);
		options.FretDiagramMode = ChordDiagramMode.None;
		html = new HtmlFormatter(compactDirective, options).ToXDocument();
		GetClassElements(html, "compact-chord-diagram").ShouldBeEmpty();

		options.KeyboardDiagramMode = ChordDiagramMode.CompactText;
		html = new HtmlFormatter(document, options).ToXDocument();
		GetClassElements(html, "compact-chord-diagram").Select(element => element.Value)
			.ShouldBe(["Gshape G-B-D", "D² A-D-F#"]);
		GetClassElements(html, "chord-diagram").ShouldBeEmpty();
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
		const string TargetFramework = "net10.0";
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
		text.ShouldNotContain("white-space: normal");
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
		defaultScript.ShouldContain("event.key === \"PageUp\" || event.key === \"PageDown\"");
		defaultScript.ShouldContain("direction * pageHeight");
		defaultScript.ShouldContain("event.preventDefault()");
		defaultStyles.ShouldContain("--column-min-width: 18em");
		defaultStyles.ShouldContain("inline-size: max-content");
		defaultStyles.ShouldContain("color-scheme: light dark");
		defaultStyles.ShouldContain("--chord-color: light-dark(#3045c7, #91a2ff)");
		defaultStyles.ShouldContain("color-mix(in srgb, var(--diagram-line-color) 25%, Canvas)");
		defaultStyles.ShouldContain("color-mix(in srgb, var(--diagram-line-color) 50%, Canvas)");
		defaultStyles.ShouldContain("background-color: Canvas");
		defaultStyles.ShouldContain("color-scheme: light");
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
			DiagramFretColor = CssColor.Parse("gray"),
			DiagramDotColor = CssColor.Parse("navy"),
			DiagramKeyColor = CssColor.Parse("silver"),
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
			"--diagram-fret-color: gray;",
			"--diagram-dot-color: navy;",
			"--diagram-key-color: silver;",
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
		css.ShouldContain(".comment {");
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
		=> container.Descendants().Where(element => HasClass(element, className));

	private static bool HasClass(XElement element, string className)
		=> ((string?)element.Attribute("class"))?.Split(' ').Contains(className) == true;

	#endregion
}
