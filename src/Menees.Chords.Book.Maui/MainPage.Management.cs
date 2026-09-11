#region Using Directives

using System.Globalization;
using Menees.Chords.Book.Application;

#endregion

namespace Menees.Chords.Book.Maui;

public partial class MainPage
{
	#region Private Methods

	private async void HandleMetronomeClicked(object? sender, EventArgs e)
	{
		if (this.currentSongIndex >= 0 && this.currentSongIndex < this.performanceSongs.Count)
		{
			await this.RunBookMutationAsync(async () =>
			{
				Guid id = this.performanceSongs[this.currentSongIndex].Id;
				MetronomePage page = new(id, this.session, this.metronome);
				await this.Navigation.PushModalAsync(page).ConfigureAwait(true);
				await page.Completion.ConfigureAwait(true);
			}).ConfigureAwait(true);
		}
	}

	private void HandleManagementTabChanged(object? sender, EventArgs e)
	{
		if (this.ManagementTabs.SelectedIndex == 1 && this.selectingSongs)
		{
			this.EndSongSelection();
		}

		this.ShowManagementTab(this.ManagementTabs.SelectedIndex == 1);
	}

	private void UpdateSongSelectionActions()
	{
		SongRow[] selected = [.. this.visibleSongs.Where(song => song.IsSelected)];
		this.EditSelectedSongButton.IsEnabled = selected.Length == 1;
		this.ArchiveSelectedSongsButton.IsEnabled = selected.Any(song => !song.IsArchived);
		this.RestoreSelectedSongsButton.IsEnabled = selected.Any(song => song.IsArchived);
		this.DeleteSelectedSongsButton.IsEnabled = selected.Length > 0 && selected.All(song => song.IsArchived);
	}

	private async void HandleArchiveSelectedSongsClicked(object? sender, EventArgs e)
		=> await this.ChangeSelectedArchiveAsync(true).ConfigureAwait(true);

	private async void HandleRestoreSelectedSongsClicked(object? sender, EventArgs e)
		=> await this.ChangeSelectedArchiveAsync(false).ConfigureAwait(true);

	private async Task ChangeSelectedArchiveAsync(bool archived)
	{
		Guid[] ids = [.. this.visibleSongs.Where(song => song.IsSelected && song.IsArchived != archived).Select(song => song.Id)];
		if (ids.Length > 0)
		{
			await this.RunBookMutationAsync(async () =>
			{
				await this.session.SetSongsArchivedAsync(ids, archived).ConfigureAwait(true);
				this.RefreshCatalogAfterEdit($"{(archived ? "Archived" : "Restored")} {ids.Length:N0} song(s).");
			}).ConfigureAwait(true);
		}
	}

	private async void HandleDeleteSelectedSongsClicked(object? sender, EventArgs e)
	{
		SongRow[] selected = [.. this.visibleSongs.Where(song => song.IsSelected)];
		if (selected.Length > 0 && selected.All(song => song.IsArchived))
		{
			await this.RunBookMutationAsync(async () =>
			{
				HashSet<Guid> ids = [.. selected.Select(song => song.Id)];
				int files = this.session.Database!.SongFiles.Count(file => ids.Contains(file.SongId));
				int entries = this.session.Database.Setlists.Sum(setlist => setlist.Entries.Count(entry => ids.Contains(entry.SongId)));
				const int PreviewSongCount = 8;
				string names = string.Join("\n", selected.Take(PreviewSongCount).Select(song => song.Title));
				string message = $"Delete {selected.Length:N0} archived song(s), {files:N0} managed file(s), and {entries:N0} setlist occurrence(s)?"
					+ $" This cannot be undone.\n\n{names}";
				bool confirmed = await this.DisplayAlertAsync(
					"Permanently delete archived songs?",
					message,
					"Delete Permanently",
					"Cancel").ConfigureAwait(true);
				if (confirmed)
				{
					await this.session.DeleteArchivedSongsAsync([.. ids]).ConfigureAwait(true);
					this.RefreshCatalogAfterEdit($"Permanently deleted {selected.Length:N0} archived song(s).");
				}
			}).ConfigureAwait(true);
		}
	}

	private async void HandleDeleteSetlistClicked(object? sender, EventArgs e)
	{
		if (this.currentSetlist is { IsArchived: true } setlist)
		{
			await this.RunBookMutationAsync(async () =>
			{
				if (await this.DisplayAlertAsync(
					"Permanently delete archived setlist?",
					$"Delete {setlist.Name} and its {setlist.EntryCount:N0} entries? The songs and their files will remain. This cannot be undone.",
					"Delete Permanently",
					"Cancel").ConfigureAwait(true))
				{
					await this.session.DeleteArchivedSetlistAsync(setlist.Id).ConfigureAwait(true);
					this.RefreshSetlists();
					this.ShowSetlistOverview();
					this.Status.Text = $"Permanently deleted {setlist.Name}.";
				}
			}).ConfigureAwait(true);
		}
	}

	private async void HandleSetlistReorderCompleted(object? sender, EventArgs e)
	{
		if (this.editingSetlist && !this.bookMutationInProgress && this.currentSetlist is SetlistRow setlist)
		{
			Guid[] order = [.. this.selectedSetlistEntries.Select(entry => entry.EntryId)];
			await this.RunBookMutationAsync(async () =>
			{
				this.RenumberSetlistEntries();
				try
				{
					await this.session.SetSetlistOrderAsync(setlist.Id, order).ConfigureAwait(true);
					this.Status.Text = "Setlist order updated.";
				}
				catch
				{
					this.RefreshSelectedSetlist();
					throw;
				}
			}).ConfigureAwait(true);
		}
	}

	private void RenumberSetlistEntries()
	{
		for (int index = 0; index < this.selectedSetlistEntries.Count; index++)
		{
			this.selectedSetlistEntries[index].UpdatePosition(index + 1, this.selectedSetlistEntries.Count);
		}
	}

	private async void HandleSetlistPositionCompleted(object? sender, EventArgs e)
		=> await this.CommitEntryPositionAsync(sender).ConfigureAwait(true);

	private async void HandleSetlistPositionUnfocused(object? sender, FocusEventArgs e)
		=> await this.CommitEntryPositionAsync(sender).ConfigureAwait(true);

	private async Task CommitEntryPositionAsync(object? sender)
	{
		if (this.editingSetlist && !this.bookMutationInProgress && this.currentSetlist is SetlistRow setlist
			&& sender is Microsoft.Maui.Controls.Entry { BindingContext: SetlistEntryRow entry } input)
		{
			if (int.TryParse(input.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int position)
				&& position >= 1 && position <= this.selectedSetlistEntries.Count)
			{
				if (position != entry.Position)
				{
					await this.SetEntryPositionAsync(setlist.Id, entry.EntryId, position).ConfigureAwait(true);
				}
			}
			else
			{
				input.Text = entry.Position.ToString(CultureInfo.CurrentCulture);
				string message = $"Enter a whole number from 1 to {this.selectedSetlistEntries.Count}.";
				await this.DisplayAlertAsync("Invalid position", message, "OK").ConfigureAwait(true);
			}
		}
	}

	private async Task SetEntryPositionAsync(Guid setlistId, Guid entryId, int position)
		=> await this.RunBookMutationAsync(async () =>
		{
			SetlistEntryRow moved = this.selectedSetlistEntries.Single(entry => entry.EntryId == entryId);
			this.selectedSetlistEntries.Move(this.selectedSetlistEntries.IndexOf(moved), position - 1);
			this.RenumberSetlistEntries();
			try
			{
				await this.session.SetSetlistEntryPositionAsync(setlistId, entryId, position).ConfigureAwait(true);
				this.Status.Text = $"Moved song to position {position:N0}.";
			}
			catch
			{
				this.RefreshSelectedSetlist();
				throw;
			}
		}).ConfigureAwait(true);

	private async void HandleEditSelectedSongClicked(object? sender, EventArgs e)
	{
		SongRow[] selected = [.. this.visibleSongs.Where(song => song.IsSelected)];
		if (selected.Length == 1)
		{
			await this.EditSongAsync(selected[0].Id).ConfigureAwait(true);
		}
	}

	private async void HandleEditCurrentSongClicked(object? sender, EventArgs e)
	{
		if (this.currentSongIndex >= 0 && this.currentSongIndex < this.performanceSongs.Count)
		{
			await this.EditSongAsync(this.performanceSongs[this.currentSongIndex].Id).ConfigureAwait(true);
		}
	}

	private async Task EditSongAsync(Guid songId)
		=> await this.RunBookMutationAsync(async () =>
		{
			this.metronome.Stop();
			SongEditDocument document = await this.session.GetSongEditAsync(songId).ConfigureAwait(true);
			SongEditorPage editor = new(document, this.session);
			await this.Navigation.PushModalAsync(editor).ConfigureAwait(true);
			bool saved = await editor.Completion.ConfigureAwait(true);
			if (saved)
			{
				bool performing = this.PerformanceSurface.IsVisible;
				this.RefreshCatalogAfterEdit("Song saved.");
				if (performing)
				{
					Dictionary<Guid, SongRow> songs = this.allSongs.ToDictionary(song => song.Id);
					this.performanceSongs = [.. this.performanceSongs.Select(song => songs[song.Id])];
					await this.ShowSongAsync(songs[songId], this.performanceSongs, this.currentSongIndex, this.performanceContextName).ConfigureAwait(true);
				}
			}
		}).ConfigureAwait(true);

	private void RefreshCatalogAfterEdit(string status)
	{
		if (this.selectingSongs)
		{
			this.EndSongSelection();
		}

		this.allSongs = this.session.SearchSongs(string.Empty, includeArchived: true);
		this.ApplyFilter(this.SongSearch.Text);
		this.RefreshSetlists();
		this.Status.Text = status;
	}

	#endregion
}
