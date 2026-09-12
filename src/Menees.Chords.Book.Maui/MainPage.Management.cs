#region Using Directives

using System.Globalization;
using Menees.Chords.Book.Application;

#endregion

namespace Menees.Chords.Book.Maui;

public partial class MainPage
{
	#region Private Methods

	private void HandlePerformanceLockClicked(object? sender, EventArgs e)
	{
		this.SetPerformanceLocked(!this.performanceLocked);
		this.FocusSongViewer();
	}

	private void SetPerformanceLocked(bool locked)
	{
		this.performanceLocked = locked;
		this.PerformanceLockButton.Text = locked ? "Unlock" : "Lock";
		this.PerformanceSongButton.IsEnabled = !locked;
		this.PerformanceAddButton.IsEnabled = !locked;
		this.PerformanceMetronomeButton.IsEnabled = !locked;
	}

	private async Task ConfirmPerformanceExitAsync()
	{
		if (!this.confirmingPerformanceExit)
		{
			this.confirmingPerformanceExit = true;
			try
			{
				if (await this.DisplayAlertAsync("Leave performance?", "Performance is locked.", "Leave", "Stay").ConfigureAwait(true))
				{
					this.ExitPerformanceMode();
				}
			}
			finally
			{
				this.confirmingPerformanceExit = false;
			}
		}
	}

	private void UpdateScreenPolicy()
	{
		if (this.windowActive && this.PerformanceSurface.IsVisible)
		{
			this.previousKeepScreenOn ??= DeviceDisplay.Current.KeepScreenOn;
			DeviceDisplay.Current.KeepScreenOn = true;
		}
		else
		{
			this.RestoreScreenPolicy();
		}
	}

	private void RestoreScreenPolicy()
	{
		if (this.previousKeepScreenOn is bool previous)
		{
			DeviceDisplay.Current.KeepScreenOn = previous;
			this.previousKeepScreenOn = null;
		}
	}

	private async void HandleEntrySettingsClicked(object? sender, EventArgs e)
	{
		if (sender is Button { CommandParameter: SetlistEntryRow row } && this.currentSetlist is SetlistRow list)
		{
			await this.RunBookMutationAsync(async () =>
			{
				SetlistEntrySettings original = this.session.GetSetlistEntrySettings(list.Id, row.EntryId);
				SongFileCatalogItem[] files = [.. this.session.GetSongFiles(row.Song.Id).Where(file => !file.IsArchived && !file.IsRecoveryVersion)];
				string[] choices = ["Default sheet", .. files.Select((file, index) => $"{index + 1}. {file.Name}")];
				string? selected = await this.DisplayActionSheetAsync("Sheet for this setlist entry", "Cancel", null, choices).ConfigureAwait(true);
				int position = Array.IndexOf(choices, selected);
				if (position >= 0)
				{
					string? text = await this.DisplayPromptAsync(
						"Transpose This Setlist Entry",
						"Semitones (-24 to +24); blank uses the default. Text sheets only.",
						initialValue: original.TransposeSemitones?.ToString(CultureInfo.CurrentCulture) ?? string.Empty).ConfigureAwait(true);
					if (text is not null)
					{
						int? transpose = string.IsNullOrWhiteSpace(text) ? null : int.Parse(text, CultureInfo.CurrentCulture);
						await this.session.SaveSetlistEntrySettingsAsync(original, position == 0 ? null : files[position - 1].Id, transpose)
							.ConfigureAwait(true);
						this.RefreshSetlists(list.Id);
					}
				}
			}).ConfigureAwait(true);
		}
	}

	private async void HandleBookActionsClicked(object? sender, EventArgs e)
		=> await this.RunBookMutationAsync(async () =>
		{
			string[] actions = ["Rename Book", "Review Folder Changes", "Display Defaults", "Metronome Defaults"];
			string? action = await this.DisplayActionSheetAsync("Book", "Cancel", null, actions)
				.ConfigureAwait(true);
			if (action == "Metronome Defaults")
			{
				MetronomePage page = new(null, this.session, this.metronome);
				await this.Navigation.PushModalAsync(page).ConfigureAwait(true);
				await page.Completion.ConfigureAwait(true);
			}
			else if (action == "Display Defaults")
			{
				DisplaySettingsPage page = new(this.session, null);
				await this.Navigation.PushModalAsync(page).ConfigureAwait(true);
				await page.Completion.ConfigureAwait(true);
			}
			else if (action == "Rename Book")
			{
				await this.RenameCurrentBookAsync().ConfigureAwait(true);
			}
			else if (action == "Review Folder Changes")
			{
				this.Status.Text = "Checking the book folder…";
				var preview = await this.session.PreviewReconcileAsync().ConfigureAwait(true);
				BookReconciliationPage page = new(this.session, preview);
				await this.Navigation.PushModalAsync(page).ConfigureAwait(true);
				if (await page.Completion.ConfigureAwait(true))
				{
					this.RefreshCatalogAfterEdit("Reviewed folder changes applied.");
				}
			}
		}).ConfigureAwait(true);

	private async void HandleNewSongClicked(object? sender, EventArgs e)
		=> await this.RunBookMutationAsync(async () =>
		{
			SongEditorPage editor = new(this.session);
			await this.Navigation.PushModalAsync(editor).ConfigureAwait(true);
			if (await editor.Completion.ConfigureAwait(true))
			{
				this.RefreshCatalogAfterEdit("Song created. Clear the search if it is hidden by the current filter.");
			}
		}).ConfigureAwait(true);

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
				this.RefreshCatalogAfterEdit("Metronome settings updated.");
			}).ConfigureAwait(true);
		}
	}

	private void HandleManagementTabChanged(object? sender, EventArgs e)
	{
		if (this.selectingSongs)
		{
			this.EndSongSelection();
		}

		bool showSetlists = this.ManagementTabs.SelectedIndex == 1;
		this.ShowManagementTab(showSetlists, preserveSongTab: true);
		if (!showSetlists && this.session.Database is not null)
		{
			if (this.CurrentCustomTab is CustomTabCatalogItem tab && this.SongSearch.Text != tab.Search)
			{
				this.SongSearch.Text = tab.Search;
			}
			else
			{
				this.ApplyFilter(this.SongSearch.Text);
			}
		}
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
			await this.ManageSongAsync(selected[0].Id).ConfigureAwait(true);
		}
	}

	private async void HandleEditCurrentSongClicked(object? sender, EventArgs e)
	{
		if (this.currentSongIndex >= 0 && this.currentSongIndex < this.performanceSongs.Count)
		{
			await this.ManageSongAsync(this.performanceSongs[this.currentSongIndex].Id).ConfigureAwait(true);
		}
	}

	private async Task ManageSongAsync(Guid songId)
		=> await this.RunBookMutationAsync(async () =>
		{
			string? action = await this.DisplayActionSheetAsync("Song", "Cancel", null, "Edit Song", "Manage Sheets", "Display Settings").ConfigureAwait(true);
			bool saved = false;
			if (action == "Display Settings")
			{
				DisplaySettingsPage page = new(this.session, songId);
				await this.Navigation.PushModalAsync(page).ConfigureAwait(true);
				saved = await page.Completion.ConfigureAwait(true);
			}
			else if (action == "Edit Song")
			{
				this.metronome.Stop();
				SongEditDocument document = await this.session.GetSongEditAsync(songId).ConfigureAwait(true);
				SongEditorPage editor = new(document, this.session);
				await this.Navigation.PushModalAsync(editor).ConfigureAwait(true);
				saved = await editor.Completion.ConfigureAwait(true);
			}
			else if (action == "Manage Sheets")
			{
				SongFilesPage page = new(songId, this.session, this.picker);
				await this.Navigation.PushModalAsync(page).ConfigureAwait(true);
				saved = await page.Completion.ConfigureAwait(true);
			}

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
