namespace Menees.Chords;

[TestClass]
public class ChordTests
{
	[TestMethod]
	public void ParseTest()
	{
		Chord.Parse("d").Name.ShouldBe("d");
		Chord.Parse("Bb").Name.ShouldBe("Bb");

		Should.Throw<FormatException>(() => Chord.Parse("A/Q"));
	}

	[TestMethod]
	public void TryParseTest()
	{
		Chord.TryParse("Nope", out Chord? chord).ShouldBeFalse();

		Chord.TryParse("A#b13", out chord).ShouldBeTrue();
		chord.Name.ShouldBe("A#b13");
	}

	[TestMethod]
	public void ToStringTest()
	{
		Chord chord = Chord.Parse("A/C#");
		chord.Name.ShouldBe("A/C#");
		chord.ToString().ShouldBe(chord.Name);
	}

	[TestMethod]
	public void NormalizeTest()
	{
		Test("B#", "C");
		Test("E#/Cb", "F/B");
		Test("fb/b#", "e/c");

		Test("B/C#");
		Test("A/C#");
		Test("VII");
		Test("4/2");

		static void Test(string text, string? expectNormalized = null)
		{
			Chord chord = Chord.Parse(text);
			Chord normalized = chord.Normalize();
			if (expectNormalized == null)
			{
				ReferenceEquals(chord, normalized).ShouldBeTrue();
			}
			else
			{
				normalized.Name.ShouldNotBe(chord.Name);
				normalized.Name.ShouldBe(expectNormalized);
			}
		}
	}
}