namespace Menees.Chords;

#region Using Directives

using System.IO;

#endregion

public static class TestUtility
{
	#region Private Data Members

	private static readonly Lazy<List<Document>> SampleDocumentCache = new(
		() => [.. GetSampleFileNames().Select(fileName => Document.Load(fileName))]);

	#endregion

	#region Public Properties

	public static string SwingLowSweetChariotFileName { get; } = GetSampleFileName("Swing Low Sweet Chariot.cho");

	public static IReadOnlyList<Document> SampleDocuments => SampleDocumentCache.Value;

	#endregion

	#region Public Methods

	public static string GetSampleFileName(string fileName)
		=> Path.Combine("Samples", fileName);

	public static IEnumerable<string> GetSampleFileNames()
		=> Directory.EnumerateFiles(GetSampleFileName(string.Empty)).Order();

	public static Document LoadSwingLowSweetChariot()
		=> Document.Load(SwingLowSweetChariotFileName);

	public static Document LoadAnnotatedDoc()
	{
		Document result = Document.Parse(
			"""
			      D ↓        G↑   D*  (* Use higher D second time) D* = x57775
			Swing low, sweet chariot,  ** Sing "low" as bass **
			A Bb B   (Half steps)
			G  G2  D/F#  Em  C  Cmaj5 (2x)
			""");
		result.Entries.All(e => e.Annotations.Count >= 0).ShouldBeTrue();
		return result;
	}

	#endregion
}
