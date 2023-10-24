namespace Menees.Chords.Formatters;

#region Using Directives

using System.Diagnostics;
using System.Text;
using Menees.Chords.Parsers;

#endregion

[TestClass]
public class TextFormatterTests
{
	#region Public Methods

	[TestMethod]
	public void TabIndentedTest()
	{
		string[] lines = Test("\t");
		lines.ShouldContain("\t# A simple ChordPro song.");
	}

	[TestMethod]
	public void SpecialIndentedTest()
	{
		string[] lines = Test("...@");
		lines.ShouldContain("...@{start_of_chorus}");
	}

	[TestMethod]
	public void UnindentedTest()
	{
		string[] lines = Test(null);
		lines.Any(line => line.Contains('\t')).ShouldBeFalse();
	}

	[TestMethod]
	public void IndentWithNewLineTest()
	{
		LyricLine line = new("Line 1\r\nLine 2");
		Section inner = new(new[] { line });
		Section outer = new(new[] { inner });
		TextFormatter formatter = new(outer, "\t");
		string text = formatter.ToString();
		Debug.WriteLine(text);
		text.ShouldBe("\tLine 1\r\n\tLine 2", StringCompareShould.IgnoreLineEndings);
	}

	[TestMethod]
	public void AnnotatedTest()
	{
		Document document = TestUtility.LoadAnnotatedDoc();
		TextFormatter formatter = new(document);
		string text = formatter.ToString();
		Debug.WriteLine(text);

		text.ShouldBe(
			"""
			      D ↓        G↑   D*  (* Use higher D second time) D* x57775
			Swing low, sweet chariot,  ** Sing "low" as bass **
			A Bb B   (Half steps)
			G  G2  D/F#  Em  C  Cmaj5 (2x)
			""",
			StringCompareShould.IgnoreLineEndings);
	}

	[TestMethod]
	public void TrimEndTest()
	{
		Test("Test Case  \t \r\n ", "Test Case");
		Test("Unchanged", "Unchanged");
		Test("  \r\n ", string.Empty);
		Test(string.Empty, string.Empty);

		static void Test(string text, string expected)
			=> TextFormatter.TrimEnd(new StringBuilder(text)).ToString().ShouldBe(expected);
	}

	#endregion

	#region Private Methods

	private static string[] Test(string? indent)
	{
		Document document = TestUtility.LoadSwingLowSweetChariot();
		TextFormatter formatter = new(document, indent);
		string text = formatter.ToString();
		Debug.WriteLine(text);
		text.ShouldContain("{title: Swing Low Sweet Chariot}");
		if (indent != null && !string.IsNullOrEmpty(indent))
		{
			text.ShouldContain(indent);
		}

		string[] lines = text.Split('\n').Select(line => line.TrimEnd()).ToArray();
		lines.Length.ShouldBe(18);
		return lines;
	}

	#endregion
}