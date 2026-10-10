using System.Threading;
using Menees.Chords.Db;

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class SongTextConversionTests
{
	public TestContext TestContext { get; set; } = null!;

	[TestMethod]
	public void ConvertsBothDirectionsAndMixedSourceWithMetadata()
	{
		const string Source = "{title: Example}\r\nC     G\r\nHello world\r\n[Am]Again\r\n";
		string chordPro = SongTextConversion.Convert(Source, SourceFormat.ChordPro, this.TestContext.CancellationToken);
		chordPro.ShouldContain("{title: Example}");
		chordPro.ShouldContain("[C]Hello");
		chordPro.ShouldContain("[G]world");
		chordPro.ShouldContain("[Am]Again");
		string text = SongTextConversion.Convert(chordPro, SourceFormat.ChordOverText, this.TestContext.CancellationToken);
		text.ShouldContain("Hello world");
		text.ShouldNotContain("[C]");
		text.ShouldContain("Am");
		SongTextConversion.Convert(text, SourceFormat.ChordPro, this.TestContext.CancellationToken).ShouldContain("[Am]Again");
	}

	[TestMethod]
	public void RejectsUnsupportedTargetsAndCancellationAndPreservesBlankBuffer()
	{
		Should.Throw<ArgumentException>(() => SongTextConversion.Convert("Text", SourceFormat.OpenSongXml, this.TestContext.CancellationToken));
		Should.Throw<OperationCanceledException>(() => SongTextConversion.Convert("Text", SourceFormat.ChordPro, new CancellationToken(true)));
		SongTextConversion.Convert(" \r\n\t", SourceFormat.ChordPro, this.TestContext.CancellationToken).ShouldBe(" \r\n\t");
	}
}
