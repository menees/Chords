#region Using Directives

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class SongFileAnalyzerTests
{
	#region Private Data

	private const int InvalidUtf8Byte = 0xE9;

	#endregion

	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public void DetectsExtensionlessOpenSong()
	{
		SongFileAnalysis analysis = SongFileAnalyzer.Analyze(TestData.OpenSongBytes(), "battle belongs i101115");

		analysis.SourceFormat.ShouldBe(SourceFormat.OpenSongXml);
		analysis.Title.ShouldBe("Blessed Assurance");
		analysis.TextEncoding.ShouldBe("utf-8");
		analysis.ByteOrderMark.ShouldBe(ByteOrderMarkKind.None);
	}

	[TestMethod]
	public void DetectsPortableEncodingRules()
	{
		byte[] utf16 = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("{title:Unicode Song}")];
		byte[] latin1Bytes = [(byte)'T', (byte)'i', (byte)'t', (byte)'l', (byte)'e', (byte)':', (byte)' ', InvalidUtf8Byte];
		SongFileAnalysis bom = SongFileAnalyzer.Analyze(utf16, "unicode.cho");
		SongFileAnalysis latin1 = SongFileAnalyzer.Analyze(latin1Bytes, "latin1.txt");

		bom.ByteOrderMark.ShouldBe(ByteOrderMarkKind.Utf16LittleEndian);
		bom.TextEncoding.ShouldBe("utf-16");
		latin1.ByteOrderMark.ShouldBe(ByteOrderMarkKind.None);
		latin1.TextEncoding.ShouldBe("iso-8859-1");
	}

	[TestMethod]
	public async Task ImportPreservesExactSourceBytesAndExtensionlessName()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Imports", Guid.NewGuid(), cancellationToken);
		byte[] source = TestData.OpenSongBytes();

		BookImportResult imported = await BookImportService.ImportAsync(
			store,
			location,
			"battle belongs i101115",
			new MemoryStream(source, writable: false),
			Guid.NewGuid(),
			cancellationToken);

		Path.HasExtension(imported.RelativePath).ShouldBeFalse();
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		database.Songs.Single().Title.ShouldBe("Blessed Assurance");
		database.SongFiles.Single().SourceFormat.ShouldBe(SourceFormat.OpenSongXml);
		using Stream content = await store.OpenManagedAssetAsync(location, imported.SongFileId, cancellationToken);
		using MemoryStream copy = new();
		await content.CopyToAsync(copy, cancellationToken);
		copy.ToArray().ShouldBe(source);
	}

	#endregion
}
