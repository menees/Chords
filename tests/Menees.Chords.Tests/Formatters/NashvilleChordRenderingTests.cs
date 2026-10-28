namespace Menees.Chords.Formatters;

#region Using Directives

using System.Diagnostics;
using System.Xml.Linq;
using Menees.Chords.Transformers;

#endregion

[TestClass]
public sealed class NashvilleChordRenderingTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	[DataRow("G7", "57", "7")]
	[DataRow("Am", "6-", null)]
	[DataRow("Amin", "6-", null)]
	[DataRow("Am7", "6-7", null)]
	[DataRow("Amin7", "6-7", null)]
	[DataRow("A-7", "6-7", null)]
	[DataRow("Cmaj7", "1△", null)]
	[DataRow("CM7", "1△", null)]
	[DataRow("C^7", "1△", null)]
	[DataRow("Cmaj9", "1△9", null)]
	[DataRow("Bdim7", "7°7", null)]
	[DataRow("Caug", "1+", null)]
	[DataRow("Gsus4", "5sus4", null)]
	[DataRow("G7sus4", "57sus4", null)]
	[DataRow("G7b9", "57♭9", null)]
	[DataRow("G9", "59", null)]
	[DataRow("G13", "513", null)]
	[DataRow("F#m7b5", "♯4-7♭5", null)]
	[DataRow("Bb7/F", "♭77/4", "7")]
	[DataRow("G7*", "57*", "7")]
	public void OnlyAnUnqualifiedDominantSeventhIsSuperscriptWithoutChangingSource(string source, string expected, string? extension)
	{
		Document document = Document.Parse("{key: C}\n[" + source + "]Words");
		string original = new TextFormatter(document).ToString();
		Document numbered = new NotationTransformer(document, Notation.Nashville).Transform().Document;
		string projected = new TextFormatter(numbered).ToString();
		XDocument html = new HtmlFormatter(numbered).ToXDocument();
		XElement symbol = Symbols(html).Single();
		symbol.Value.ShouldBe(expected);
		symbol.Element("sup")?.Value.ShouldBe(extension);
		((string?)symbol.Element("sup")?.Attribute("class")).ShouldBe(extension is null ? null : "chord-extension");
		new TextFormatter(document).ToString().ShouldBe(original);
		new TextFormatter(numbered).ToString().ShouldBe(projected);
	}

	[TestMethod]
	[DataRow("[G7]Dominant [Am7]Minor [Cmaj7]Major")]
	[DataRow("G7       Am7   Cmaj7\nDominant minor major")]
	[DataRow("G7 Am7 Cmaj7\n\n")]
	[DataRow("{start_of_grid}\n| G7 . | Am7 . | Cmaj7 . |\n{end_of_grid}")]
	public void EveryChordLinePathUsesTheSameNashvilleTypography(string source)
	{
		Document document = Document.Parse("{key: C}\n" + source);
		Document numbered = new NotationTransformer(document, Notation.Nashville).Transform().Document;
		XDocument html = new HtmlFormatter(numbered).ToXDocument();
		XElement[] symbols = [.. Symbols(html)];
		symbols.Select(symbol => symbol.Value).ShouldBe(["57", "6-7", "1△"]);
		symbols.SelectMany(symbol => symbol.Elements("sup")).Select(element => element.Value).ShouldBe(["7"]);
	}

	[TestMethod]
	[DataRow("[*(][G7][*)]")]
	[DataRow("(G7) C\n\n")]
	public void ParenthesesAndAnnotationsStayOutsideTheExtension(string source)
	{
		Document document = Document.Parse("{key: C}\n" + source);
		Document numbered = new NotationTransformer(document, Notation.Nashville).Transform().Document;
		XElement chord = Symbols(new HtmlFormatter(numbered).ToXDocument()).First();
		chord.Value.ShouldBe("(57)");
		chord.Element("sup")!.Value.ShouldBe("7");
		chord.Nodes().OfType<XText>().Select(text => text.Value).ShouldBe(["(5", ")"]);
	}

	[TestMethod]
	public void LargeNumberedChartKeepsEveryChordAndRecordsFormattingWork()
	{
		const int Lines = 500;
		Document source = Document.Parse("{key: C}\n" + string.Concat(Enumerable.Repeat("[G7]Dominant [Am]minor [Cmaj7]major [C]root\n", Lines)));
		Document numbered = new NotationTransformer(source, Notation.Nashville).Transform().Document;
		long before = GC.GetAllocatedBytesForCurrentThread();
		Stopwatch timer = Stopwatch.StartNew();
		XDocument html = new HtmlFormatter(numbered, new() { ResponsivePages = false }).ToXDocument();
		long elapsed = timer.ElapsedMilliseconds;
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Symbols(html).Count().ShouldBe(Lines * 4);
		html.Descendants("sup").Count().ShouldBe(Lines);
		this.TestContext.WriteLine($"2,000-chord Nashville HTML: {elapsed} ms, {allocated:N0} thread bytes; all chords/extensions retained.");
	}

	[TestMethod]
	[DataRow(Notation.Name, "G7", "Am", "Cmaj7")]
	[DataRow(Notation.Roman, "V7", "vi", "Imaj7")]
	public void OtherNotationsKeepTheirExistingChordNames(Notation notation, string dominant, string minor, string major)
	{
		Document document = Document.Parse("{key: C}\n[G7]One [Am]Two [Cmaj7]Three");
		document = new NotationTransformer(document, notation).Transform().Document;
		XDocument html = new HtmlFormatter(document).ToXDocument();
		Symbols(html).Select(element => element.Value).ShouldBe([dominant, minor, major]);
		html.Descendants("sup").ShouldBeEmpty();
	}

	#endregion

	#region Private Methods

	private static IEnumerable<XElement> Symbols(XDocument html) => html.Descendants("span").Where(element =>
		((string?)element.Attribute("class")) is "chord" or "chord-only" or "grid-chord");

	#endregion
}
