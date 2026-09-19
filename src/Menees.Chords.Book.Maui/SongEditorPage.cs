#region Using Directives

using System.Text;
using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui.Platforms.Windows;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Maui;

public sealed partial class SongEditorPage : ContentPage
{
	#region Private Data

	private readonly SongEditDocument original;
	private readonly BookSession session;
	private readonly WindowsSongTextEditor editor;
	private readonly IDispatcherTimer previewTimer;
	private readonly CancellationTokenSource lifetime = new();
	private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private bool saving;
	private bool loading;
	private bool ready;
	private bool closed;
	private bool previewRunning;
	private bool previewPending;
	private int editVersion;

	#endregion

	#region Public API

	public SongEditorPage(BookSession session)
		: this(new SongEditDocument(Guid.Empty, 0, string.Empty, [], [], null, null, string.Empty), session)
	{
		this.Title = "New Song";
		this.heading.Text = this.Title;
		this.newSongActions.IsVisible = true;
		this.editingHint.Text = "Paste or type a song. Save creates a UTF-8 sheet; catalog fields do not rewrite your source.";
	}

	public SongEditorPage(SongEditDocument original, BookSession session)
	{
		this.original = original;
		this.session = session;
		this.InitializeComponent();
		this.songTitle.Text = original.Title;
		this.artists.Text = string.Join("; ", original.Artists);
		this.tags.Text = string.Join("; ", original.Tags);
		this.scalarMetadata.Load(original);
		this.editorArea.IsVisible = original.Text is not null;
		this.showPreview.IsEnabled = original.Text is not null;
		this.revertButton.IsVisible = original.Text is not null;
		this.text.IsEnabled = false;
		this.editingHint.Text = original.Text is null
			? "Metadata editing only. PDF and OpenSong source files are preserved."
			: string.Empty;
		this.editingHint.IsVisible = original.Text is null;
		this.editor = new WindowsSongTextEditor(this.text);
		this.editor.PreviewChanged += (_, _) => this.QueuePreview();
		this.previewTimer = this.Dispatcher.CreateTimer();
		this.previewTimer.Interval = TimeSpan.FromMilliseconds(350);
		this.previewTimer.IsRepeating = false;
		this.previewTimer.Tick += async (_, _) => await this.RefreshPreviewAsync().ConfigureAwait(true);
		this.text.Loaded += this.HandleEditorLoaded;
	}

	public Task<bool> Completion => this.completion.Task;

	public Guid? SavedSongId { get; private set; }

	#endregion

	#region Protected Methods

	protected override bool OnBackButtonPressed()
	{
		this.HandleCancel(this, EventArgs.Empty);
		return true;
	}

	#endregion

	#region Private Methods

	private static string[] Split(string? text) => (text ?? string.Empty).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

	private async void HandleEditorLoaded(object? sender, EventArgs e)
	{
		if (!this.loading && !this.ready && !this.closed && this.original.Text is not null)
		{
			this.loading = true;
			try
			{
				await this.editor.LoadAsync(this.original.Text, this.lifetime.Token).ConfigureAwait(true);
				if (!this.closed)
				{
					await this.editor.MatchUiFontAsync(this.songTitle, this.lifetime.Token).ConfigureAwait(true);

					this.ready = true;
					this.editorToolbar.IsEnabled = true;
					this.text.IsEnabled = true;
					new WindowsDocumentViewer(this.text).ApplyTheme();
					this.QueuePreview();
				}
			}
#pragma warning disable CA1031 // Report initialization failures without losing the original source or preventing Cancel.
			catch (Exception exception)
			{
				if (!this.closed)
				{
					this.status.Text = "Could not load the song editor: " + exception.Message;
				}
			}
#pragma warning restore CA1031
			finally
			{
				this.loading = false;
			}
		}
	}

	private async void HandlePreviewChanged(object? sender, CheckedChangedEventArgs e)
	{
		this.preview.IsVisible = e.Value;
		Grid.SetColumnSpan(this.text, e.Value ? 1 : 2);
		this.QueuePreview();
		try
		{
			await this.editor.SetPreviewEnabledAsync(e.Value, this.lifetime.Token).ConfigureAwait(true);
		}
#pragma warning disable CA1031 // Report browser failures at the final UI boundary without losing edits.
		catch (Exception exception)
		{
			if (!this.closed)
			{
				this.status.Text = "Preview: " + exception.Message;
			}
		}
#pragma warning restore CA1031
	}

	private void QueuePreview()
	{
		this.editVersion++;
		this.previewTimer.Stop();
		if (this.ready && !this.closed && this.showPreview.IsChecked)
		{
			this.previewPending = true;
			this.previewTimer.Start();
		}
	}

	private async Task RefreshPreviewAsync()
	{
		if (!this.previewRunning && this.ready && !this.closed && this.showPreview.IsChecked)
		{
			this.previewRunning = true;
			try
			{
				if (this.previewPending && !this.closed && this.showPreview.IsChecked)
				{
					this.previewPending = false;
					int version = this.editVersion;
					string source = await this.editor.GetPreviewTextAsync(this.lifetime.Token).ConfigureAwait(true);
					DisplayProfile profile = this.session.GetDisplaySettings(this.original.SongId == Guid.Empty ? null : this.original.SongId);
					string html = await Task.Run(() => SongDisplaySettings.Render(Document.Parse(source), profile, responsivePages: false), this.lifetime.Token)
						.ConfigureAwait(true);
					if (!this.closed && version == this.editVersion && this.showPreview.IsChecked)
					{
						this.preview.Source = new HtmlWebViewSource { Html = html };
					}
				}
			}
#pragma warning disable CA1031 // Preview errors never replace or save the editor buffer.
			catch (Exception exception)
			{
				if (!this.closed)
				{
					this.status.Text = "Preview: " + exception.Message;
				}
			}
#pragma warning restore CA1031
			finally
			{
				this.previewRunning = false;
				if (this.previewPending && !this.closed && this.showPreview.IsChecked)
				{
					this.previewTimer.Start();
				}
			}
		}
	}

	private async void HandleFind(object? sender, EventArgs e)
		=> await this.RunOperationAsync(() => this.editor.FindAsync(false, this.lifetime.Token), disablePage: false).ConfigureAwait(true);

	private async void HandleRedo(object? sender, EventArgs e)
		=> await this.RunOperationAsync(() => this.editor.RedoAsync(this.lifetime.Token), disablePage: false).ConfigureAwait(true);

	private async void HandleUndo(object? sender, EventArgs e)
		=> await this.RunOperationAsync(() => this.editor.UndoAsync(this.lifetime.Token), disablePage: false).ConfigureAwait(true);

	private async void HandlePreviewMetadata(object? sender, EventArgs e)
		=> await this.RunOperationAsync(async () =>
		{
			string source = await this.editor.GetTextAsync(this.lifetime.Token).ConfigureAwait(true);
			SongFileAnalysis analysis = await Task.Run(() => SongFileAnalyzer.Analyze(Encoding.UTF8.GetBytes(source), "New Song.txt"))
				.ConfigureAwait(true);
			if (analysis.MediaKind != MediaKind.Text || analysis.SourceFormat == SourceFormat.OpenSongXml)
			{
				this.status.Text = "Import PDF and OpenSong files instead. New Song supports ChordPro, chord-over-text, and mixed text.";
			}
			else
			{
				if (string.IsNullOrWhiteSpace(this.songTitle.Text) && (analysis.Metadata.ContainsKey("title") || analysis.Metadata.ContainsKey("t")))
				{
					this.songTitle.Text = analysis.Title;
				}

				if (string.IsNullOrWhiteSpace(this.artists.Text))
				{
					this.artists.Text = string.Join("; ", analysis.Artists);
				}

				this.status.Text = string.IsNullOrWhiteSpace(this.songTitle.Text) ? "Enter a catalog title before saving." : "Metadata previewed.";
			}
		}).ConfigureAwait(true);

	private async void HandleRevert(object? sender, EventArgs e)
		=> await this.RunOperationAsync(async () =>
		{
			if (await this.DisplayAlertAsync("Revert song text?", "Replace the buffer with the saved text? You can undo this change.", "Revert", "Cancel")
				.ConfigureAwait(true))
			{
				await this.editor.ReplaceTextAsync(this.original.Text!, this.lifetime.Token).ConfigureAwait(true);
			}
		}).ConfigureAwait(true);

	private SongEditMetadata GetMetadata() => new(this.songTitle.Text, Split(this.artists.Text), Split(this.tags.Text))
	{
		Scalars = this.scalarMetadata.GetValues(),
	};

	private async void HandleImportMetadata(object? sender, EventArgs e)
		=> await this.RunOperationAsync(async () =>
		{
			string source = await this.editor.GetTextAsync(this.lifetime.Token).ConfigureAwait(true);
			SongEditMetadata metadata = this.GetMetadata();
			MetadataDirectiveImport import = await Task.Run(() => new MetadataDirectiveImport(source, metadata), this.lifetime.Token).ConfigureAwait(true);
			MetadataImportPage page = new(import);
			await this.Navigation.PushModalAsync(page).ConfigureAwait(true);
			string? updated = await page.Completion.ConfigureAwait(true);
			if (updated is not null)
			{
				if (source != await this.editor.GetTextAsync(this.lifetime.Token).ConfigureAwait(true))
				{
					throw new InvalidOperationException("The editor text changed during review. Preview the metadata import again.");
				}

				await this.editor.ReplaceTextAsync(updated, this.lifetime.Token).ConfigureAwait(true);
				this.status.Text = "Metadata imported into the buffer. Undo is available; Save commits the text.";
			}
		}).ConfigureAwait(true);

	private async void HandleSave(object? sender, EventArgs e)
		=> await this.RunOperationAsync(async () =>
		{
			string? source = this.original.Text is null ? null : await this.editor.GetTextAsync(this.lifetime.Token).ConfigureAwait(true);
			if (this.original.SongId == Guid.Empty)
			{
				this.SavedSongId = await this.session.CreateSongAsync(
					this.GetMetadata(), source ?? string.Empty).ConfigureAwait(true);
			}
			else
			{
				IReadOnlyList<string> warnings = await this.session.SaveSongEditAsync(this.original, this.GetMetadata(), source)
					.ConfigureAwait(true);
				this.SavedSongId = this.original.SongId;
				if (warnings.Count > 0)
				{
					await this.DisplayAlertAsync("Saved with metadata warnings", string.Join(Environment.NewLine, warnings), "OK").ConfigureAwait(true);
				}
			}

			await this.CloseAsync(true).ConfigureAwait(true);
		}).ConfigureAwait(true);

	private async void HandleCancel(object? sender, EventArgs e)
		=> await this.RunOperationAsync(
		async () =>
		{
			string? source = this.ready ? await this.editor.GetTextAsync(this.lifetime.Token).ConfigureAwait(true) : this.original.Text;
			bool changed = this.songTitle.Text != this.original.Title || !Split(this.artists.Text).SequenceEqual(this.original.Artists)
				|| !Split(this.tags.Text).SequenceEqual(this.original.Tags) || source != this.original.Text || this.scalarMetadata.HasChanges;
			if (!changed || await this.DisplayAlertAsync("Discard changes?", "Your unsaved song edits will be lost.", "Discard", "Keep Editing")
				.ConfigureAwait(true))
			{
				await this.CloseAsync(false).ConfigureAwait(true);
			}
		},
		requireEditor: false).ConfigureAwait(true);

	private async Task CloseAsync(bool saved)
	{
		this.closed = true;
		this.previewTimer.Stop();
		await this.lifetime.CancelAsync().ConfigureAwait(true);
		this.editor.Dispose();
		await this.Navigation.PopModalAsync().ConfigureAwait(true);
		this.completion.TrySetResult(saved);
		this.lifetime.Dispose();
	}

	private async Task RunOperationAsync(Func<Task> operation, bool requireEditor = true, bool disablePage = true)
	{
		if (!this.saving && !this.closed)
		{
			if (requireEditor && this.original.Text is not null && !this.ready)
			{
				this.status.Text = "Wait for the song editor to finish loading.";
			}
			else
			{
				this.saving = true;
				this.IsEnabled = !disablePage;
				try
				{
					await operation().ConfigureAwait(true);
				}
#pragma warning disable CA1031 // Keep unsaved buffers and report failures at the final UI boundary.
				catch (Exception exception)
				{
					this.status.Text = exception.Message;
				}
#pragma warning restore CA1031
				finally
				{
					this.saving = false;
					this.IsEnabled = true;
				}
			}
		}
	}

	#endregion
}
