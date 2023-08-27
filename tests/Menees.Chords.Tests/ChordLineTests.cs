namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;
using Shouldly;

#endregion

[TestClass]
public class ChordLineTests
{
	#region Public Methods

	[TestMethod]
	public void TryParseValidTest()
	{
		Test("Em          C     C/B          Am", "Em", "C", "C/B", "Am");
		Test("         Am               B", "Am", "B");
		Test(" D7        G  ", "D7", "G");
		Test("C       C*   G*", "C", "C*", "G*");

		// https://tabs.ultimate-guitar.com/tab/hinder/lips-of-an-angel-chords-455832
		Test("Bm7     Asus4     Gsus2~~~", "Bm7", "Asus4", "Gsus2~~~");

		// If we're not in a ChordPro sog/eog environment, then chord grid lines are chord lines.
		Test("| Am . | B . |", "Am", "B");

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

		line = Test("      D ↓        G↑   D*  (* Use higher D second time) D* = x57775", "D", "G↑", "D*");
		line.Annotations.Count.ShouldBe(2);
		comment = line.Annotations[0].ShouldBeOfType<Comment>();
		comment.Prefix.ShouldBe("(");
		comment.Text.ShouldBe("* Use higher D second time");
		comment.Suffix.ShouldBe(")");
		ChordDefinitions definition = line.Annotations[1].ShouldBeOfType<ChordDefinitions>();
		definition.Definitions.Count.ShouldBe(1);
		definition.Definitions[0].ToString().ShouldBe("D* x57775");

		static ChordLine Test(string text, params string[] expectedChordNames)
		{
			LineContext context = LineContextTests.Create(text);
			ChordLine line = ChordLine.TryParse(context).ShouldNotBeNull(text);
			line.Segments.Count.ShouldBeGreaterThan(0);
			line.Segments.OfType<ChordSegment>().Zip(expectedChordNames, (first, second) => (first, second))
				.All(pair => pair.first.Chord.Name == pair.second).ShouldBeTrue();
			if (line.Annotations.OfType<ChordDefinitions>().Any())
			{
				// ChordDefinition.ToString() omits the optional '='.
				text = text.Replace(" = ", " ");
			}

			line.ToString().ShouldBe(text);
			return line;
		}
	}

	[TestMethod]
	public void TryParseInvalidTest()
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