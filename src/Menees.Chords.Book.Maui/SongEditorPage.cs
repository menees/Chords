using Menees.Chords.Book.Application;

namespace Menees.Chords.Book.Maui;

public sealed partial class SongEditorPage : ContentPage
{
	private readonly SongEditDocument original;
	private readonly BookSession session;
	private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private bool saving;

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

	protected override bool OnBackButtonPressed()
	{
		this.HandleCancel(this, EventArgs.Empty);
		return true;
	}

	private static string[] Split(string? text) => (text ?? string.Empty).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

	private async void HandleSave(object? sender, EventArgs e)
	{
		if (!this.saving)
		{
			this.saving = true;
			try
			{
				await this.session.SaveSongEditAsync(
					this.original,
					this.songTitle.Text,
					Split(this.artists.Text),
					Split(this.tags.Text),
					this.original.Text is null ? null : this.text.Text ?? string.Empty).ConfigureAwait(true);
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
}
