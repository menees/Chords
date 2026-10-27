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
	public void KeyOffsetCanDifferFromChordOffsetTest()
	{
		Document document = Document.Parse("{key: Db}\n[C]One [F]four [G/B]five\n{meta: key Eb}\n[D]Two");
		Document transformed = new TransposeTransformer(document, 1, AccidentalPreference.Flats)
		{
			KeyHalfSteps = 0,
		}.Transform().Document;

		transformed.Entries[0].ToString().ShouldBe("{key: Db}");
		transformed.Entries[1].ToString().ShouldBe("[Db]One [Gb]four [Ab/C]five");
		transformed.Entries[2].ToString().ShouldBe("{meta: key Eb}");
		transformed.Entries[3].ToString().ShouldBe("[Eb]Two");
		new Menees.Chords.Formatters.TextFormatter(document).ToString().ShouldContain("[C]One");

		Document keyOnly = new TransposeTransformer(document, 0) { KeyHalfSteps = 2 }.Transform().Document;
		keyOnly.Entries[0].ToString().ShouldBe("{key: D#}");
		keyOnly.Entries[1].ShouldBeSameAs(document.Entries[1]);
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
