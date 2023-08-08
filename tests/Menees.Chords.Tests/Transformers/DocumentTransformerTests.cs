namespace Menees.Chords.Transformers;

#region Using Directives

using System.IO;

#endregion

[TestClass]
public class DocumentTransformerTests
{
	#region Public Methods

	[TestMethod]
	public void FlattenStaticTest()
	{
		Document document = TestUtility.LoadSwingLowSweetChariot();
		IReadOnlyList<Entry> entries = DocumentTransformer.Flatten(document.Entries);
		entries.Count.ShouldBe(18);

		// TODO: Test with includeAnnotations. [Bill, 8/7/2023]
	}

	[TestMethod]
	public void FlattenInstanceTest()
	{
		Document document = TestUtility.LoadSwingLowSweetChariot();
		DocumentTransformer transformer = new TestDocumentTransformer(document);
		transformer.Flatten().Document.ShouldNotBe(document);
		transformer.Document.Entries.Count.ShouldBe(18);
	}

	[TestMethod]
	public void SetEntriesTest()
	{
		Document document = TestUtility.LoadSwingLowSweetChariot();
		IReadOnlyList<Entry> entries = DocumentTransformer.Flatten(document.Entries);
		entries.Count.ShouldBe(18);
		DocumentTransformer transformer = new TestDocumentTransformer(document);
		transformer.SetEntries(entries).Document.ShouldNotBe(document);
		transformer.Document.Entries.Count.ShouldBe(18);
	}

	[TestMethod]
	public void SetFileNameTest()
	{
		Document document = TestUtility.LoadSwingLowSweetChariot();
		DocumentTransformer transformer = new TestDocumentTransformer(document);
		Path.GetFileName(document.FileName).ShouldBe("Swing Low Sweet Chariot.cho");

		transformer.SetFileName("Testing.txt").Document.ShouldNotBe(document);
		transformer.Document.FileName.ShouldBe("Testing.txt");

		transformer.SetFileName(null).Document.ShouldNotBe(document);
		transformer.Document.FileName.ShouldBeNull();
	}

	#endregion

	#region Private Types

	private sealed class TestDocumentTransformer : DocumentTransformer
	{
		public TestDocumentTransformer(Document document)
			: base(document)
		{
		}

		public TestDocumentTransformer(string text)
			: base(Document.Parse(text))
		{
		}
	}

	#endregion
}