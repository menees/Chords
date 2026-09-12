#region Using Directives

using System.Text;
using Menees.Chords.Book.Application;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Maui;

public sealed partial class SongEditorPage : ContentPage
{
	#region Private Data

	private readonly SongEditDocument original;
	private readonly BookSession session;
	private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private bool saving;

	#endregion

	#region Public API

	public SongEditorPage(BookSession session)
		: this(new SongEditDocument(Guid.Empty, 0, string.Empty, [], [], null, null, string.Empty), session)
	{
		this.Title = "New Song";
		this.heading.Text = this.Title;
		this.newSongActions.IsVisible = true;
		this.editingHint.Text = "Paste or type a song, then preview its metadata. Save creates a UTF-8 sheet without changing its syntax.";
	}

	public SongEditorPage(SongEditDocument original, BookSession session)
	{
		this.original = original;
		this.session = session;
		this.InitializeComponent();
		this.songTitle.Text = original.Title;
		this.artists.Text = string.Join("; ", original.Artists);
		this.tags.Text = string.Join("; ", original.Tags);
		this.text.Text = original.Text;
		this.text.IsVisible = original.Text is not null;
		this.editingHint.Text = original.Text is null
			? "Metadata editing only. PDF and OpenSong source files are preserved."
			: "Song text — saves use the original encoding. Catalog fields do not rewrite source directives.";
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

	private async void HandlePreviewMetadata(object? sender, EventArgs e)
		=> await this.PreviewMetadataAsync().ConfigureAwait(true);

	private async Task PreviewMetadataAsync()
	{
		string source = this.text.Text ?? string.Empty;
		if (this.saving || string.IsNullOrWhiteSpace(source))
		{
			return;
		}

		this.saving = true;
		try
		{
			SongFileAnalysis analysis = await Task.Run(() => SongFileAnalyzer.Analyze(Encoding.UTF8.GetBytes(source), "New Song.txt")).ConfigureAwait(true);
			if (this.text.Text == source)
			{
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

					string format = analysis.SourceFormat switch
					{
						SourceFormat.ChordPro => "ChordPro",
						SourceFormat.ChordOverText => "Chords over text",
						SourceFormat.Mixed => "Mixed song text",
						_ => "Text",
					};
					this.editingHint.Text = $"Detected: {format}. Catalog fields can differ from the source; saving preserves your text.";
					this.status.Text = string.IsNullOrWhiteSpace(this.songTitle.Text) ? "Enter a catalog title before saving." : string.Empty;
				}
			}
		}
#pragma warning disable CA1031 // Invalid pasted source is reported without losing the editor buffer.
		catch (Exception exception)
		{
			this.status.Text = exception.Message;
		}
#pragma warning restore CA1031
		finally
		{
			this.saving = false;
		}
	}

	private async void HandleSave(object? sender, EventArgs e)
	{
		if (!this.saving)
		{
			this.saving = true;
			try
			{
				if (this.original.SongId == Guid.Empty)
				{
					this.SavedSongId = await this.session.CreateSongAsync(
						this.songTitle.Text, Split(this.artists.Text), Split(this.tags.Text), this.text.Text ?? string.Empty).ConfigureAwait(true);
				}
				else
				{
					await this.session.SaveSongEditAsync(
						this.original,
						this.songTitle.Text,
						Split(this.artists.Text),
						Split(this.tags.Text),
						this.original.Text is null ? null : this.text.Text ?? string.Empty).ConfigureAwait(true);
					this.SavedSongId = this.original.SongId;
				}

				await this.Navigation.PopModalAsync().ConfigureAwait(true);
				this.completion.TrySetResult(true);
			}
#pragma warning disable CA1031 // The editor retains the unsaved buffer and reports failures at the UI boundary.
			catch (Exception exception)
			{
				this.status.Text = exception.Message;
			}
#pragma warning restore CA1031
			finally
			{
				this.saving = false;
			}
		}
	}

	private async void HandleCancel(object? sender, EventArgs e)
	{
		bool changed = this.songTitle.Text != this.original.Title || !Split(this.artists.Text).SequenceEqual(this.original.Artists)
			|| !Split(this.tags.Text).SequenceEqual(this.original.Tags) || (this.original.Text is not null && this.text.Text != this.original.Text);
		if (!this.saving && (!changed
			|| await this.DisplayAlertAsync("Discard changes?", "Your unsaved song edits will be lost.", "Discard", "Keep Editing").ConfigureAwait(true)))
		{
			await this.Navigation.PopModalAsync().ConfigureAwait(true);
			this.completion.TrySetResult(false);
		}
	}

	#endregion
}
