namespace Menees.Chords;

using System.Text;

[TestClass]
public class KeyTests
{
	[TestMethod]
	public void ParseTest()
	{
		Key.Parse("C").IsMinor.ShouldBeFalse();
		Key.Parse("F#m").IsMinor.ShouldBeTrue();
		Key.Parse("Bbmin").Root.ShouldBe("Bb");
		Should.Throw<FormatException>(() => Key.Parse("C7"));
		Key.TryParse("4", out _).ShouldBeFalse();
	}

	[TestMethod]
	public void EqualityTest()
	{
		Key.Parse("f#").ShouldBe(Key.Parse("F#"));
		Key.Parse("F#m").ShouldNotBe(Key.Parse("F#"));
	}

	[TestMethod]
	public void TransposeTest()
	{
		Key key = Key.Parse("C");
		key.Transpose(13, AccidentalPreference.Sharps).Name.ShouldBe("C#");
		key.Transpose(-13, AccidentalPreference.Flats).Name.ShouldBe("B");
		ReferenceEquals(key, key.Transpose(120, AccidentalPreference.Default)).ShouldBeTrue();
		key.Transpose(sbyte.MaxValue, AccidentalPreference.Default).Name.ShouldBe("G");
		key.Transpose(sbyte.MinValue, AccidentalPreference.Default).Name.ShouldBe("E");
	}

	[TestMethod]
	public void FindTest()
	{
		Document document = Document.Parse(
			"""
			Am G
			First line
			C
			Last line
			""");
		Key.Find(document, DetectKey.MetadataOnly).ShouldBeNull();
		Key.Find(document, DetectKey.FirstChord)!.Name.ShouldBe("Am");
		Key.Find(document, DetectKey.LastChord)!.Name.ShouldBe("C");

		document = Document.Parse("{key: D}\nAm G\nLyrics");
		Key.Find(document, DetectKey.LastChord)!.Name.ShouldBe("D");
	}

#if NET8_0_OR_GREATER
	[TestMethod]
	public void ModernParseTest()
	{
		Parse<Key>("Eb").Name.ShouldBe("Eb");
		ParseSpan<Key>("Cm").Name.ShouldBe("Cm");
		ParseUtf8<Key>("F#").Name.ShouldBe("F#");
		Key.TryParse([0xC3, 0x28], null, out _).ShouldBeFalse();
	}

	private static T Parse<T>(string value)
		where T : IParsable<T>
		=> T.Parse(value, null);

	private static T ParseSpan<T>(ReadOnlySpan<char> value)
		where T : ISpanParsable<T>
		=> T.Parse(value, null);

	private static T ParseUtf8<T>(string value)
		where T : IUtf8SpanParsable<T>
		=> T.Parse(Encoding.UTF8.GetBytes(value), null);
#endif
}
