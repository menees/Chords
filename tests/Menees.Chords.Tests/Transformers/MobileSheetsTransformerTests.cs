namespace Menees.Chords.Transformers;

[TestClass]
public class MobileSheetsTransformerTests
{
	[TestMethod]
	public void ConvertSamplesTest()
	{
		ChordProTransformerTests.TestSamples("Expected MobileSheets", doc => new MobileSheetsTransformer(doc));
	}
}