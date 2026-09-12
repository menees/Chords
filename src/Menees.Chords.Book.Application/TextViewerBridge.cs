using System.Globalization;

namespace Menees.Chords.Book.Application;

public static class TextViewerBridge
{
	public static string Attach(string html, int generation, bool startAtEnd)
	{
		string stamp = generation.ToString(CultureInfo.InvariantCulture);
		string script = $$"""
			<script>
			window.addEventListener('menees-chords-boundary', event => {
				if (event.detail.direction === -1 || event.detail.direction === 1) {
					location.href = 'chordbook://viewport/{{stamp}}/' + event.detail.direction;
				}
			});
			</script>
			""";
		if (startAtEnd)
		{
			script += "<script>window.addEventListener('menees-chords-layout', function end() {"
				+ "const viewer = window.meneesChordsViewer; viewer.goToPage(viewer.getState().pageCount - 1);"
				+ "window.removeEventListener('menees-chords-layout', end); });</script>";
		}

		return html.Replace("</head>", script + "</head>", StringComparison.Ordinal);
	}

	public static bool TryReadBoundary(string url, int generation, out int direction)
	{
		direction = 0;
		bool result = false;
		if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == "chordbook" && uri.Host == "viewport")
		{
			string[] parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
			result = parts.Length == 2 && int.TryParse(parts[0], CultureInfo.InvariantCulture, out int stamp) && stamp == generation
				&& int.TryParse(parts[1], CultureInfo.InvariantCulture, out direction) && direction is -1 or 1;
		}

		return result;
	}
}
