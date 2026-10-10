#region Using Directives

using System.Net;
using System.Text;
using System.Threading;

#endregion

namespace Menees.Chords.Book.Application;

/// <summary>Read-only, selectable text columns. Authored text is encoded and cannot inject markup or execute scripts.</summary>
public static class SongVersionComparisonHtml
{
	#region Public Methods

	public static string Create(SongVersionComparison comparison, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(comparison);
		string left = comparison.Left.Text ?? string.Empty, right = comparison.Right.Text ?? string.Empty;
		IReadOnlyList<SongDifferenceLine>? rows = null;
		string? notice = null;
		try
		{
			rows = SongTextDifference.Compare(left, right, cancellationToken);
		}
		catch (InvalidOperationException exception)
		{
			notice = exception.Message;
		}

		StringBuilder html = new();
		html.Append("""
			<!doctype html><html><head><meta charset="utf-8">
			<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
			<meta name="viewport" content="width=device-width, initial-scale=1">
			<style>
			:root {color-scheme:light dark;--added:#d7f4db;--removed:#ffe0e0;--changed:#fff0bd;--gap:#eee}
			body {margin:0;font:14px Consolas,monospace;background:Canvas;color:CanvasText}
			.columns {display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:8px}
			section {min-width:0;overflow-x:auto}
			pre {margin:0;line-height:1.5;white-space:pre}
			.line {min-height:1.5em;content-visibility:auto;contain-intrinsic-block-size:1.5em}
			.line::before {content:attr(data-number);display:inline-block;width:4em;margin-right:1em;text-align:right;
			color:GrayText;user-select:none}
			.Added {background:var(--added)} .Removed {background:var(--removed)} .Changed {background:var(--changed)}
			.gap {background:var(--gap);user-select:none}
			.notice {padding:8px;font-family:Segoe UI,sans-serif}
			@media(prefers-color-scheme:dark) {:root {--added:#183e25;--removed:#542525;--changed:#493d18;--gap:#292929}}
			</style></head><body>
			""");
		if (notice is not null)
		{
			html.Append("<p class=\"notice\">").Append(WebUtility.HtmlEncode(notice)).Append(" Full text is shown without highlighting.</p>");
		}

		html.Append("<div class=\"columns\">");
		AppendSide(html, rows, left, isLeft: true, cancellationToken);
		AppendSide(html, rows, right, isLeft: false, cancellationToken);
		html.Append("</div></body></html>");
		return html.ToString();
	}

	#endregion

	#region Private Methods

	private static void AppendSide(
		StringBuilder html, IReadOnlyList<SongDifferenceLine>? rows, string text, bool isLeft, CancellationToken cancellationToken)
	{
		html.Append("<section><pre>");
		if (rows is null)
		{
			html.Append(WebUtility.HtmlEncode(text));
		}
		else
		{
			foreach (SongDifferenceLine row in rows)
			{
				cancellationToken.ThrowIfCancellationRequested();
				int? number = isLeft ? row.LeftNumber : row.RightNumber;
				string? line = isLeft ? row.LeftText : row.RightText;
				html.Append("<div class=\"line ").Append(number is null ? "gap" : row.Kind.ToString())
					.Append("\" data-number=\"").Append(number?.ToString()).Append("\">")
					.Append(WebUtility.HtmlEncode(line)).Append("</div>");
			}
		}

		html.Append("</pre></section>");
	}

	#endregion
}
