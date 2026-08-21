namespace Menees.Chords.Formatters;

#region Using Directives

using System.Text;
using Menees.Chords.Formatters.Html;

#endregion

[TestClass]
public class CssValueTests
{
	#region Public Methods

	[TestMethod]
	public void ColorTest()
	{
		CssColor.Parse("#3045c7").ToString().ShouldBe("#3045c7");
		CssColor.Parse("oklch(45% 0.2 275)").ToString().ShouldBe("oklch(45% 0.2 275)");
		CssColor.Parse("var(--chord-color)").ToString().ShouldBe("var(--chord-color)");
		CssColor.FromRgb(48, 69, 199).ToString().ShouldBe("rgb(48 69 199)");
		CssColor.TryParse("red; } body { color: red", out _).ShouldBeFalse();
	}

	[TestMethod]
	public void FontFamilyTest()
	{
		CssFontFamily.FromName("Roboto Flex").ToString().ShouldBe("\"Roboto Flex\"");
		CssFontFamily.FromNames("Inter", "Arial", "sans-serif").ToString()
			.ShouldBe("\"Inter\", \"Arial\", sans-serif");
		CssFontFamily.Parse("var(--app-font, \"Segoe UI\", sans-serif)").ToString()
			.ShouldBe("var(--app-font, \"Segoe UI\", sans-serif)");
		CssFontFamily.TryParse("Arial, , sans-serif", out _).ShouldBeFalse();
		CssFontFamily.TryParse("Arial; color: red", out _).ShouldBeFalse();
	}

	[TestMethod]
	public void SizeTest()
	{
		CssSize.Pixels(13).ToString().ShouldBe("13px");
		CssSize.Em(1.1).ToString().ShouldBe("1.1em");
		CssSize.Parse("calc(100dvh - 2rem)").ToString().ShouldBe("calc(100dvh - 2rem)");
		CssSize.Parse("min(20em, 50vw)").ToString().ShouldBe("min(20em, 50vw)");
		CssSize.Parse("max(var(--minimum), 18em)").ToString().ShouldBe("max(var(--minimum), 18em)");
		CssSize.TryParse("1.2", out _).ShouldBeFalse();
		CssSize.TryParse("expression(alert(1))", out _).ShouldBeFalse();
		CssSize.TryParse("1.2furlongs", out _).ShouldBeFalse();
	}

#if NET8_0_OR_GREATER
	[TestMethod]
	public void ModernParseInterfacesTest()
	{
		Parse<CssSize>("1.25rem").ShouldBe(CssSize.Rem(1.25));
		Parse<CssColor>("#3045c7").ShouldBe(CssColor.Parse("#3045c7"));
		Parse<CssFontFamily>("Arial, sans-serif").ShouldBe(CssFontFamily.Parse("Arial, sans-serif"));

		ParseSpan<CssSize>("calc(100% - 1em)").ToString().ShouldBe("calc(100% - 1em)");
		ParseSpan<CssColor>("rgb(1 2 3)").ToString().ShouldBe("rgb(1 2 3)");
		ParseSpan<CssFontFamily>("var(--song-font)").ToString().ShouldBe("var(--song-font)");
	}

	[TestMethod]
	public void Utf8ParseInterfacesTest()
	{
		ParseUtf8<CssSize>("20px").ShouldBe(CssSize.Pixels(20));
		ParseUtf8<CssColor>("oklch(45% 0.2 275)").ShouldBe(CssColor.Parse("oklch(45% 0.2 275)"));
		ParseUtf8<CssFontFamily>("Inter, sans-serif").ShouldBe(CssFontFamily.Parse("Inter, sans-serif"));

		byte[] invalidUtf8 = [0xC3, 0x28];
		CssFontFamily.TryParse(invalidUtf8, null, out _).ShouldBeFalse();
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

	#endregion
}
