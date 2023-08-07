namespace Menees.Chords;

using Menees.Chords.Parsers;

[TestClass]
public class CommentTests
{
	[TestMethod]
	public void CommentTest()
	{
		new Comment("Test").Text.ShouldBe("Test");
	}

	[TestMethod]
	public void TryParseTest()
	{
		Test("Not a comment", null);
		Test("I like C#", null);
		Test("f(x)", null);
		Test("Chord*", null);

		Test("# ChordPro remark #", "ChordPro remark #", "# ");
		Test("** Asterisk delimited **", "Asterisk delimited", "** ", " **");
		Test("(Parenthesized)", "Parenthesized", "(", ")");
		Test(" ( Check! ) ", "Check!", "( ", " )");

		static void Test(string text, string? expectedText, string? expectedPrefix = null, string? expectedSuffix = null)
		{
			LineContext context = LineContextTests.Create(text);
			Comment? comment = Comment.TryParse(context);
			if (expectedText == null)
			{
				comment.ShouldBeNull();
			}
			else
			{
				comment.ShouldNotBeNull();
				comment.Text.ShouldBe(expectedText);
				comment.Prefix.ShouldBe(expectedPrefix);
				comment.Suffix.ShouldBe(expectedSuffix);
				comment.ToString().ShouldBe(text.Trim());
			}
		}
	}
}