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
			""");
		Document transformed = new TransposeTransformer(document, 2).Transform().Document;
		transformed.Entries[0].ToString().ShouldBe("{key: D}");
		transformed.Entries[1].ToString().ShouldBe("[D]One [G]four [A/C#]five");
		transformed.Entries[2].ToString().ShouldBe("{chord: Bm frets x 0 2 2 1 0}");
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
