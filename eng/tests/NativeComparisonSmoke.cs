using System.Runtime.InteropServices.WindowsRuntime;
using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui;
using Menees.Chords.Db;
using Microsoft.Web.WebView2.Core;

internal static class NativeComparisonSmoke
{
	public static async Task RunAsync(Microsoft.Maui.Controls.ContentPage host)
	{
		using FileSystemBookStore store = new(Path.Combine(AppContext.BaseDirectory, "DisposableComparison"));
		Guid device = Guid.NewGuid();
		BookLocation book = await store.CreateBookAsync("Native comparison", device);
		BookApplicationSession app = new();
		await app.ActivateAsync(store, book);
		Guid song = await app.CreateSongAsync("Disposable song", [], [], "Left\r\n[C]Shared\nOld\n", device);
		Guid left = app.Database!.SongFiles.Single().Id;
		string other = Path.Combine(AppContext.BaseDirectory, "right.cho");
		await File.WriteAllTextAsync(other, "Right\r\n[C]Shared\nNew\nAdded\n");
		await app.ImportSongFilesAsync(song, [other], device);
		Guid right = app.Database!.SongFiles.Single(file => file.Id != left).Id;
		BookSession session = new(app, store.GetDirectory(book), device);
		SongVersionComparison text = await app.CompareSongVersionsAsync(song, left, right);
		SongVersionComparisonPage textPage = new(text, session);
		await host.Navigation.PushModalAsync(textPage);
		Microsoft.Maui.Controls.WebView textView = GetViews(textPage).Single();
		CoreWebView2 textCore = await GetCoreAsync(textView);
		try
		{
			await WaitAsync(textCore, "document.querySelectorAll('.columns section').length === 2");
		}
		catch (Exception error)
		{
			var layout = (Microsoft.Maui.Controls.Grid)textPage.Content;
			throw new Exception(error.Message + "; loaded=" + textPage.IsLoaded + "; source=" + textView.Source?.GetType().Name
				+ "; status=" + layout.Children.OfType<Microsoft.Maui.Controls.Label>().Last().Text + "; uri=" + textCore.Source);
		}
		if (textCore.Profile.PreferredColorScheme != CoreWebView2PreferredColorScheme.Dark) throw new Exception("Text comparison did not inherit the app theme.");
		if (await textCore.ExecuteScriptAsync("document.querySelectorAll('.Changed').length > 0") != "true")
			throw new Exception("Native comparison did not highlight changed lines.");
		if (await textCore.ExecuteScriptAsync("document.querySelectorAll('textarea,input,[contenteditable]').length === 0") != "true")
			throw new Exception("Comparison unexpectedly offered editing.");
		using (FileStream capture = File.Create(Path.Combine(AppContext.BaseDirectory, "comparison-native-text.png")))
			await textCore.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, capture.AsRandomAccessStream());
		await host.Navigation.PopModalAsync();

		string pdfA = Path.Combine(AppContext.BaseDirectory, "left.pdf"), pdfB = Path.Combine(AppContext.BaseDirectory, "right.pdf");
		await File.WriteAllBytesAsync(pdfA, SyntheticPdf.Create(2));
		await File.WriteAllBytesAsync(pdfB, SyntheticPdf.Create(3));
		await app.ImportSongFilesAsync(song, [pdfA, pdfB], device);
		SongFile[] pdfs = app.Database!.SongFiles.Where(file => file.MediaKind == MediaKind.Pdf).ToArray();
		SongVersionComparison pair = await app.CompareSongVersionsAsync(song, pdfs[0].Id, pdfs[1].Id);
		SongVersionComparisonPage pdfPage = new(pair, session);
		await host.Navigation.PushModalAsync(pdfPage);
		var views = GetViews(pdfPage);
		if (views.Length != 2) throw new Exception("PDF comparison did not create two previews.");
		for (int index = 0; index < views.Length; index++)
		{
			CoreWebView2 core = await GetCoreAsync(views[index]);
			try
			{
				await WaitAsync(core, $"window.meneesChordsViewer?.getState().pageCount === {index + 2}");
			}
			catch (Exception error)
			{
				var layout = (Microsoft.Maui.Controls.Grid)pdfPage.Content;
				string message = await core.ExecuteScriptAsync("document.getElementById('message')?.textContent");
				throw new Exception(error.Message + "; status=" + layout.Children.OfType<Microsoft.Maui.Controls.Label>().Last().Text
					+ "; uri=" + core.Source + "; source=" + views[index].Source?.GetType().Name
					+ "; loadedFlag=" + typeof(SongVersionComparisonPage).GetField("loaded", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(pdfPage)
					+ "; viewer=" + DumpViewers(pdfPage) + "; PDF message=" + message);
			}
			await WaitAsync(core, "document.getElementById('page').max === '" + (index + 2) + "' && !document.getElementById('canvas').hidden");
			await core.ExecuteScriptAsync("document.getElementById('next').click()");
			await WaitAsync(core, "window.meneesChordsViewer?.getState().page === 1");
			if (await core.ExecuteScriptAsync("document.getElementById('canvas').width > 0 && !document.getElementById('canvas').hidden") != "true")
				throw new Exception("PDF comparison did not paint its canvas.");
			using FileStream capture = File.Create(Path.Combine(AppContext.BaseDirectory, $"comparison-native-pdf-{index}.png"));
			await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, capture.AsRandomAccessStream());
		}

		await host.Navigation.PopModalAsync();
		foreach (SongFile pdf in pdfs)
		{
			using FileStream exclusive = File.Open(Path.Combine(session.DirectoryPath, pdf.RelativePath), FileMode.Open, FileAccess.Read, FileShare.None);
		}

		SongVersionComparison mixed = await app.CompareSongVersionsAsync(song, left, pdfs[0].Id);
		SongVersionComparisonPage mixedPage = new(mixed, session);
		await host.Navigation.PushModalAsync(mixedPage);
		var mixedViews = GetViews(mixedPage);
		await WaitAsync(await GetCoreAsync(mixedViews[0]), "document.querySelector('pre')?.textContent.includes('Left')");
		await WaitAsync(await GetCoreAsync(mixedViews[1]), "window.meneesChordsViewer?.getState().pageCount === 2");
		await host.Navigation.PopModalAsync();
	}

	static string DumpViewers(SongVersionComparisonPage page)
	{
		var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
		var viewers = (System.Collections.IEnumerable)typeof(SongVersionComparisonPage).GetField("documentViewers", flags)!.GetValue(page)!;
		return string.Join(",", viewers.Cast<object>().Select(viewer =>
			"generation:" + viewer.GetType().GetField("generation", flags)!.GetValue(viewer)
			+ "/content:" + (viewer.GetType().GetField("content", flags)!.GetValue(viewer) is not null)
			+ "/core:" + (viewer.GetType().GetField("core", flags)!.GetValue(viewer) is not null)));
	}

	static Microsoft.Maui.Controls.WebView[] GetViews(SongVersionComparisonPage page)
	{
		var layout = (Microsoft.Maui.Controls.Grid)page.Content;
		return layout.Children.OfType<Microsoft.Maui.Controls.WebView>()
			.Concat(layout.Children.OfType<Microsoft.Maui.Controls.Grid>().SelectMany(grid => grid.Children.OfType<Microsoft.Maui.Controls.WebView>())).ToArray();
	}

	static async Task<CoreWebView2> GetCoreAsync(Microsoft.Maui.Controls.WebView view)
	{
		if (view.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 native) throw new Exception("Comparison WebView handler is unavailable.");
		for (int attempt = 0; attempt < 200 && native.CoreWebView2 is null; attempt++) await Task.Delay(50);
		CoreWebView2 core = native.CoreWebView2 ?? throw new Exception("Native core did not initialize.");
		if (!string.Equals(core.Environment.UserDataFolder, Path.Combine(AppContext.BaseDirectory, "LocalProfile"), StringComparison.OrdinalIgnoreCase))
			throw new Exception("Browser profile did not use the configured device-local directory.");
		return core;
	}

	static async Task WaitAsync(CoreWebView2 core, string expression)
	{
		for (int attempt = 0; attempt < 200; attempt++)
		{
			if (await core.ExecuteScriptAsync(expression) == "true") return;
			await Task.Delay(50);
		}
		throw new Exception("Native comparison did not become ready: " + expression);
	}
}
