namespace Menees.Chords.Transformers;

#region Using Directives

using System.Diagnostics;
using Menees.Chords.Formatters;

#endregion

[TestClass]
public class ChordProTransformerTests
{
	#region Public Methods

	[TestMethod]
	public void BringHimHomeTest()
	{
		// TODO: Test more. [Bill, 8/13/2023]
		Document original = TestUtility.LoadBringHimHome();
		Test(original);
	}

	[TestMethod]
	public void AloneWithYouTest()
	{
		// TODO: Test more. [Bill, 8/13/2023]
		Document original = TestUtility.LoadAloneWithYou();
		Test(original);
	}

	[TestMethod]
	public void EveryRoseHasItsThornTest()
	{
		// TODO: Test more. [Bill, 8/13/2023]
		Document original = TestUtility.LoadEveryRoseHasItsThorn();
		Test(original);
	}

	#endregion

	#region Private Methods

	private static Document Test(Document original)
	{
		ChordProTransformer transformer = new(original);
		Document converted = transformer.ToChordPro().Document;
		TextFormatter formatter = new(converted);
		string text = formatter.ToString();
		Debug.WriteLine(text);
		return converted;
	}

	#endregion
}