using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using Menees.Chords.Book.Application;

internal static class Program
{
	[STAThread]
	private static void Main()
	{
		var app = new System.Windows.Application();
		var browser = new WebView2();
		var window = new Window { Width = 1000, Height = 800, Left = -3000, Top = -3000, ShowInTaskbar = false, ShowActivated = false, Content = browser };
		window.Loaded += async (_, _) =>
		{
			var streams = new List<Stream>();
			var messages = new List<string>();
			var requests = new List<string>();
			var log = Path.Combine(AppContext.BaseDirectory, "smoke-result.txt");
			try
			{
				string output = Environment.GetEnvironmentVariable("CHORDBOOK_TEST_OUTPUT")!;
				string repo = Environment.GetEnvironmentVariable("CHORDBOOK_TEST_REPO")!;
				File.WriteAllBytes(Path.Combine(output, "fixture.pdf"), SyntheticPdf.Create(100));
				await browser.EnsureCoreWebView2Async();
				var core = browser.CoreWebView2;
				var assets = Environment.GetEnvironmentVariable("CHORDBOOK_TEST_ASSETS")!;
				core.SetVirtualHostNameToFolderMapping("assets.chordbook.invalid", assets, CoreWebView2HostResourceAccessKind.Allow);
				core.SetVirtualHostNameToFolderMapping("editor.chordbook.invalid", Path.Combine(Path.GetDirectoryName(assets)!, "Editor"), CoreWebView2HostResourceAccessKind.DenyCors);
				core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
				core.WebMessageReceived += (_, e) =>
				{
					if (e.TryGetWebMessageAsString() == "resize") { window.Width = 700; window.Height = 900; }
				};
				core.NavigationStarting += (_, e) =>
				{
					if (e.Uri.StartsWith("chordbook:"))
					{
						messages.Add(e.Uri);
						e.Cancel = true;
						if (e.Uri == "chordbook://command/7/NextViewport")
							_ = core.ExecuteScriptAsync("window.meneesChordsViewer.moveViewport(1)");
					}
				};
				core.WebResourceRequested += (_, e) =>
				{
					var uri = new Uri(e.Request.Uri);
					if (uri.Host is "assets.chordbook.invalid" or "editor.chordbook.invalid" || uri.Scheme == "blob" || uri.Scheme == "data") return;
					requests.Add(e.Request.Uri);
					Stream? stream = null;
					string type = "application/pdf";
					if (uri.Host == "chordbook.invalid" && uri.AbsolutePath == "/viewer.html")
					{
						type = "text/html";
						var html = File.ReadAllText(Path.Combine(assets, "viewer.html")).Replace("__ASSET_ORIGIN__", "https://assets.chordbook.invalid");
						html = html.Replace("</head>", "<script>window.viewerErrors=[];window.addEventListener('error',e=>viewerErrors.push(e.message));window.addEventListener('unhandledrejection',e=>viewerErrors.push(String(e.reason)));</script></head>");
						if (uri.Query == "?generation=7") html = TextViewerBridge.Attach(html, 7, false, new(98));
						if (uri.Query == "?generation=8") html = TextViewerBridge.Attach(html, 8, false, new(98), []);
						if (uri.Query == "?generation=3") html = TextViewerBridge.Attach(html, 3, true);
						stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
					}
					else if (uri.Host == "chordbook.invalid" && uri.AbsolutePath.StartsWith("/document/"))
					{
						stream = uri.AbsolutePath == "/document/2.pdf"
							? new MemoryStream(Encoding.ASCII.GetBytes("invalid PDF"))
							: File.OpenRead(Path.Combine(output, "fixture.pdf"));
					}
					if (stream is not null) streams.Add(stream);
					e.Response = core.Environment.CreateWebResourceResponse(stream, stream is null ? 404 : 200, stream is null ? "Blocked" : "OK", "Content-Type: " + type);
				};

				async Task Wait(string predicate)
				{
					var timer = Stopwatch.StartNew();
					while (await core.ExecuteScriptAsync(predicate) != "true")
					{
						if (timer.Elapsed > TimeSpan.FromSeconds(25)) throw new Exception("Timed out: " + predicate);
						await Task.Delay(25);
					}
				}
				async Task CheckScript(string filename)
				{
					await core.ExecuteScriptAsync(File.ReadAllText(Path.Combine(repo, "eng/tests", filename)));
					await Wait("typeof window.testResult === 'string'");
					string? result = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("window.testResult"));
					if (result != "PASS") throw new Exception(filename + ": " + result);
				}
				async Task Navigate(int generation)
				{
					foreach (var stream in streams) stream.Dispose();
					streams.Clear();
					core.Navigate("https://chordbook.invalid/viewer.html?generation=" + generation);
					await Wait($"location.search === '?generation={generation}' && document.querySelector('#page')?.max === '100'");
				}

				await Navigate(1);
				await CheckScript("DocumentViewerChecks.js");
				if (requests.Count(url => url == "https://chordbook.invalid/document/1.pdf") != 1) throw new Exception("PDF was downloaded more than once.");
				await Navigate(3);
				await Wait("document.querySelector('#page')?.value === '100'");
				await Navigate(7);
				await Wait("document.querySelector('#page')?.value === '99'");
				await core.ExecuteScriptAsync("window.dispatchEvent(new KeyboardEvent('keydown',{key:' ',bubbles:true}))");
				await Wait("document.querySelector('#page')?.value === '100'");
				await core.ExecuteScriptAsync("window.dispatchEvent(new KeyboardEvent('keydown',{key:' ',bubbles:true}))");
				var timer = Stopwatch.StartNew();
				while (!messages.Contains("chordbook://viewport/7/1") && timer.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(25);
				if (!messages.Contains("chordbook://viewport/7/1")) throw new Exception("Boundary message missing.");
				if (!messages.Contains("chordbook://ready/7")) throw new Exception("Ready message missing.");
				int commandCount = messages.Count(message => message.Contains("/NextViewport"));
				await core.ExecuteScriptAsync("window.dispatchEvent(new KeyboardEvent('keydown',{key:' ',repeat:true,bubbles:true}));document.querySelector('#page').dispatchEvent(new KeyboardEvent('keydown',{key:' ',bubbles:true}));");
				await Task.Delay(150);
				if (messages.Count(message => message.Contains("/NextViewport")) != commandCount) throw new Exception("Repeats or form fields triggered a command.");
				using (var capture = File.Create(Path.Combine(output, "native-viewer.png")))
					await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, capture);
				await Navigate(8);
				await Wait("document.querySelector('#page')?.value === '99'");
				await core.ExecuteScriptAsync("window.dispatchEvent(new KeyboardEvent('keydown',{key:'PageDown',bubbles:true}))");
				await Task.Delay(150);
				if (await core.ExecuteScriptAsync("document.querySelector('#page').value") != "\"99\"")
					throw new Exception("An unbound PageDown key bypassed the application input bindings.");
				core.Navigate("https://chordbook.invalid/viewer.html?generation=2");
				await Wait("document.querySelector('#message')?.textContent.startsWith('Could not open this PDF:') === true");
				if (await core.ExecuteScriptAsync("window.viewerErrors.length === 0") != "true") throw new Exception("Unhandled error opening invalid PDF.");

				string css = File.ReadAllText(Path.Combine(repo, "src/Menees.Chords/Formatters/Html/Formatter.css"));
				core.NavigateToString("<html><head><style>" + css + "</style></head><body id='tablature-test'></body></html>");
				await Wait("document.body?.id === 'tablature-test'");
				await CheckScript("HtmlTablatureChecks.js");
				core.Navigate("https://editor.chordbook.invalid/editor.html");
				await Wait("typeof window.chordBookEditor === 'object'");
				await CheckScript("SongEditorChecks.js");
				File.WriteAllText(Path.Combine(output, "editor-performance.txt"), await core.ExecuteScriptAsync("window.editorLoadMilliseconds"));
				if (requests.Any(url => !url.StartsWith("https://chordbook.invalid/") && !url.StartsWith("about:")))
					throw new Exception("The viewer/editor attempted an external request: " + string.Join(", ", requests));
				using (var capture = File.Create(Path.Combine(output, "native-editor.png")))
					await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, capture);
				File.WriteAllText(log, "PASS: 100-page PDF painting, bounded canvas, rapid commands, zoom/resize, scroll restoration, both boundaries, last-page load, invalid PDF, streamed native host and input bridge, tablature alignment/scrollbars (including regression sensitivity), offline CodeMirror text preservation/undo/redo/highlighting/search/read-only/10,000-line virtualization. No Node.js.");
			}
			catch (Exception ex) { File.WriteAllText(log, ex.ToString()); Environment.ExitCode = 1; }
			finally { browser.Dispose(); foreach (var stream in streams) stream.Dispose(); window.Close(); app.Shutdown(); }
		};
		app.Run(window);
	}
}
