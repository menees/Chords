#region Using Directives

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Menees.Chords.Book.Application;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Streams;

#endregion

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Hosts bundled viewer assets and streams only the selected document into WebView2.</summary>
public sealed class WindowsDocumentViewer : IDocumentViewer
{
	#region Private Data

	private const string HostName = "chordbook.invalid";
	private const string AssetHostName = "assets.chordbook.invalid";
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private readonly WebView view;
	private readonly List<IRandomAccessStream> responseStreams = [];
	private CoreWebView2? core;
	private DocumentViewerContent? content;
	private string? viewerHtml;
	private int generation;
	private bool startAtEnd;

	#endregion

	#region Constructors

	public WindowsDocumentViewer(WebView view) => this.view = view;

	#endregion

	#region Public Methods

	public void ApplyTheme()
	{
		if (this.view.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 { CoreWebView2: not null } browser)
		{
			AppTheme theme = global::Microsoft.Maui.Controls.Application.Current?.UserAppTheme ?? AppTheme.Unspecified;
			browser.CoreWebView2.Profile.PreferredColorScheme = theme switch
			{
				AppTheme.Dark => CoreWebView2PreferredColorScheme.Dark,
				AppTheme.Light => CoreWebView2PreferredColorScheme.Light,
				_ => CoreWebView2PreferredColorScheme.Auto,
			};
		}
	}

	public async Task LoadAsync(
		DocumentViewerContent content,
		int generation,
		bool startAtEnd,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		this.Clear();
		this.generation = generation;
		this.content = content;
		this.startAtEnd = startAtEnd;
		if (content.OpenPdf is null)
		{
			string html = TextViewerBridge.Attach(content.Html ?? string.Empty, generation, startAtEnd, content.Position, content.InputBindings);
			this.view.Source = new HtmlWebViewSource { Html = html };
		}
		else
		{
			if (this.view.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 browser)
			{
				throw new InvalidOperationException("The sheet viewer is not available yet.");
			}

			await browser.EnsureCoreWebView2Async().AsTask(cancellationToken).ConfigureAwait(true);
			if (this.generation != generation || this.content != content)
			{
				return;
			}

			if (this.core != browser.CoreWebView2)
			{
				this.Disconnect();
				this.core = browser.CoreWebView2;
				string assets = Path.Combine(AppContext.BaseDirectory, "Viewer");
				this.core.SetVirtualHostNameToFolderMapping(AssetHostName, assets, CoreWebView2HostResourceAccessKind.Allow);
				this.core.AddWebResourceRequestedFilter($"https://{HostName}/*", CoreWebView2WebResourceContext.All);
				this.core.WebResourceRequested += this.HandleResourceRequested;

				// This small bundled template is read once; assign it before another load can reuse the core.
				this.viewerHtml = File.ReadAllText(Path.Combine(assets, "viewer.html"))
					.Replace("__ASSET_ORIGIN__", $"https://{AssetHostName}", StringComparison.Ordinal);
			}

			cancellationToken.ThrowIfCancellationRequested();
			if (this.generation == generation && this.content == content)
			{
				this.view.Source = new UrlWebViewSource { Url = $"https://{HostName}/viewer.html?generation={generation}" };
			}
		}
	}

	public async Task<DocumentViewerState?> GetStateAsync(CancellationToken cancellationToken = default)
	{
		string? json = await this.EvaluateAsync("JSON.stringify(window.meneesChordsViewer?.getState() ?? null)", cancellationToken).ConfigureAwait(true);
		DocumentViewerState? result = null;
		if (!string.IsNullOrWhiteSpace(json) && json is not ("null" or "undefined"))
		{
			// MAUI platforms can return the string or its JSON-encoded representation.
			if (json.StartsWith('"'))
			{
				json = JsonSerializer.Deserialize<string>(json) ?? "null";
			}

			result = JsonSerializer.Deserialize<DocumentViewerState>(json, JsonOptions);
		}

		return result;
	}

	public async Task MoveViewportAsync(int direction, CancellationToken cancellationToken = default)
	{
		if (direction is not (-1 or 1))
		{
			throw new ArgumentOutOfRangeException(nameof(direction));
		}

		_ = await this.EvaluateAsync(
			$"window.meneesChordsViewer?.moveViewport({direction.ToString(CultureInfo.InvariantCulture)})",
			cancellationToken).ConfigureAwait(true);
	}

	public async Task RestorePositionAsync(DocumentViewerPosition position, CancellationToken cancellationToken = default)
	{
		string json = JsonSerializer.Serialize(position, JsonOptions);
		_ = await this.EvaluateAsync(
			$"(() => {{ const v = window.meneesChordsViewer; if(v?.restorePosition) v.restorePosition({json}); else v?.goToPage({position.Page}); }})()",
			cancellationToken).ConfigureAwait(true);
	}

	public void Clear()
	{
		this.generation++;
		this.content = null;
		this.view.Source = new HtmlWebViewSource { Html = string.Empty };
		foreach (IRandomAccessStream stream in this.responseStreams)
		{
			stream.Dispose();
		}

		this.responseStreams.Clear();
	}

	#endregion

	#region Private Methods

	private void Disconnect()
	{
		this.core?.WebResourceRequested -= this.HandleResourceRequested;
		this.core = null;
	}

	private async Task<string?> EvaluateAsync(string script, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		int version = this.generation;
		string result = await this.view.EvaluateJavaScriptAsync($"window.chordBookGeneration === {version} ? ({script}) : null").ConfigureAwait(true);
		cancellationToken.ThrowIfCancellationRequested();
		return version == this.generation ? result : null;
	}

	private void HandleResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
	{
		Uri uri = new(args.Request.Uri);
		if (uri.AbsolutePath == "/viewer.html" || uri.AbsolutePath.StartsWith("/document/", StringComparison.Ordinal))
		{
			Stream? stream = null;
			string contentType = "application/pdf";
			try
			{
				if (args.Request.Method == "GET" && this.content is not null && this.responseStreams.Count < 2)
				{
					if (uri.AbsolutePath == "/viewer.html" && uri.Query == $"?generation={this.generation}" && this.viewerHtml is not null)
					{
						contentType = "text/html; charset=utf-8";
						string html = TextViewerBridge.Attach(
							this.viewerHtml, this.generation, this.startAtEnd, this.content.Position, this.content.InputBindings);
						stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
					}
					else if (uri.AbsolutePath == $"/document/{this.generation}.pdf" && this.content.OpenPdf is not null)
					{
						stream = this.content.OpenPdf();
					}
				}

				if (stream is not null)
				{
					IRandomAccessStream nativeStream = stream.AsRandomAccessStream();
					this.responseStreams.Add(nativeStream);
					args.Response = sender.Environment.CreateWebResourceResponse(
						nativeStream,
						(int)HttpStatusCode.OK,
						"OK",
						$"Content-Type: {contentType}\r\nCache-Control: no-store\r\nContent-Length: {stream.Length}");
				}
				else
				{
					args.Response = sender.Environment.CreateWebResourceResponse(null, (int)HttpStatusCode.NotFound, "Not Found", string.Empty);
				}
			}
			catch (IOException)
			{
				stream?.Dispose();
				args.Response = sender.Environment.CreateWebResourceResponse(null, (int)HttpStatusCode.NotFound, "Not Found", string.Empty);
			}
			catch (UnauthorizedAccessException)
			{
				stream?.Dispose();
				args.Response = sender.Environment.CreateWebResourceResponse(null, (int)HttpStatusCode.Forbidden, "Forbidden", string.Empty);
			}
		}
		else
		{
			args.Response = sender.Environment.CreateWebResourceResponse(null, (int)HttpStatusCode.NotFound, "Not Found", string.Empty);
		}
	}

	#endregion
}
