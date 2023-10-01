namespace Menees.Chords;

using Menees.Chords.Parsers;

[TestClass]
public class ChordLyricPairTests
{
	[TestMethod]
	public void ToStringTest()
	{
		Test("      A D     E", "Well, I fight authority");

		// From "Alyssa Lies" tab. Make sure the defintion "lyrics" line isn't considered annotated.
		// https://tabs.ultimate-guitar.com/tab/jason-michael-carroll/alyssa-lies-chords-531384
		Test("Cadd9   Em      D       G", "x32033  x22000  xx0232  320033");

		static void Test(string chords, string lyrics)
		{
			LineContext context = LineContextTests.Create(chords);
			ChordLine chordLine = ChordLine.TryParse(context).ShouldNotBeNull();

			context = LineContextTests.Create(lyrics);
			LyricLine lyricLine = LyricLine.Parse(context);

			ChordLyricPair pair = new(chordLine, lyricLine);
			pair.Chords.ShouldBe(chordLine);
			pair.Lyrics.ShouldBe(lyricLine);
			pair.ToString().ShouldBe(chords + Environment.NewLine + lyrics);
		}
	}
}