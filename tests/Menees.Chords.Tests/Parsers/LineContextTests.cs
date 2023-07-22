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
		doc.Entries[0].ShouldBeOfType<TextLine>();
		doc.Entries[3].ShouldBeOfType<BlankLine>();

		TextLine CheckContext(LineContext context)
		{
			context.Parser.ShouldBe(parser);

			// Should be 1-based line number.
			context.LineNumber.ShouldBe(context.Entries.Count + 1);
			switch (context.LineNumber)
			{
				case 1:
				case 3:
					context.LineText.ShouldBe($"Line {context.LineNumber}");
					break;

				case 2:
					context.LineText.ShouldBe("Line    2"); // Tab should have expanded to 4 spaces.
					break;

				default:
					// Note: The final whitespace line won't be passed to CheckContext.
					throw new InvalidOperationException("We shouldn't see other line numbers.");
			}

			return new TextLine(context.LineText);
		}
	}

	#endregion

	#region Internal Methods

	internal static LineContext Create(string line)
	{
		LineContext? result = null;

		DocumentParser parser = new(new[] { SaveContext });
		Document doc = Document.Parse(line, parser);

		TextLine SaveContext(LineContext context)
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
