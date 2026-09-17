using System.Collections.Concurrent;
using System.Xml.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Menees.Chords.Book.Maui.Platforms.Windows;

internal static class FluentIconSource
{
	private static readonly ConcurrentDictionary<string, string> Paths = new(StringComparer.Ordinal);

	public static PathIcon Create(string name)
	{
		string data = Paths.GetOrAdd(name, static icon =>
		{
			using Stream stream = typeof(FluentIconSource).Assembly.GetManifestResourceStream(
				$"Menees.Chords.Book.Maui.Resources.Fluent.{icon}.svg") ?? throw new InvalidOperationException($"Unknown Fluent icon: {icon}");
			XDocument svg = XDocument.Load(stream);
			return "F1 " + string.Join(" ", svg.Root!.Elements().Select(path => (string?)path.Attribute("d")));
		});
		const int IconSize = 20;
		XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
		XElement element = new(ns + "PathIcon", new XAttribute("Data", data), new XAttribute("Width", IconSize), new XAttribute("Height", IconSize));
		return (PathIcon)XamlReader.Load(element.ToString());
	}
}
