namespace Menees.Chords.Formatters;

#region Using Directives

using System.Diagnostics;
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
		text.ShouldBe("\tLine 1\r\n\tLine 2\r\n");
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
		lines.Length.ShouldBe(19);
		return lines;
	}

	#endregion
}