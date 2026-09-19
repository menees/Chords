#region Using Directives

using System.Text.Json;
using Menees.Chords.Book.Application;
using Microsoft.Web.WebView2.Core;

#endregion

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Hosts the offline editor and exchanges buffers only on explicit reads or replacements.</summary>
public sealed partial class WindowsSongTextEditor(WebView view) : ISongTextEditor
{
	#region Private Data

	private const string Origin = "https://editor.chordbook.invalid";
	private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private CoreWebView2? core;
	private bool disposed;
	private bool navigationStarted;

	#endregion

	#region Public Methods

	public event EventHandler? TextChanged;

	public event EventHandler? PreviewChanged;

	public async Task<string> GetPreviewTextAsync(CancellationToken cancellationToken = default)
		=> JsonSerializer.Deserialize<string>(await this.ExecuteAsync("getPreviewText()", cancellationToken).ConfigureAwait(true))
			?? throw new InvalidOperationException("The editor did not return its preview text.");

	public async Task FindAsync(bool replace, CancellationToken cancellationToken = default)
		{
		view.Focus();
		_ = await this.ExecuteAsync("search(" + (replace ? "true" : "false") + ")", cancellationToken).ConfigureAwait(true);
	}

	public async Task SetPreviewEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
		=> _ = await this.ExecuteAsync("setPreviewEnabled(" + (enabled ? "true" : "false") + ")", cancellationToken).ConfigureAwait(true);

	public async Task UndoAsync(CancellationToken cancellationToken = default)
		{
		view.Focus();
		_ = await this.ExecuteAsync("undo()", cancellationToken).ConfigureAwait(true);
	}

	public async Task RedoAsync(CancellationToken cancellationToken = default)
	{
		view.Focus();
		_ = await this.ExecuteAsync("redo()", cancellationToken).ConfigureAwait(true);
	}

	public async Task MatchUiFontAsync(View reference, CancellationToken cancellationToken = default)
	{
		if (reference.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.Control control)
		{
			string family = control.FontFamily.Source;
			_ = await this.ExecuteAsync(
				"setUiFont(" + JsonSerializer.Serialize(family == "XamlAutoFontFamily" ? "Segoe UI" : family)
				+ "," + JsonSerializer.Serialize(control.FontSize) + ")",
				cancellationToken).ConfigureAwait(true);
		}
	}

	public async Task LoadAsync(string text, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(this.disposed, this);
		if (this.core is null)
		{
			if (view.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 browser)
			{
				throw new InvalidOperationException("The song editor is not available yet.");
			}

			await browser.EnsureCoreWebView2Async().AsTask(cancellationToken).ConfigureAwait(true);
			ObjectDisposedException.ThrowIf(this.disposed, this);
			this.core = browser.CoreWebView2;
			this.core.SetVirtualHostNameToFolderMapping(
				"editor.chordbook.invalid", Path.Combine(AppContext.BaseDirectory, "Editor"), CoreWebView2HostResourceAccessKind.DenyCors);
			this.core.WebMessageReceived += this.HandleMessage;
			this.core.NavigationStarting += this.HandleNavigation;
			this.core.Navigate(Origin + "/editor.html");
		}

		await this.ready.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(true);
		_ = await this.ExecuteAsync("load(" + JsonSerializer.Serialize(text) + ")", cancellationToken).ConfigureAwait(true);
	}

	public async Task<string> GetTextAsync(CancellationToken cancellationToken = default)
		=> JsonSerializer.Deserialize<string>(await this.ExecuteAsync("getText()", cancellationToken).ConfigureAwait(true))
			?? throw new InvalidOperationException("The editor did not return its text. Your changes have not been saved.");

	public async Task ReplaceTextAsync(string text, CancellationToken cancellationToken = default)
		=> _ = await this.ExecuteAsync("replaceText(" + JsonSerializer.Serialize(text) + ")", cancellationToken).ConfigureAwait(true);

	public async Task SetReadOnlyAsync(bool readOnly, CancellationToken cancellationToken = default)
		=> _ = await this.ExecuteAsync("setReadOnly(" + (readOnly ? "true" : "false") + ")", cancellationToken).ConfigureAwait(true);

	public void Dispose()
	{
		this.disposed = true;
		this.ready.TrySetCanceled();
		if (this.core is not null)
		{
			this.core.WebMessageReceived -= this.HandleMessage;
			this.core.NavigationStarting -= this.HandleNavigation;
			this.core = null;
		}
	}

	#endregion

	#region Private Methods

	private async Task<string> ExecuteAsync(string expression, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(this.disposed, this);
		cancellationToken.ThrowIfCancellationRequested();
		CoreWebView2 browser = this.core ?? throw new InvalidOperationException("The editor is still loading.");
		string script = "(() => { try { return {ok:true,value:window.chordBookEditor." + expression
			+ "}; } catch(error) { return {ok:false,error:String(error)}; } })()";
		string result = await browser.ExecuteScriptAsync(script).AsTask(cancellationToken).ConfigureAwait(true);
		cancellationToken.ThrowIfCancellationRequested();
		ObjectDisposedException.ThrowIf(this.disposed, this);
		using JsonDocument response = JsonDocument.Parse(result);
		if (response.RootElement.ValueKind != JsonValueKind.Object
			|| !response.RootElement.TryGetProperty("ok", out JsonElement success) || !success.GetBoolean())
		{
			throw new InvalidOperationException("The song editor could not complete the operation. Your changes have not been saved.");
		}

		return response.RootElement.TryGetProperty("value", out JsonElement value) ? value.GetRawText() : "null";
	}

	private void HandleNavigation(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
	{
		// A browser reload would discard the unsaved buffer without the page's confirmation flow.
		args.Cancel = this.navigationStarted || args.Uri != Origin + "/editor.html";
		if (!args.Cancel)
		{
			this.navigationStarted = true;
		}
	}

	private void HandleMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
	{
		if (args.Source == Origin + "/editor.html")
		{
			string message = args.TryGetWebMessageAsString();
			if (message == "ready")
			{
				this.ready.TrySetResult();
			}
			else if (message == "preview")
			{
				this.PreviewChanged?.Invoke(this, EventArgs.Empty);
			}
			else if (message == "changed")
			{
				this.TextChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	#endregion
}
