namespace Menees.Chords;

using Menees.Chords.Parsers;
using Shouldly;

[TestClass]
public class LyricLineTests
{
	[TestMethod]
	public void LyricLineTest()
	{
		LyricLine line = new("Test");
		line.Text.ShouldBe("Test");
	}

	[TestMethod]
	public void ParseTest()
	{
		Test("Testing");

		// Most parenthetical comments at the end of lyrics should not be treated as an annotation.
		// They're usually just harmony parts. In fact, the harmony part might be the whole line.
		Test("Make it last all night (make it last all night)");
		Test("If I had those golden dreams of my yesterday (yesterday)");
		Test("What a nice surprise (What a nice surprise)");
		Test("Jesus loves you more than you will know (Wo, wo, wo)");
		Test("A nation turns its lonely eyes to you (Woo, woo, woo)");
		Test("(Hey, hey, hey...hey, hey, hey)");

		// Trailing whitespace is significant due to annotations.
		Test("Test G 320033", "Test ", "G 320033");
		Test("Test ** with comment **", "Test ", "** with comment **");
		Test("Test ** with comment ** G 320033", "Test ", "** with comment **", "G 320033");
		Test("I love you (love you) (love you)  Cmaj7 = x32000", "I love you (love you) (love you) ", "Cmaj7 x32000");

		// If there's enough whitespace between the parenthetical comment and the lyric text,
		// then it will be considered an annotation Comment.
		Test(
			"And be a simple kind of man                              (only in acoustic version)",
			"And be a simple kind of man                              ",
			"(only in acoustic version)");

		static void Test(string text, string? expectedText = null, params string[] expectedAnnotations)
		{
			LineContext context = LineContextTests.Create(text);
			LyricLine line = LyricLine.Parse(context);
			line.Text.ShouldBe(expectedText ?? text);
			line.Annotations.Select(annotation => annotation.ToString()).ShouldBe(expectedAnnotations);
		}
	}
}