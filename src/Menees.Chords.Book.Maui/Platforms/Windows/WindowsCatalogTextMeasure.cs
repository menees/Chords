using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;
using NativeFontStyle = global::Windows.UI.Text.FontStyle;
using NativeFontWeights = Microsoft.UI.Text.FontWeights;
using NativeSize = global::Windows.Foundation.Size;

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Measures catalog text with WinUI's font and glyph metrics without realizing catalog rows.</summary>
internal sealed class WindowsCatalogTextMeasure
{
	private const int CacheLimit = 32768;
	private readonly Dictionary<(string Text, double Size, FontAttributes Attributes), double> cache = [];
	private readonly UISettings settings = new();
	private readonly TextBlock probe = new()
	{
		FontFamily = new FontFamily(CatalogCell.DefaultFontFamily),
		TextWrapping = TextWrapping.NoWrap,
	};

	private double textScale;

	internal void BeginPass()
	{
		double scale = this.settings.TextScaleFactor;
		if (this.textScale != scale)
		{
			this.cache.Clear();
			this.textScale = scale;
		}
	}

	internal double Measure(string text, double size, FontAttributes attributes)
	{
		var key = (text, size, attributes);
		if (!this.cache.TryGetValue(key, out double width))
		{
			this.probe.Text = text;
			this.probe.FontSize = size;
			this.probe.FontWeight = (attributes & FontAttributes.Bold) != 0 ? NativeFontWeights.Bold : NativeFontWeights.Normal;
			this.probe.FontStyle = (attributes & FontAttributes.Italic) != 0 ? NativeFontStyle.Italic : NativeFontStyle.Normal;
			this.probe.Measure(new NativeSize(double.PositiveInfinity, double.PositiveInfinity));
			width = Math.Ceiling(this.probe.DesiredSize.Width + (2 * CatalogCell.HorizontalPadding));
			if (this.cache.Count < CacheLimit)
			{
				this.cache.Add(key, width);
			}
		}

		return width;
	}
}
