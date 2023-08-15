namespace Menees.Chords.Parsers;

[TestClass]
public sealed class LineContextTests
{
	#region Public Methods

	[TestMethod]
	public void LineNumberTest()
	{
		int expectedLineNumber = 0;

		// The parser variable must be assigned something first so the CheckContext local method can safely capture the variable.
		DocumentParser parser = null!;
		parser = new(new[] { CheckContext }, DocumentParser.Ungrouped);

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
	public void CreateLexerTest()
	{
		LineContext context = Create("Test");

		// Consecutive calls should get the same instance due to caching.
		Lexer lexer1 = context.CreateLexer();
		Lexer lexer2 = context.CreateLexer();
		lexer2.ShouldBe(lexer1);
		lexer1.Read().ShouldBeTrue();
		lexer1.Token.ShouldBe(new Token("Test"));

		// Reset should have been called.
		Lexer lexer3 = context.CreateLexer();
		lexer3.ShouldBe(lexer1);
		lexer3.Token.ShouldBe(default);
	}

	[TestMethod]
	public void CreateLexerWithAnnotationsTest()
	{
		Test("This (a) has (comment)", "This (a) has ", new[] { "(comment)" });
		Test("This (also) has one ** here **", "This (also) has one ", new[] { "** here **" });
		Test("This has (a) Cmaj7/F=1-3-2-0-0-0", "This has ", new[] { "(a)" }, "Cmaj7/F 132000");
		Test("High chord: Dm x-x-12-10-10-10", "High chord: ", expectedDefinitions: "Dm x-x-12-10-10-10");
		Test("I play Em7 several ways: Em7 020000", "I play Em7 several ways: ", expectedDefinitions: "Em7 020000");
		Test("I play Em7 several ways: Em7 = 022030", "I play Em7 several ways: ", expectedDefinitions: "Em7 022030");
		Test("I play Em7 several ways: Em7 020003, Em7 = 020030", "I play Em7 several ways: ", expectedDefinitions: "Em7 020003, Em7 020030");
		Test("A Bm C Db F# 3x", "A Bm C Db F# ", new[] { "3x" });
		Test("A Bm C Db F#   x12", "A Bm C Db F#   ", new[] { "x12" });
		Test("A Bm C Db F#   x123", "A Bm C Db ", expectedDefinitions: "F# x123"); // Chord def NOT repeat 123x!

		Test("This doesn't. )", "This doesn't. )");
		Test("A Bm C Db F#", "A Bm C Db F#");
		Test("I also love this unnamed D chord ... x54030", "I also love this unnamed D chord ... x54030");

		static IReadOnlyList<Entry> Test(
			string text,
			string expectUnannotated,
			string[]? expectedComments = null,
			string? expectedDefinitions = null)
		{
			LineContext context = Create(text);
			Lexer lexer = context.CreateLexer(out IReadOnlyList<Entry> annotations);
			string unannotated = lexer.ReadToEnd();
			unannotated.ShouldBe(expectUnannotated);

			annotations.Count.ShouldBe((expectedComments?.Length ?? 0) + (expectedDefinitions != null ? 1 : 0));

			int commentIndex = 0;
			foreach (Entry annotation in annotations)
			{
				if (annotation is Comment comment)
				{
					comment.ToString().ShouldBe(expectedComments?[commentIndex++]);
				}
				else if (annotation is ChordDefinitions definitions)
				{
					definitions.ToString().ShouldBe(expectedDefinitions);
				}
				else
				{
					Assert.Fail($"Unexpected annotation type: {annotation} ({annotation.GetType()}).");
				}
			}

			return annotations;
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

		result.ShouldNotBeNull("No LineContext was provided (e.g., for an empty line).");
		return result;
	}

	#endregion
}
