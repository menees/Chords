#region Using Directives

using Menees.Chords.Book.Maui.Services;

#endregion

namespace Menees.Chords.Book.Maui;

public sealed partial class SongFilesPage : ContentPage
{
	#region Private Data

	private readonly Guid songId;
	private readonly BookSession session;
	private readonly IWindowsPicker picker;
	private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private List<SongFileRow> rows = [];
	private bool busy;
	private bool changed;

	#endregion

	#region Public API

	public SongFilesPage(Guid songId, BookSession session, IWindowsPicker picker)
	{
		this.songId = songId;
		this.session = session;
		this.picker = picker;
		this.InitializeComponent();
		this.heading.Text = session.Database!.Songs.Single(song => song.Id == songId).Title + " — Sheets";
		this.Refresh();
	}

	public Task<bool> Completion => this.completion.Task;

	#endregion

	#region Protected Methods

	protected override bool OnBackButtonPressed()
	{
		this.HandleClose(this, EventArgs.Empty);
		return true;
	}

	#endregion

	#region Private Methods

	private void Refresh(Guid? selectedId = null)
	{
		this.rows = [.. this.session.GetSongFiles(this.songId).Select(file => new SongFileRow(file))];
		this.files.ItemsSource = this.rows;
		this.files.SelectedItem = this.rows.FirstOrDefault(file => file.Id == selectedId);
		this.UpdateActions();
	}

	private void HandleSelectionChanged(object? sender, SelectionChangedEventArgs e) => this.UpdateActions();

	private void UpdateActions()
	{
		SongFileRow? selected = this.files.SelectedItem as SongFileRow;
		int index = selected is null ? -1 : this.rows.IndexOf(selected);
		this.add.IsEnabled = !this.session.Database!.Songs.Single(song => song.Id == this.songId).IsArchived;
		this.prefer.IsEnabled = selected is { IsArchived: false } && index > 0;
		this.edit.IsEnabled = selected is { CanEdit: true };
		this.up.IsEnabled = index > 0;
		this.down.IsEnabled = index >= 0 && index + 1 < this.rows.Count;
		this.rename.IsEnabled = selected is not null;
		this.archive.IsEnabled = selected is not null;
		this.archive.Text = selected is { IsArchived: true } ? "Restore" : "Archive";
		this.delete.IsEnabled = selected is { IsArchived: true };
	}

	private async void HandleAdd(object? sender, EventArgs e)
		=> await this.RunAsync(async () =>
		{
			IReadOnlyList<string> paths = await this.picker.PickFilesAsync(CancellationToken.None).ConfigureAwait(true);
			int count = paths.Count == 0 ? 0 : await this.session.ImportSongFilesAsync(this.songId, paths).ConfigureAwait(true);
			this.status.Text = $"Added {count:N0} sheet(s). Identical content already in this song is skipped, including archived sheets.";
			return count > 0;
		}).ConfigureAwait(true);

	private async void HandlePrefer(object? sender, EventArgs e) => await this.MoveAsync(1, relative: false).ConfigureAwait(true);

	private async void HandleEdit(object? sender, EventArgs e)
		=> await this.RunAsync(async () =>
		{
			SongFileRow selected = (SongFileRow)this.files.SelectedItem;
			var document = await this.session.GetSongFileEditAsync(this.songId, selected.Id).ConfigureAwait(true);
			SongEditorPage editor = new(document, this.session);
			await this.Navigation.PushModalAsync(editor).ConfigureAwait(true);
			return await editor.Completion.ConfigureAwait(true);
		}).ConfigureAwait(true);

	private async void HandleUp(object? sender, EventArgs e) => await this.MoveAsync(-1, relative: true).ConfigureAwait(true);

	private async void HandleDown(object? sender, EventArgs e) => await this.MoveAsync(1, relative: true).ConfigureAwait(true);

	private Task MoveAsync(int position, bool relative)
		=> this.RunAsync(async () =>
		{
			SongFileRow selected = (SongFileRow)this.files.SelectedItem;
			int target = relative ? this.rows.IndexOf(selected) + 1 + position : position;
			await this.session.SetSongFilePositionAsync(this.songId, selected.Id, target).ConfigureAwait(true);
			this.status.Text = "Sheet order saved.";
			return true;
		});

	private async void HandleRename(object? sender, EventArgs e)
		=> await this.RunAsync(async () =>
		{
			SongFileRow selected = (SongFileRow)this.files.SelectedItem;
			string? name = await this.DisplayPromptAsync(
				"Rename Sheet",
				"Enter a sheet name. The file type is retained.",
				initialValue: System.IO.Path.GetFileNameWithoutExtension(selected.Name)).ConfigureAwait(true);
			bool rename = !string.IsNullOrWhiteSpace(name);
			if (rename)
			{
				await this.session.RenameSongFileAsync(this.songId, selected.Id, name!).ConfigureAwait(true);
				this.status.Text = "Sheet renamed.";
			}

			return rename;
		}).ConfigureAwait(true);

	private async void HandleArchive(object? sender, EventArgs e)
		=> await this.RunAsync(async () =>
		{
			SongFileRow selected = (SongFileRow)this.files.SelectedItem;
			await this.session.SetSongFileArchivedAsync(this.songId, selected.Id, !selected.IsArchived).ConfigureAwait(true);
			this.status.Text = selected.IsArchived ? "Sheet restored." : "Sheet archived. Its content is retained.";
			return true;
		}).ConfigureAwait(true);

	private async void HandleDelete(object? sender, EventArgs e)
		=> await this.RunAsync(async () =>
		{
			SongFileRow selected = (SongFileRow)this.files.SelectedItem;
			bool confirmed = await this.DisplayAlertAsync(
				"Delete archived sheet?",
				$"Permanently delete {selected.Name}? Setlist and instrument preferences for this sheet will use their default instead. This cannot be undone.",
				"Delete Permanently",
				"Cancel").ConfigureAwait(true);
			if (confirmed)
			{
				await this.session.DeleteArchivedSongFileAsync(this.songId, selected.Id).ConfigureAwait(true);
				this.status.Text = "Archived sheet permanently deleted.";
			}

			return confirmed;
		}).ConfigureAwait(true);

	private async Task RunAsync(Func<Task<bool>> operation)
	{
		if (!this.busy)
		{
			this.busy = true;
			this.actions.IsEnabled = false;
			this.files.IsEnabled = false;
			Guid? selectedId = (this.files.SelectedItem as SongFileRow)?.Id;
			try
			{
				this.changed |= await operation().ConfigureAwait(true);
				this.Refresh(selectedId);
			}
#pragma warning disable CA1031 // Report file operations at the UI boundary and retain the open management view.
			catch (Exception exception)
			{
				this.status.Text = exception.Message;
				this.Refresh(selectedId);
			}
#pragma warning restore CA1031
			finally
			{
				this.files.IsEnabled = true;
				this.actions.IsEnabled = true;
				this.busy = false;
			}
		}
	}

	private async void HandleClose(object? sender, EventArgs e)
	{
		if (!this.busy)
		{
			await this.Navigation.PopModalAsync().ConfigureAwait(true);
			this.completion.TrySetResult(this.changed);
		}
	}

	#endregion
}
