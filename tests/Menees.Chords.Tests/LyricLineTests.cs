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
		LineContext context = LineContextTests.Create("Testing");
		LyricLine.Parse(context).Text.ShouldBe("Testing");

		context = LineContextTests.Create("Test (with comment)");
		LyricLine line = LyricLine.Parse(context);
		line.Text.ShouldBe("Test "); // Trailing whitespace is significant due to annotation.
		line.Annotations.Count.ShouldBe(1);
		Comment comment = line.Annotations[0].ShouldBeOfType<Comment>();
		comment.Text.ShouldBe("with comment");
		comment.Prefix.ShouldBe("(");
		comment.Suffix.ShouldBe(")");
	}
}