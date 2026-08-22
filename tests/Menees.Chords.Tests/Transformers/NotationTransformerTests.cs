namespace Menees.Chords.Transformers;

[TestClass]
public class NotationTransformerTests
{
	[TestMethod]
	public void TransformTest()
	{
		Document document = Document.Parse(
			"""
			{key: E}
			[E]One [A]four [B/D#]five
			{define: F#m frets x 0 4 2 2 2}
			{key: D}
			[D]One [G]four
			""");
		Document transformed = new NotationTransformer(document, Notation.Nashville).Transform().Document;
		transformed.Entries[0].ToString().ShouldBe("{key: E}");
		transformed.Entries[1].ToString().ShouldBe("[1]One [4]four [5/7]five");
		transformed.Entries[2].ToString().ShouldBe("{define: 2m frets x 0 4 2 2 2}");
		transformed.Entries[4].ToString().ShouldBe("[1]One [4]four");
	}

	[TestMethod]
	public void MetadataOnlyTest()
	{
		Document document = Document.Parse("C G\nLyrics");
		Should.Throw<InvalidOperationException>(
			() => new NotationTransformer(document, Notation.Roman, DetectKey.MetadataOnly).Transform());
	}
}
