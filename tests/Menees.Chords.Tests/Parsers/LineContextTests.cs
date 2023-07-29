namespace Menees.Chords.Parsers;

[TestClass]
public sealed class LineContextTests
{
	#region Public Methods

	[TestMethod]
	public void LineNumber()
	{
		int expectedLineNumber = 0;

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
			context.LineNumber.ShouldBe(++expectedLineNumber);
			return new LyricLine(context.LineText);
		}
	}

	[TestMethod]
	public void CreateLexer()
	{
		LineContext context = Create("Test");

		// Consecutive calls should get the same instance due to caching.
		Lexer lexer1 = context.CreateLexer();
		Lexer lexer2 = context.CreateLexer();
		lexer2.ShouldBe(lexer1);
		lexer1.Read().ShouldBeTrue();
		lexer1.Token.ShouldBe(new Token("Test", TokenType.Text, 0));

		// Reset should have been called.
		Lexer lexer3 = context.CreateLexer();
		lexer3.ShouldBe(lexer1);
		lexer3.Token.ShouldBe(default);
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

		result.ShouldNotBeNull("No LineContext was provided (e.g., for an empty line).");
		return result;
	}

	#endregion
}
