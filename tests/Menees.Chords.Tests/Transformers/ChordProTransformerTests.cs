namespace Menees.Chords.Transformers;

#region Using Directives

using System.Diagnostics;
using System.IO;
using Menees.Chords.Formatters;
using Shouldly;

#endregion

[TestClass]
public class ChordProTransformerTests
{
	#region Public Methods

	[TestMethod]
	public void ConvertSamplesTest()
	{
		TestSamples("Expected ChordPro", doc => new ChordProTransformer(doc));
	}

	[TestMethod]
	public void GroupTabEnvironmentTest()
	{
		const string TabLines = """
			e|--------------------------------------------------------|
			B|--------------------------------------------------------|
			G|-----0---2/4-----2/4-4-2-0-0h2-----2-0---0--------------|
			D|--/2---2-------------------------------4---4-0-0h2------|

			e|-----------------5/7-5-3-3h5------5-7-8-7--------|
			B|------3-5-5--------------------------------------|
			G|--2/4--------------------------------------------|
			D|-------------------------------------------------|
			""";

		TestGroupEnvironment<TablatureLine>(TabLines, "tab");
	}

	[TestMethod]
	public void GroupGridEnvironmentTest()
	{
		// ChordProGridLine.TryParse only returns true inside of explicit start_of_grid/end_of_grid environments.
		// So, we can't test with an implicit environment because the lines would all parse as ChordLines without
		// the explicit directives.
		const string GridLines = """
			{start_of_grid}
			|| Am . . . | C . . . | D  . . . | F  . . . |
			|  Am . . . | C . . . | E  . . . | E  . . . |
			|  Am . . . | C . . . | D  . . . | F  . . . |
			|  Am . . . | E . . . | Am . . . | Am . . . |
			{end_of_grid}

			{start_of_grid}
			|  Am . . . | C . . . | D  . . . | F  . . . |
			|  Am . . . | C . . . | E  . . . | E  . . . |
			|  Am . . . | C . . . | D  . . . | F  . . . |
			|  Am . . . | E . . . | Am . . . | Am . . . ||
			{end_of_grid}
			""";

		TestGroupEnvironment<ChordProGridLine>(GridLines, "grid");
	}

	#endregion

	#region Internal Methods

	internal static void TestSamples(
		string expectedFolder,
		Func<Document, DocumentTransformer> createTransformer,
		string extension = ".cho")
	{
		foreach (Document original in TestUtility.SampleDocuments)
		{
			Test(original, createTransformer, out string text);

			string fileName = original.FileName.ShouldNotBeNull();
			string baseFolder = Path.GetDirectoryName(fileName) ?? string.Empty;
			string expectedFileName = Path.Combine(
				baseFolder,
				expectedFolder,
				Path.ChangeExtension(Path.GetFileName(fileName), extension));
			File.Exists(expectedFileName).ShouldBeTrue($"Expected file should exist: {expectedFileName}");
			string expectedText = File.ReadAllText(expectedFileName);
			text.ShouldBe(expectedText, StringCompareShould.IgnoreLineEndings);
		}
	}

	#endregion

	#region Private Methods

	private static Document Test(Document original, Func<Document, DocumentTransformer> createTransformer, out string text)
	{
		DocumentTransformer transformer = createTransformer(original);
		Document converted = transformer.Transform().Document;
		TextFormatter formatter = new(converted);
		text = formatter.ToString();

		Debug.WriteLine($"***** {original.FileName} *****");
		Debug.WriteLine(text);
		return converted;
	}

	private static void TestGroupEnvironment<T>(string text, string suffix)
	{
		Document original = Document.Parse(text);
		Document converted = Test(original, doc => new ChordProTransformer(doc), out _);
		converted.Entries.Count.ShouldBe(3);
		TestSection(converted.Entries[0].ShouldBeOfType<Section>());
		converted.Entries[1].ShouldBeOfType<BlankLine>();
		TestSection(converted.Entries[2].ShouldBeOfType<Section>());

		void TestSection(Section section)
		{
			section.Entries[0].ShouldBeOfType<ChordProDirectiveLine>().Name.ShouldBe($"start_of_{suffix}");
			section.Entries[^1].ShouldBeOfType<ChordProDirectiveLine>().Name.ShouldBe($"end_of_{suffix}");
			foreach (Entry entry in section.Entries.Skip(1).Take(section.Entries.Count - 2))
			{
				entry.ShouldBeOfType<T>();
			}
		}
	}

	#endregion
}