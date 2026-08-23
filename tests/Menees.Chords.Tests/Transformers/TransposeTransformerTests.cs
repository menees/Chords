namespace Menees.Chords.Transformers;

[TestClass]
public class TransposeTransformerTests
{
	[TestMethod]
	public void TransformTest()
	{
		Document document = Document.Parse(
			"""
			{key: C}
			[C]One [F]four [G/B]five
			{chord: Am frets x 0 2 2 1 0}
			{chord: [Am]}
			""");
		Document transformed = new TransposeTransformer(document, 2).Transform().Document;
		transformed.Entries[0].ToString().ShouldBe("{key: D}");
		transformed.Entries[1].ToString().ShouldBe("[D]One [G]four [A/C#]five");
		transformed.Entries[2].ToString().ShouldBe("{chord: Am frets x 0 2 2 1 0}");
		transformed.Entries[3].ToString().ShouldBe("{chord: [Bm]}");
	}

	[TestMethod]
	public void ChordDefinitionsRemainLiteralTest()
	{
		Document document = Document.Parse(
			"{key: C}\n      D ↓        G↑   D*  (* Use higher D second time) D* = x57775");
		Document transformed = new TransposeTransformer(document, 2).Transform().Document;
		ChordLine line = transformed.Entries.OfType<ChordLine>().Single();

		line.Segments.OfType<ChordSegment>().First().Chord.Name.ShouldBe("E");
		line.Annotations.OfType<ChordDefinitions>().Single().Definitions.Single().Chord.Name.ShouldBe("D*");
	}

	[TestMethod]
	public void ZeroHalfStepsTest()
	{
		Document document = Document.Parse("{key: C}\n[C]Lyrics");
		Document transformed = new TransposeTransformer(document, 0).Transform().Document;
		ReferenceEquals(document.Entries, transformed.Entries).ShouldBeTrue();
	}

	[TestMethod]
	public void ExplicitKeyAndAccidentalTest()
	{
		Document document = Document.Parse("[C]Lyrics");
		Document transformed = new TransposeTransformer(
			document,
			1,
			Key.Parse("C"),
			AccidentalPreference.Flats).Transform().Document;
		transformed.Entries.Single().ToString().ShouldBe("[Db]Lyrics");
	}
}
