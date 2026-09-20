#region Using Directives

using System.Collections.Concurrent;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

#endregion

namespace Menees.Chords.Book.Maui.Platforms.Windows;

internal static class FluentIconSource
{
	#region Private Data

	private static readonly ConcurrentDictionary<string, (string Path, double X, double Y, double Width, double Height)> Paths = new(StringComparer.Ordinal);

	#endregion

	#region Public Methods

	public static PathIcon Create(string name)
	{
		var data = Paths.GetOrAdd(name, static icon =>
		{
			using Stream stream = typeof(FluentIconSource).Assembly.GetManifestResourceStream(
				$"Menees.Chords.Book.Maui.Resources.Fluent.{icon}.svg")
				?? typeof(FluentIconSource).Assembly.GetManifestResourceStream($"Menees.Chords.Book.Maui.Resources.Icons.{icon}.svg")
				?? throw new InvalidOperationException($"Unknown icon: {icon}");
			using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
			XDocument svg = XDocument.Load(reader);
			XNamespace svgNamespace = "http://www.w3.org/2000/svg";
			string paths = string.Join(" ", svg.Descendants(svgNamespace + "path").Select(path => (string?)path.Attribute("d")));
			if (string.IsNullOrWhiteSpace(paths))
			{
				throw new InvalidOperationException($"Icon has no path geometry: {icon}");
			}

			const int ViewBoxLength = 4;
			double[] bounds = [.. ((string?)svg.Root?.Attribute("viewBox") ?? "0 0 20 20")
				.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
				.Select(value => double.Parse(value, CultureInfo.InvariantCulture))];
			if (bounds.Length != ViewBoxLength || bounds.Any(value => !double.IsFinite(value)) || bounds[2] <= 0 || bounds[3] <= 0)
			{
				throw new InvalidOperationException($"Icon has an invalid viewBox: {icon}");
			}

			return ("F1 " + paths, bounds[0], bounds[1], bounds[2], bounds[3]);
		});
		const int IconSize = 20;
		XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
		XElement element = new(ns + "PathIcon", new XAttribute("Data", data.Path), new XAttribute("Width", IconSize), new XAttribute("Height", IconSize));
		PathIcon result = (PathIcon)XamlReader.Load(element.ToString());
		double scale = IconSize / Math.Max(data.Width, data.Height);
		double offsetX = ((IconSize - (data.Width * scale)) / 2) - (data.X * scale);
		double offsetY = ((IconSize - (data.Height * scale)) / 2) - (data.Y * scale);
		Normalize((PathGeometry)result.Data, scale, offsetX, offsetY);
		return result;
	}

	#endregion

	#region Private Methods

	// Geometry.Transform reports correct Bounds but renders blank in WinUI PathIcon.
	// Normalize the actual points, preserving curves and arcs instead.
	private static void Normalize(PathGeometry geometry, double scale, double offsetX, double offsetY)
	{
		global::Windows.Foundation.Point Map(global::Windows.Foundation.Point point)
			=> new((point.X * scale) + offsetX, (point.Y * scale) + offsetY);

		void MapPoints(Microsoft.UI.Xaml.Media.PointCollection points)
		{
			for (int index = 0; index < points.Count; index++)
			{
				points[index] = Map(points[index]);
			}
		}

		foreach (PathFigure figure in geometry.Figures)
		{
			figure.StartPoint = Map(figure.StartPoint);
			foreach (PathSegment segment in figure.Segments)
			{
				switch (segment)
				{
					case LineSegment line:
						line.Point = Map(line.Point);
						break;
					case BezierSegment curve:
						curve.Point1 = Map(curve.Point1);
						curve.Point2 = Map(curve.Point2);
						curve.Point3 = Map(curve.Point3);
						break;
					case QuadraticBezierSegment curve:
						curve.Point1 = Map(curve.Point1);
						curve.Point2 = Map(curve.Point2);
						break;
					case ArcSegment arc:
						arc.Point = Map(arc.Point);
						arc.Size = new(arc.Size.Width * scale, arc.Size.Height * scale);
						break;
					case PolyLineSegment lines:
						MapPoints(lines.Points);
						break;
					case PolyBezierSegment curves:
						MapPoints(curves.Points);
						break;
					case PolyQuadraticBezierSegment curves:
						MapPoints(curves.Points);
						break;
					default:
						throw new InvalidOperationException($"Unsupported icon segment: {segment.GetType().Name}");
				}
			}
		}
	}

	#endregion
}
