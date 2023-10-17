namespace Menees.Chords.Transformers.Tests;

[TestClass]
public class ChordOverLyricTransformerTests
{
	[TestMethod]
	public void ConvertSamplesTest()
	{
		ChordProTransformerTests.TestSamples(
			"Expected ChordOverLyric",
			doc =>
			{
				ChordProTransformer toChordPro = new(doc);
				Document chordPro = toChordPro.Transform().Document;
				ChordOverLyricTransformer result = new(chordPro);
				return result;
			},
			".txt");
	}
}