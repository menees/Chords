﻿namespace Menees.Chords.Formatters;

using System.Diagnostics;
using System.Xml.Linq;

[TestClass]
public class XmlFormatterTests
{
	// TODO: Test with annotations. [Bill, 8/12/2023]
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
}