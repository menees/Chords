namespace Menees.Chords.Parsers;

[TestClass]
public sealed class LineContextTests
{
	#region Public Methods

	[TestMethod]
	public void LineNumber()
	{
		// The parser variable must be assigned something first so the CheckContext local method can safely capture the variable.
		DocumentParser parser = null!;
		parser = new(new[] { CheckContext });

		Document doc = Document.Parse("Line 1\nLine\t2\r\nLine 3\n  ", parser);
		doc.ShouldNotBeNull();
		doc.Entries.Count.ShouldBe(4);
		doc.Entries[0].ShouldBeOfType<LyricLine>().Text.ShouldBe("Line 1");
		doc.Entries[1].ShouldBeOfType<LyricLine>().Text.ShouldBe("Line    2"); // Tab should have expanded to 4 spaces.
		doc.Entries[2].ShouldBeOfType<LyricLine>().Text.ShouldBe("Line 3");
		doc.Entries[3].ShouldBeOfType<BlankLine>();

		LyricLine CheckContext(LineContext context)
		{
			context.Parser.ShouldBe(parser);

			// Should be 1-based line number.
			context.LineNumber.ShouldBe(context.Entries.Count + 1);
			return new LyricLine(context.LineText);
		}
	}

	#endregion

	#region Internal Methods

	internal static LineContext Create(string line)
	{
		LineContext? result = null;

		DocumentParser parser = new(new[] { SaveContext });
		Document doc = Document.Parse(line, parser);

		LyricLine SaveContext(LineContext context)
		{
			if (result != null)
			{
				Assert.Fail("Multiple test lines are not supported.");
			}

			result = context;
			return new(line);
		}

		result.ShouldNotBeNull();
		return result;
	}

	#endregion
}
