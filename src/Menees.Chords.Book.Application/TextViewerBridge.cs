#region Using Directives

using System.Globalization;
using System.Text.Json;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application;

public static class TextViewerBridge
{
	#region Public Methods

	public static string Attach(
		string html, int generation, bool startAtEnd, DocumentViewerPosition? position = null, IReadOnlyList<PerformanceKeyBinding>? inputBindings = null)
	{
		string stamp = generation.ToString(CultureInfo.InvariantCulture);
		string bindingsJson = JsonSerializer.Serialize(inputBindings ?? PerformanceKeyBinding.CreateDefaults(), JsonSerializerOptions.Web);
		string script = $$"""
			<script>
			window.chordBookGeneration = {{stamp}};
			const bindings = {{bindingsJson}};
			window.addEventListener('keydown', event => {
				if (event.target?.closest?.('input,textarea,select,[contenteditable="true"]')) return;
				const binding = bindings.find(item => item.gesture.key === event.key
					&& item.gesture.control === event.ctrlKey && item.gesture.alt === event.altKey
					&& item.gesture.shift === event.shiftKey && item.gesture.meta === event.metaKey);
				if (binding) {
					event.preventDefault();
					event.stopImmediatePropagation();
					if (!event.repeat) location.href = 'chordbook://command/{{stamp}}/' + binding.command;
				}
			}, true);
			window.addEventListener('menees-chords-layout', () => {
				location.href = 'chordbook://ready/{{stamp}}';
			}, { once: true });
			window.addEventListener('menees-chords-boundary', event => {
				if (event.detail.direction === -1 || event.detail.direction === 1) {
					location.href = 'chordbook://viewport/{{stamp}}/' + event.detail.direction;
				}
			});
			let positionTimer;
			function reportPosition() {
				clearTimeout(positionTimer);
				positionTimer = setTimeout(() => {
					const state = window.meneesChordsViewer?.getState();
					if (state?.pageCount > 0) location.href = 'chordbook://position/{{stamp}}/'
						+ [state.page, state.zoom ?? 1, state.horizontalProgress ?? 0, state.verticalProgress ?? 0].join('/');
				}, 100);
			}
			window.addEventListener('menees-chords-layout', reportPosition);
			window.addEventListener('scroll', reportPosition, true);
			</script>
			""";
		if (startAtEnd)
		{
			script += "<script>window.addEventListener('menees-chords-layout', function end() {"
				+ "const viewer = window.meneesChordsViewer; viewer.goToPage(viewer.getState().pageCount - 1);"
				+ "window.removeEventListener('menees-chords-layout', end); });</script>";
		}
		else if (position is not null)
		{
			string json = JsonSerializer.Serialize(position, JsonSerializerOptions.Web);
			script += "<script>window.addEventListener('menees-chords-layout', function restore() {"
				+ "window.removeEventListener('menees-chords-layout', restore); const viewer = window.meneesChordsViewer;"
				+ $"if(viewer.restorePosition) viewer.restorePosition({json}); else viewer.goToPage({position.Page});"
				+ "});</script>";
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

	public static bool IsReadyMessage(string url, int generation)
		=> Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == "chordbook" && uri.Host == "ready"
			&& uri.AbsolutePath == "/" + generation.ToString(CultureInfo.InvariantCulture);

	public static bool TryReadPosition(string url, int generation, out DocumentViewerPosition? position)
	{
		const int PositionPartCount = 5;
		position = null;
		if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == "chordbook" && uri.Host == "position")
		{
			string[] parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == PositionPartCount && int.TryParse(parts[0], CultureInfo.InvariantCulture, out int stamp) && stamp == generation
				&& int.TryParse(parts[1], CultureInfo.InvariantCulture, out int page) && page >= 0
				&& double.TryParse(parts[2], CultureInfo.InvariantCulture, out double zoom) && zoom is >= 1 and <= 2
				&& double.TryParse(parts[3], CultureInfo.InvariantCulture, out double horizontal) && horizontal is >= 0 and <= 1
				&& double.TryParse(parts[4], CultureInfo.InvariantCulture, out double vertical) && vertical is >= 0 and <= 1)
			{
				position = new(page, zoom, horizontal, vertical);
			}
		}

		return position is not null;
	}
	#endregion
}
