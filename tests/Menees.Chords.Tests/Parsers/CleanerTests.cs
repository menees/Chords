namespace Menees.Chords.Parsers;

#region Using Directives

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#endregion

[TestClass]
public class CleanerTests
{
	#region Public Methods

	[TestMethod]
	public void TrailingWhitespaceTest()
	{
		Test("Line 1   \n  Line 2\n  Line 3   \n", "Line 1\n  Line 2\n  Line 3");
	}

	[TestMethod]
	public void AlternatingOddBlankLinesTest()
	{
		Test("Line 1\n\nLine 3\n\nLine 5", "Line 1\nLine 3\nLine 5");
		Test("Line 1\n\nLine 3\n\nLine 5\n\n", "Line 1\nLine 3\nLine 5");
	}

	[TestMethod]
	public void AlternatingEvenBlankLinesTest()
	{
		Test("\nLine 2\n\nLine 4\n\nLine 6", "Line 2\nLine 4\nLine 6");
		Test("\nLine 2\n\nLine 4\n\nLine 6\n\n", "Line 2\nLine 4\nLine 6");
	}

	[TestMethod]
	public void ConsecutiveBlankLinesTest()
	{
		Test("Line 1\n\n\n\nLine 5\n\n\nLine 8\n\n", "Line 1\n\nLine 5\n\nLine 8");
		Test("\nLine 2\n\n\nLine 5\n\nLine 7", "Line 2\n\nLine 5\n\nLine 7");
	}

	[TestMethod]
	public void LeadingAndTrailingBlankLinesTest()
	{
		Test("\n\nLine 3\nLine 4", "Line 3\nLine 4");
		Test("Line 3\nLine 4\n\n\n\n", "Line 3\nLine 4");
		Test("\n\nLine 3\nLine 4\n\n\n\n", "Line 3\nLine 4");
	}

	[TestMethod]
	public void MultipleProblemsTest()
	{
		const string Input = """
			Line 1

			Line 3   
			


			Line 7

			""";

		const string Expected = """
			Line 1
			Line 3

			Line 7
			""";

		Test(Input, Expected);
	}

	[TestMethod]
	public void NoProblemsTest()
	{
		Test("Line 1\nLine 2\n\nLine 4\n\n[Outro]\nLine 7", "Line 1\nLine 2\n\nLine 4\n\n[Outro]\nLine 7");
	}

	[TestMethod]
	public void BadTrailingLines()
	{
		Test("Line 1\nLine 2\nX", "Line 1\nLine 2");
		Test("Line 1\nLine 2\nSet8", "Line 1\nLine 2");
		Test("Line 1\nLine 2\nSet8\nX", "Line 1\nLine 2");
	}

	[TestMethod]
	public void NormalizeSections()
	{
		Test("L1\n\n[Intro]\n\nL4", "L1\n\n[Intro]\nL4");
		Test("L1\n[Intro]\nL4", "L1\n\n[Intro]\nL4");
		Test("L1\n[Intro]\n\nL4", "L1\n\n[Intro]\nL4");
	}

	#endregion

	#region Private Methods

	private static void Test(string text, string expected)
	{
		Cleaner cleaner = new(text);
		cleaner.OriginalText.ShouldBe(text);

		// C#'s triple-quote strings will use \r\r\n for blank lines instead of \r\n\r\n.
		expected = expected.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
		cleaner.CleanText.ShouldBe(expected);
	}

	#endregion
}
