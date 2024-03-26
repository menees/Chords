namespace Menees.Chords;

#region Using Directives

using System;
using System.Xml.Linq;
using Menees.Chords.Parsers;
using Shouldly;

#endregion

[TestClass]
public class TitleLineTests
{
	#region Public Methods

	[TestMethod]
	public void TryParseValidLineTest()
	{
		Test2("This is a test", "This is a test", ("title", "This is a test"));
		Test("Every Rose Has Its Thorn - Poison", ("title", "Every Rose Has Its Thorn"), ("artist", "Poison"));

		Test(
			"867-5309/Jenny - Tommy Tutone. Recorded sharp (A = 445).",
			("title", "867-5309/Jenny"),
			("artist", "Tommy Tutone"),
			("comment", "Recorded sharp (A = 445)"));

		Test(
			"Addicted To Love – Robert Palmer. Key: A. Bpm: 111. Ultimate-Guitar. Official.",
			("title", "Addicted To Love"),
			("artist", "Robert Palmer"),
			("key", "A"),
			("tempo", "111"),
			("comment", "Ultimate-Guitar"),
			("comment", "Official"));

		Test(
			"Angel of Harlem – U2. Key: C. Bpm: 101.",
			("title", "Angel of Harlem"),
			("artist", "U2"),
			("key", "C"),
			("tempo", "101"));

		Test(
			"Escape (Piña Colada) – Rupert Holmes. Key: F. Bpm: 140.",
			("title", "Escape (Piña Colada)"),
			("artist", "Rupert Holmes"),
			("key", "F"),
			("tempo", "140"));

		Test(
			"   Faith – George Michael. Key: B. Bpm: 96. ",
			("title", "Faith"),
			("artist", "George Michael"),
			("key", "B"),
			("tempo", "96"));

		Test2(
			"Hey There Delilah – Plain White T’s. Bpm: 104. Fingerpick, alternate bass note with double stops. UG.com.",
			"Hey There Delilah – Plain White T’s. Bpm: 104. Fingerpick. alternate bass note with double stops. UG.com.",
			("title", "Hey There Delilah"),
			("artist", "Plain White T’s"),
			("tempo", "104"),
			("comment", "Fingerpick"),
			("comment", "alternate bass note with double stops"),
			("comment", "UG.com"));

		Test(
			"In Case You Didn’t Know – Brett Young. Bpm: 74. Key: Bb. Capo @ 3.",
			("title", "In Case You Didn’t Know"),
			("artist", "Brett Young"),
			("tempo", "74"),
			("key", "Bb"),
			("comment", "Capo @ 3"));

		Test2(
			"Pretty Little Lie Chords by Blackberry Smoke",
			"Pretty Little Lie – Blackberry Smoke.",
			("title", "Pretty Little Lie"),
			("artist", "Blackberry Smoke"));

		Test2(
			"Free Bird by Lynyrd Skynyrd. Key: G.",
			"Free Bird – Lynyrd Skynyrd. Key: G.",
			("title", "Free Bird"),
			("artist", "Lynyrd Skynyrd"),
			("key", "G"));

		Test(
			"Free Bird. Key: G.",
			("title", "Free Bird"),
			("key", "G"));

		Test2(
			"Free Bird Official by Lynyrd Skynyrd. Key: G. Bpm: 110,150.",
			"Free Bird – Lynyrd Skynyrd. Key: G. Bpm: 110,150.",
			("title", "Free Bird"),
			("artist", "Lynyrd Skynyrd"),
			("key", "G"),
			("tempo", "110,150"));

		static void Test(string text, params (string Name, string Argument)[] expected)
		{
			// Replace normal dash with en-dash.
			string expectedMetadata = text.Trim().Replace(" - ", " – ");
			if (!expectedMetadata.EndsWith('.'))
			{
				expectedMetadata += '.';
			}

			Test2(text, expectedMetadata, expected);
		}

		static void Test2(string text, string expectedMetadata, params (string Name, string Argument)[] expected)
		{
			LineContext context = LineContextTests.Create(text);
			TitleLine line = TitleLine.TryParse(context).ShouldNotBeNull();
			line.Text.ShouldBe(text);
			line.Metadata.Select(metadata => (metadata.Name, metadata.Argument)).ShouldBe(expected);

			string metadata = line.ToMetadataString();
			metadata.ShouldBe(expectedMetadata, StringCompareShould.IgnoreCase);
		}
	}

	[TestMethod]
	public void TryParseInvalidLineTest()
	{
		// TitleLine.TryParse will allow anything for line 1, and it won't try to parse any other lines.
		// That's why in the default line parser list it has a very low priority.
		DocumentParser parser = new([TitleLine.TryParse, LyricLine.Parse]);
		Document doc = Document.Parse("Line 1\nLine 2", parser);
		doc.Entries[0].ShouldBeOfType<TitleLine>().Text.ShouldBe("Line 1");
		doc.Entries[1].ShouldBeOfType<LyricLine>().Text.ShouldBe("Line 2");
	}

	[TestMethod]
	public void TryParseValidUriTest()
	{
		Test("https://tabs.ultimate-guitar.com/tab/acdc/its-a-long-way-to-the-top-chords-59377", "Its A Long Way To The Top", "Acdc");
		Test("https://tabs.ultimate-guitar.com/tab/alice-cooper/be-my-lover-official-4068151", "Be My Lover", "Alice Cooper");
		Test("https://tabs.ultimate-guitar.com/tab/jelly-roll-us-nashville-tn/son-of-a-sinner-chords-3909071", "Son Of A Sinner", "Jelly Roll Us Nashville Tn");
		Test("https://tabs.ultimate-guitar.com/tab/u2/one-chords-1081745", "One", "U2");

		Test("https://www.songsterr.com/a/wsa/lynyrd-skynyrd-free-bird-chords-s21", "Lynyrd Skynyrd Free Bird");
		Test("https://www.songsterr.com/a/wsa/lynyrd-skynyrd-free-bird-tab-s21", "Lynyrd Skynyrd Free Bird");
		Test("https://www.songsterr.com/a/wsa/lynyrd-skynyrd-free-bird-tab-s21t0", "Lynyrd Skynyrd Free Bird");

		Test("https://m.e-chords.com/chords/lynyrd-skynyrd/freebird", "Freebird", "Lynyrd Skynyrd");
		Test("https://www.e-chords.com/tabs/lynyrd-skynyrd/free-bird", "Free Bird", "Lynyrd Skynyrd");

		Test("https://chordu.com/chords-tabs-dinah-washington-make-me-a-present-of-you-id_9txY8qbaLrE", "Dinah Washington Make Me A Present Of You");

		Test("https://www.yalp.io/chords/dinah-washington-make-me-a-present-of-you-a1f9", "Dinah Washington Make Me A Present Of You");

		Test("https://www.guitartabsexplorer.com/lynyrd-skynyrd-Tabs/free-bird-tab.php", "Free Bird", "Lynyrd Skynyrd");

		static void Test(string input, string expectedTitle, string? expectedArtist = null)
		{
			Uri uri = new(input);
			TitleLine titleLine = TitleLine.TryParse(uri).ShouldNotBeNull();
			titleLine.Metadata.Count.ShouldBe(expectedArtist != null ? 2 : 1);
			titleLine.Metadata[0].Name.ShouldBe("title");
			titleLine.Metadata[0].Argument.ShouldBe(expectedTitle);
			if (expectedArtist != null)
			{
				titleLine.Metadata[1].Name.ShouldBe("artist");
				titleLine.Metadata[1].Argument.ShouldBe(expectedArtist);
			}
		}
	}

	[TestMethod]
	public void TryParseInvalidUriTest()
	{
		Test("http://www.youtube.com"); // Non-transcription
		Test("https://tabs.ultimate-guitar.com/tab/267384"); // ID only
		Test("https://tabs.ultimate-guitar.com/tab/u2/one-chords"); // No ID

		static void Test(string text)
		{
			Uri uri = new(text);
			TitleLine.TryParse(uri).ShouldBeNull();
		}
	}

	#endregion
}