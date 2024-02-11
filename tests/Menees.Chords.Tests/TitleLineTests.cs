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
	public void TryParseValidTest()
	{
		Test("This is a test", ("title", "This is a test"));
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

		Test(
			"Hey There Delilah – Plain White T’s. Bpm: 104. Fingerpick, alternate bass note with double stops. UG.com.",
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

		Test(
			"Pretty Little Lie Chords by Blackberry Smoke",
			("title", "Pretty Little Lie"),
			("artist", "Blackberry Smoke"));

		Test(
			"Free Bird Official by Lynyrd Skynyrd. Key: G. Bpm: 110,150.",
			("title", "Free Bird"),
			("artist", "Lynyrd Skynyrd"),
			("key", "G"),
			("tempo", "110,150"));

		static void Test(string text, params (string Name, string Argument)[] expected)
		{
			LineContext context = LineContextTests.Create(text);
			TitleLine line = TitleLine.TryParse(context).ShouldNotBeNull();
			line.Text.ShouldBe(text);
			line.Metadata.Select(metadata => (metadata.Name, metadata.Argument)).ShouldBe(expected);
		}
	}

	[TestMethod]
	public void TryParseInvalidTest()
	{
		// TitleLine.TryParse will allow anything for line 1, and it won't try to parse any other lines.
		// That's why in the default line parser list it has a very low priority.
		DocumentParser parser = new(new Func<LineContext, Entry?>[] { TitleLine.TryParse, LyricLine.Parse });
		Document doc = Document.Parse("Line 1\nLine 2", parser);
		doc.Entries[0].ShouldBeOfType<TitleLine>().Text.ShouldBe("Line 1");
		doc.Entries[1].ShouldBeOfType<LyricLine>().Text.ShouldBe("Line 2");
	}

	#endregion
}