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
		Chord.TryParse("Nope", out _).ShouldBeFalse();

		Chord.TryParse("A#b13", out Chord? chord).ShouldBeTrue();
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
		Test("B#*", "C*");
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

	[TestMethod]
	public void ChangeNotationTest()
	{
		Key key = Key.Parse("E");
		Chord chord = Chord.Parse("F#m7/C#*");
		Chord nashville = chord.ChangeNotation(Notation.Nashville, key);
		nashville.Name.ShouldBe("2m7/6*");
		Chord roman = nashville.ChangeNotation(Notation.Roman, key);
		roman.Name.ShouldBe("ii7/VI*");
		roman.ChangeNotation(Notation.Name, key).Name.ShouldBe(chord.Name);
		nashville.ChangeNotation(Notation.Name, key).Name.ShouldBe(chord.Name);
		ReferenceEquals(chord, chord.ChangeNotation(Notation.Name, key)).ShouldBeTrue();
	}

	[TestMethod]
	public void TransposeTest()
	{
		Chord chord = Chord.Parse("C/E*");
		chord.Transpose(4).Name.ShouldBe("E/G#*");
		chord.Transpose(-4).Name.ShouldBe("Ab/C*");
		ReferenceEquals(chord, chord.Transpose(0)).ShouldBeTrue();
		Chord numeric = Chord.Parse("1/3");
		ReferenceEquals(numeric, numeric.Transpose(1)).ShouldBeTrue();
		Chord.Parse("bb").Transpose(1).Name.ShouldBe("b");
		chord.Transpose(13).Name.ShouldBe("C#/F*");
		chord.Transpose(-13).Name.ShouldBe("B/Eb*");
		chord.Transpose(1, AccidentalPreference.Flats).Name.ShouldBe("Db/F*");
		chord.Transpose(-1, AccidentalPreference.Sharps).Name.ShouldBe("B/D#*");
		ReferenceEquals(chord, chord.Transpose(120)).ShouldBeTrue();
		chord.Transpose(sbyte.MaxValue).Name.ShouldBe("G/B*");
		chord.Transpose(sbyte.MinValue).Name.ShouldBe("E/Ab*");
	}
}
