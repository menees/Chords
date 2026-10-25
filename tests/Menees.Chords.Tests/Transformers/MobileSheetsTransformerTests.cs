namespace Menees.Chords.Transformers;

using Menees.Chords.Formatters;

[TestClass]
public class MobileSheetsTransformerTests
{
	[TestMethod]
	public void ConvertSamplesTest()
	{
		ChordProTransformerTests.TestSamples("Expected MobileSheets", doc => new MobileSheetsTransformer(doc));
	}

	[TestMethod]
	public void VerseLabelsArePreservedWithoutInventingDefaultsTest()
	{
		string[] starts =
		[
			"{sov}", "{start_of_verse}", "{sov: label=\"\"}",
			"{sov: part=\"guitar\"}", "{sov: Verse}", "{sov: label=\"Verse 2\"}",
		];
		foreach (string start in starts)
		{
			string text = $"{start}\n[C]Sing freely\n{{eov}}";
			Document document = Document.Parse(text);
			string original = new TextFormatter(document).ToString();
			Document transformed = new MobileSheetsTransformer(document).Transform().Document;

			new TextFormatter(transformed).ToString().ShouldBe(original);
		}
	}
}
