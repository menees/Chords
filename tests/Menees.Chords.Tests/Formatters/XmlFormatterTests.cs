namespace Menees.Chords.Formatters;

using System.Diagnostics;
using System.Xml.Linq;

[TestClass]
public class XmlFormatterTests
{
	[TestMethod]
	public void ToStringTest()
	{
		Document document = TestUtility.LoadSwingLowSweetChariot();
		XmlFormatter formatter = new(document);
		string text = formatter.ToString();
		Debug.WriteLine(text);
		text.ShouldContain("{title: Swing Low Sweet Chariot}");
		string[] lines = text.Split('\n');
		lines.Length.ShouldBe(26);
		XElement.Parse(text).ShouldNotBeNull();
	}

	[TestMethod]
	public void ToXElementTest()
	{
		Document document = TestUtility.LoadSwingLowSweetChariot();
		XmlFormatter formatter = new(document);
		XElement element = formatter.ToXElement();
		Debug.WriteLine(element);
		element.Name.LocalName.ShouldBe(nameof(Document));
	}

	[TestMethod]
	public void AnnotatedTest()
	{
		Document document = TestUtility.LoadAnnotatedDoc();
		XmlFormatter formatter = new(document);
		string text = formatter.ToString();
		Debug.WriteLine(text);

		XElement.Parse(text).ShouldNotBeNull();

		text.ShouldBe(
			"""
			<Document>
			  <ChordLyricPair>
			    <ChordLine>
			      <ToString><![CDATA[      D ↓        G↑   D*  ]]></ToString>
			      <Comment><![CDATA[(* Use higher D second time)]]></Comment>
			      <ChordDefinitions><![CDATA[D* x57775]]></ChordDefinitions>
			    </ChordLine>
			    <LyricLine>
			      <ToString><![CDATA[Swing low, sweet chariot,  ]]></ToString>
			      <Comment><![CDATA[** Sing "low" as bass **]]></Comment>
			    </LyricLine>
			  </ChordLyricPair>
			  <ChordLine>
			    <ToString><![CDATA[A Bb B   ]]></ToString>
			    <Comment><![CDATA[(Half steps)]]></Comment>
			  </ChordLine>
			  <ChordLine>
			    <ToString><![CDATA[G  G2  D/F#  Em  C  Cmaj5 ]]></ToString>
			    <Comment><![CDATA[(2x)]]></Comment>
			  </ChordLine>
			</Document>
			""",
			StringCompareShould.IgnoreLineEndings);
	}
}