namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

[TestClass]
public class ChordLineTests
{
	#region Public Methods

	[TestMethod]
	public void TryParseValid()
	{
		Test("Em          C     C/B          Am", "Em", "C", "C/B", "Am");
		Test("         Am               B", "Am", "B");
		Test("[D7]        [G]", "D7", "G");
		Test("C       C*   G*", "C", "C", "G");

		ChordLine line = Test("G  G2  D/F#  Em  C  Cmaj5 (2x)", "G", "G2", "D/F#", "Em", "C", "Cmaj5");
		line.Annotations.Count.ShouldBe(1);
		Comment comment = line.Annotations[0].ShouldBeOfType<Comment>();
		comment.Prefix.ShouldBe("(");
		comment.Text.ShouldBe("2x");
		comment.Suffix.ShouldBe(")");

		line = Test("C *          G     Am          Em    (*high e)", "C", "G", "Am", "Em");
		line.Annotations.Count.ShouldBe(1);
		comment = line.Annotations[0].ShouldBeOfType<Comment>();
		comment.Prefix.ShouldBe("(");
		comment.Text.ShouldBe("*high e");
		comment.Suffix.ShouldBe(")");

		static ChordLine Test(string text, params string[] expectedChordNames)
		{
			LineContext context = LineContextTests.Create(text);
			ChordLine? line = ChordLine.TryParse(context).ShouldNotBeNull(text);
			line.Segments.Count.ShouldBeGreaterThan(0);
			line.ToString().ShouldBe(text);
			line.Segments.OfType<ChordSegment>().Zip(expectedChordNames, (first, second) => (first, second))
				.All(pair => pair.first.Chord.Name == pair.second).ShouldBeTrue();
			return line;
		}
	}

	[TestMethod]
	public void TryParseInvalid()
	{
		Test("A test line");
		Test("From N.C.");
		Test("Am Q7");
		Test("  [A12]  ");

		static void Test(string text)
		{
			LineContext context = LineContextTests.Create(text);
			ChordLine.TryParse(context).ShouldBeNull();
		}
	}

	#endregion
}