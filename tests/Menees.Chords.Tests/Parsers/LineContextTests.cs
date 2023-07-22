namespace Menees.Chords.Parsers;

using Shouldly;

[TestClass]
public sealed class LineContextTests
{
	[TestMethod]
	public void LineNumber()
	{
		// The parser variable must be assigned something so the CheckContext local method can capture it.
		DocumentParser parser = null!;
		parser = new(new[] { CheckContext });

		Document doc = Document.Parse("Line 1\nLine\t2\r\nLine 3\n  ", parser);
		doc.ShouldNotBeNull();
		doc.Sections.Count.ShouldBe(1);
		doc.Sections[0].Entries.Count.ShouldBe(4);
		doc.Sections[0].Entries[0].ShouldBeOfType<TextLine>();
		doc.Sections[0].Entries[3].ShouldBeOfType<BlankLine>();

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
}
