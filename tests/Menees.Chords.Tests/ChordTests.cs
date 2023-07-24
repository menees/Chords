namespace Menees.Chords;

[TestClass]
public class ChordTests
{
	[TestMethod]
	public void Parse()
	{
		Chord.Parse("d").Name.ShouldBe("d");
		Chord.Parse("Bb").Name.ShouldBe("Bb");

		Should.Throw<FormatException>(() => Chord.Parse("A/Q"));
	}

	[TestMethod]
	public void TryParse()
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
}