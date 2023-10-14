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
	}

	[TestMethod]
	public void FlattenStaticAnnotationsTest()
	{
		Document document = CreateAnnotatedDocument();
		DocumentTransformer.Flatten(document.Entries, includeAnnotations: false).Count.ShouldBe(3);
		IReadOnlyList<Entry> entries = DocumentTransformer.Flatten(document.Entries, includeAnnotations: true);
		CheckFlattenedAnnotations(entries);
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
	public void FlattenInstanceAnnotationsTest()
	{
		Document document = CreateAnnotatedDocument();
		DocumentTransformer transformer = new TestDocumentTransformer(document);
		transformer.Flatten(includeAnnotations: false).Document.Entries.Count.ShouldBe(3);
		CheckFlattenedAnnotations(transformer.Flatten(includeAnnotations: true).Document.Entries);
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

	#region Private Methods

	private static Document CreateAnnotatedDocument()
	{
		Document document = Document.Parse(
			"""
			[Verse]  ** Short and sweet **
			Dm/A
			Testing (with comment) Dm/A x00231
			""");
		document.Entries.Count.ShouldBe(1);
		return document;
	}

	private static void CheckFlattenedAnnotations(IReadOnlyList<Entry> entries)
	{
		entries.Count.ShouldBe(5);
		entries[0].ShouldBeOfType<HeaderLine>().Annotations.Count.ShouldBe(0);
		entries[1].ShouldBeOfType<Comment>().Annotations.Count.ShouldBe(0);
		entries[2].ShouldBeOfType<ChordLine>().Annotations.Count.ShouldBe(0);

		// This lyric line's parenthetical comment is considered lyrics (e.g., harmonies),
		// so it won't be flattened out as a separate Comment annotation entry.
		entries[3].ShouldBeOfType<LyricLine>().Annotations.Count.ShouldBe(0);
		entries[4].ShouldBeOfType<ChordDefinitions>().Annotations.Count.ShouldBe(0);
	}

	#endregion

	#region Private Types

	private sealed class TestDocumentTransformer : DocumentTransformer
	{
		public TestDocumentTransformer(Document document)
			: base(document)
		{
		}

		public override DocumentTransformer Transform() => this;
	}

	#endregion
}