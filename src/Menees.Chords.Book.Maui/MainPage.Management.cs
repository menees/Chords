#region Using Directives

using System.Globalization;
using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui.Platforms.Windows;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Maui;

public partial class MainPage
{
	#region Private Properties

	private bool FileOpeningBlocked => !this.fileOpeningReady || this.bookMutationInProgress || this.editingSetlist || this.selectingSongs
		|| this.performanceLocked || this.Navigation.NavigationStack.Count > 1 || this.Navigation.ModalStack.Count > 0;

	#endregion

	#region Private Methods

	private void HandlePerformanceLockClicked(object? sender, EventArgs e)
	{
		this.SetPerformanceLocked(!this.performanceLocked);
		this.FocusSongViewer();
	}

	private void SetPerformanceLocked(bool locked)
	{
		this.performanceLocked = locked;
		this.PerformanceLockButton.SetIcon(locked ? "LockOpen" : "LockClosed", locked ? "Unlock" : "Lock");
		this.QuickNotation.IsEnabled = !locked && this.showingHtmlChart;
		this.QuickTranspose.IsEnabled = this.QuickNotation.IsEnabled;
		this.QuickInstrument.IsEnabled = !locked;
		this.PerformanceSongButton.IsEnabled = !locked;
		this.PerformanceAddButton.IsEnabled = !locked;
		this.PerformanceMetronomeButton.IsEnabled = !locked;
		this.PerformanceBeatToggle.IsEnabled = !locked;
		if (!locked)
		{
			_ = this.ProcessPendingFilesAsync();
		}
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
				SetlistEntrySettings original = this.setlistEdit!.GetEntrySettings(row.EntryId);
				InstrumentProfileInfo[] instruments = [.. this.session.Instruments.GetProfiles()];
				string[] instrumentChoices = ["Use current instrument", .. instruments.Select(item => item.Name)];
				string? selectedInstrument = await this.DisplayActionSheetAsync(
					"Instrument for this entry", "Cancel", null, instrumentChoices).ConfigureAwait(true);
				int instrumentIndex = Array.IndexOf(instrumentChoices, selectedInstrument);

				if (instrumentIndex >= 0)
				{
					Guid? instrumentId = instrumentIndex <= 0 ? null : instruments[instrumentIndex - 1].Id;
					SongFileCatalogItem[] files = [.. this.session.GetSongFiles(row.Song.Id).Where(file => !file.IsArchived && !file.IsRecoveryVersion)];
					string[] choices = ["Default sheet", .. files.Select((file, index) => $"{index + 1}. {file.Name}")];
					string? selected = await this.DisplayActionSheetAsync("Sheet for this setlist entry", "Cancel", null, choices).ConfigureAwait(true);
					int position = Array.IndexOf(choices, selected);
					if (position >= 0 && instrumentIndex >= 0)
					{
						string? text = await this.DisplayPromptAsync(
							"Transpose This Setlist Entry",
							"Semitones (-24 to +24); blank uses the default. Text sheets only.",
							initialValue: original.TransposeSemitones?.ToString(CultureInfo.CurrentCulture) ?? string.Empty).ConfigureAwait(true);
						if (text is not null)
						{
							int? transpose = string.IsNullOrWhiteSpace(text) ? null : int.Parse(text, CultureInfo.CurrentCulture);
							this.setlistEdit!.SetEntrySettings(
								this.session.Database!, row.EntryId, position == 0 ? null : files[position - 1].Id, transpose, instrumentId);
							this.RefreshSelectedSetlist();
						}
					}
				}
			}).ConfigureAwait(true);
		}
	}

	private async void HandleBookActionsClicked(object? sender, EventArgs e)
		=> await this.RunBookMutationAsync(async () =>
		{
			string[] actions = ["Resume Last Performance", "Rename Book", "Review Folder Changes", "Display Defaults",
				"Metronome Defaults", "Instruments", "Keyboard and Pedals", "Backup and Restore", "Validate Book",
				"OneDrive", "File Associations", "Appearance"];
			string? action = await this.DisplayActionSheetAsync("Options", "Cancel", null, actions)
				.ConfigureAwait(true);
			if (action == "Appearance")
			{
				string? theme = await this.DisplayActionSheetAsync("App appearance", "Cancel", null, "System", "Light", "Dark").ConfigureAwait(true);
				if (global::Microsoft.Maui.Controls.Application.Current is App app && theme is "System" or "Light" or "Dark")
				{
					app.SetTheme(theme switch { "Light" => AppTheme.Light, "Dark" => AppTheme.Dark, _ => AppTheme.Unspecified });
				}
			}
			else if (action == "File Associations")
			{
				await WindowsFileAssociations.ConfigureAsync().ConfigureAwait(true);
				this.Status.Text = "Choose Menees ChordBook for .cho files in Windows Default apps.";
			}
			else if (action == "OneDrive")
			{
				CloudSyncPage page = new(this.session, new WindowsOneDriveConnection());
				await ScreenNavigation.PushAsync(this, page).ConfigureAwait(true);
				await page.Completion.ConfigureAwait(true);
				this.RefreshSongs("OneDrive review closed.");
				this.RefreshSetlists();
			}
			else if (action == "Backup and Restore")
			{
				BookBackupPage page = new(this.session, this.picker);
				await ScreenNavigation.PushAsync(this, page).ConfigureAwait(true);
				if (await page.Completion.ConfigureAwait(true))
				{
					this.RefreshSongs("Restored book opened.");
				}
			}
			else if (action == "Validate Book")
			{
				BookIntegrityPage page = new(this.session);
				await ScreenNavigation.PushAsync(this, page).ConfigureAwait(true);
				await page.Completion.ConfigureAwait(true);
			}
			else if (action == "Resume Last Performance")
			{
				PerformanceResume? resume = await this.performanceStore.LoadAsync(this.session.Database!).ConfigureAwait(true);
				if (resume is null)
				{
					this.Status.Text = "No saved performance is available for the current songs and setlists.";
				}
				else
				{
					Dictionary<Guid, SongRow> rows = this.allSongs.ToDictionary(song => song.Id);
					SongRow[] songs = [.. resume.SongIds.Select(id => rows[id])];
					this.metronome.Stop();
					this.performanceSetlistId = resume.SetlistId;
					this.performanceEntryIds = resume.EntryIds;
					await this.ShowSongAsync(songs[resume.Index], songs, resume.Index, resume.Name).ConfigureAwait(true);
				}
			}
			else if (action == "Keyboard and Pedals")
			{
				PerformanceInputPage page = new(this.session);
				await ScreenNavigation.PushAsync(this, page).ConfigureAwait(true);
				await page.Completion.ConfigureAwait(true);
			}
			else if (action == "Instruments")
			{
				InstrumentSettingsPage page = new(this.session);
				await ScreenNavigation.PushAsync(this, page).ConfigureAwait(true);
				await page.Completion.ConfigureAwait(true);
			}
			else if (action == "Metronome Defaults")
			{
				MetronomePage page = new(null, this.session, this.metronome);
				await ScreenNavigation.PushAsync(this, page).ConfigureAwait(true);
				await page.Completion.ConfigureAwait(true);
			}
			else if (action == "Display Defaults")
			{
				DisplaySettingsPage page = new(this.session, null);
				await ScreenNavigation.PushAsync(this, page).ConfigureAwait(true);
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
				await ScreenNavigation.PushAsync(this, page).ConfigureAwait(true);
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
			await ScreenNavigation.PushAsync(this, editor).ConfigureAwait(true);
			if (await editor.Completion.ConfigureAwait(true))
			{
				this.RefreshCatalogAfterEdit("Song created. Clear the search if it is hidden by the current filter.");
			}
		}).ConfigureAwait(true);

	private void HandleMetronomeClicked(object? sender, EventArgs e)
	{
		if (this.metronomePanel is not null)
		{
			if (!this.metronomePanel.IsBusy)
			{
				this.CloseMetronomePanel();
			}
		}
		else if (this.currentSongIndex >= 0 && this.currentSongIndex < this.performanceSongs.Count)
		{
			Guid songId = this.performanceSongs[this.currentSongIndex].Id;
			this.metronomePanel = new(songId, this.session, this.metronome);
			this.metronomePanel.Closed += (_, _) => this.CloseMetronomePanel();
			this.metronomePanel.SettingsSaved += (panel, _) =>
			{
				if (ReferenceEquals(panel, this.metronomePanel) && this.pausedMetronomeSettings is not null)
				{
					this.pausedMetronomeSettings = this.session.GetMetronomeSettings(songId);
				}

				this.RefreshCatalogAfterEdit("Metronome settings updated.");
			};
			this.MetronomeHost.Content = this.metronomePanel;
			this.UpdatePerformanceMetronome();
		}
	}

	private void CloseMetronomePanel()
	{
		this.metronomePanel?.Detach();
		this.MetronomeHost.Content = null;
		this.metronomePanel = null;
		this.UpdatePerformanceMetronome();
		this.FocusSongViewer();
	}

	private async void HandlePerformanceBeatClicked(object? sender, EventArgs e)
	{
		if (!this.performanceLocked && this.metronomePanel is null && this.PerformanceBeatControl.IsVisible)
		{
			await this.RunUiOperationAsync(this.TogglePerformanceMetronomeAsync).ConfigureAwait(true);
		}
	}

	private async Task TogglePerformanceMetronomeAsync()
	{
		if (!this.togglingPerformanceMetronome && this.currentSongIndex >= 0)
		{
			this.togglingPerformanceMetronome = true;
			try
			{
				if (this.metronome.IsRunning)
				{
					this.pausedMetronomeSettings = this.metronome.CurrentSettings;
					this.metronome.Stop();
				}
				else
				{
					MetronomeSettings settings = this.pausedMetronomeSettings
						?? this.session.GetMetronomeSettings(this.performanceSongs[this.currentSongIndex].Id);
					await this.metronome.StartAsync(settings).ConfigureAwait(true);
				}
			}
			finally
			{
				this.togglingPerformanceMetronome = false;
				this.UpdatePerformanceMetronome();
			}
		}
	}

	private void UpdatePerformanceMetronome()
	{
		bool running = this.metronome.IsRunning;
		if (running)
		{
			this.pausedMetronomeSettings = null;
		}

		this.PerformanceBeatControl.IsVisible = this.metronomePanel is null && (running || this.pausedMetronomeSettings is not null);
		if (this.PerformanceBeatControl.IsVisible)
		{
			string action = running ? "Stop metronome" : "Start metronome";
			if (!Equals(ToolTipProperties.GetText(this.PerformanceBeatToggle), action))
			{
				ToolTipProperties.SetText(this.PerformanceBeatToggle, action);
				SemanticProperties.SetDescription(this.PerformanceBeatToggle, action);
			}

			int beat = running && this.metronome.VisualEnabled ? this.metronome.CurrentBeat : 0;
			this.PerformanceBeats.Update(
				this.pausedMetronomeSettings?.BeatsPerMeasure ?? this.metronome.BeatsPerMeasure,
				beat,
				this.metronome.AccentFirstBeat && beat == 1);
		}
	}

	private void UpdatePerformanceSongContext(Guid songId, Guid? entryId)
	{
		if (this.instrumentSongId != songId || this.instrumentEntryId != entryId)
		{
			this.pausedMetronomeSettings = null;
			if (this.togglingPerformanceMetronome && !this.metronome.IsRunning)
			{
				this.metronome.Stop();
			}

			this.instrumentOverride = null;
			this.instrumentSongId = songId;
			this.instrumentEntryId = entryId;
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
		this.SelectAllSongsButton.IsEnabled = selected.Length < this.visibleSongs.Count;
		this.UnselectAllSongsButton.IsEnabled = selected.Length > 0;
		this.EditSelectedSongButton.IsVisible = this.ArchiveSelectedSongsButton.IsVisible
			= this.RestoreSelectedSongsButton.IsVisible = this.DeleteSelectedSongsButton.IsVisible
			= this.TagSelectedSongsButton.IsVisible = !this.editingSetlist;
		this.EditSelectedSongButton.IsEnabled = selected.Length == 1;
		this.TagSelectedSongsButton.IsEnabled = selected.Length > 0;
		this.ArchiveSelectedSongsButton.IsEnabled = selected.Any(song => !song.IsArchived);
		this.RestoreSelectedSongsButton.IsEnabled = selected.Any(song => song.IsArchived);
		this.DeleteSelectedSongsButton.IsEnabled = selected.Length > 0 && selected.All(song => song.IsArchived);
	}

	private void HandleSelectAllSongsClicked(object? sender, EventArgs e)
	{
		if (this.selectingSongs && !this.bookMutationInProgress)
		{
			this.SongList.UpdateSelectedItems([.. this.visibleSongs]);
		}
	}

	private void HandleUnselectAllSongsClicked(object? sender, EventArgs e)
	{
		if (this.selectingSongs && !this.bookMutationInProgress)
		{
			this.SongList.UpdateSelectedItems([]);
		}
	}

	private async void HandleTagSelectedSongsClicked(object? sender, EventArgs e)
		=> await this.EditTagsAsync([.. this.visibleSongs.Where(song => song.IsSelected).Select(song => song.Id)]).ConfigureAwait(true);

	private void HandleSelectSetlistSongsClicked(object? sender, EventArgs e)
	{
		if (!this.editingSetlist && !this.bookMutationInProgress)
		{
			HashSet<Guid> ids = [.. this.selectedSetlistEntries.Select(entry => entry.Song.Id)];
			this.ShowManagementTab(showSetlists: false);
			this.SongSearch.Text = string.Empty;
			if (this.allSongs.Any(song => ids.Contains(song.Id) && song.IsArchived))
			{
				this.ShowArchived.IsChecked = true;
			}

			this.ApplyFilter(string.Empty);
			this.BeginSongSelection(null);
			this.SongList.SelectedItems = [.. this.visibleSongs.Where(song => ids.Contains(song.Id))];
		}
	}

	private async Task EditTagsAsync(Guid[] songIds)
	{
		if (songIds.Length > 0 && !this.editingSetlist)
		{
			await this.RunBookMutationAsync(async () =>
			{
				CatalogValueSelection draft = this.session.GetSongTagSelection(songIds);
				IReadOnlyList<string>? tags = await WindowsCatalogValueDialog.ShowAsync(
					this,
					$"Edit tags for {songIds.Length:N0} {(songIds.Length == 1 ? "song" : "songs")}",
					draft,
					description: "Apply replaces each song's tags with the checked set. Clear all checks to remove all tags.").ConfigureAwait(true);
				if (tags is not null)
				{
					int changed = await this.session.SetSongTagsAsync(songIds, tags).ConfigureAwait(true);
					this.RefreshCatalogAfterEdit(changed == 0 ? "All selected songs already have these tags." : $"Updated tags for {changed:N0} song(s).");
				}
			}).ConfigureAwait(true);
		}
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
		if (this.currentSetlist is SetlistRow setlist && this.setlistEdit?.Value.IsArchived == true)
		{
			await this.RunBookMutationAsync(async () =>
			{
				if (await this.DisplayAlertAsync(
					"Permanently delete archived setlist?",
					$"Delete {setlist.Name} and its {setlist.EntryCount:N0} entries? The songs and their files will remain. This cannot be undone.",
					"Delete Permanently",
					"Cancel").ConfigureAwait(true))
				{
					this.setlistEdit.RequestDeletion();
					this.Status.Text = "Choose Save to permanently delete the setlist, or Back to discard.";
					this.UpdateSetlistEditorControls();
				}
			}).ConfigureAwait(true);
		}
	}

	private void HandleSetlistReorderCompleted(object? sender, EventArgs e)
	{
		if (this.editingSetlist && !this.bookMutationInProgress && this.setlistEdit is SetlistEdit draft)
		{
			draft.SetOrder([.. this.selectedSetlistEntries.Select(entry => entry.EntryId)]);
			this.RenumberSetlistEntries();
		}
	}

	private async void HandleSortSetlistClicked(object? sender, EventArgs e)
		=> await this.RunBookMutationAsync(() =>
		{
			if (this.setlistEdit is SetlistEdit draft)
			{
				draft.SortByName(this.selectedSetlistEntries.DistinctBy(entry => entry.Song.Id)
					.ToDictionary(entry => entry.Song.Id, entry => entry.Song.Title));
				this.RefreshSelectedSetlist();
			}

			return Task.CompletedTask;
		}).ConfigureAwait(true);

	private void UpdateSetlistEditorControls()
	{
		bool editing = this.editingSetlist;
		bool deleted = this.setlistEdit?.DeleteRequested == true;
		this.EditSetlistButton.SetIcon(editing ? "Save" : "EditLineHorizontal3", editing ? "Save" : "Edit");
		this.AddSetlistSongsButton.IsVisible = editing;
		this.SelectSetlistSongsButton.IsVisible = !editing;
		this.SelectSetlistSongsButton.IsEnabled = this.selectedSetlistEntries.Count > 0;
		this.RenameSetlistButton.IsVisible = editing;
		this.SortSetlistButton.IsVisible = editing;
		this.ArchiveSetlistButton.IsVisible = editing;
		this.DeleteSetlistButton.IsVisible = editing && this.setlistEdit?.Value.IsArchived == true;
		this.AddSetlistSongsButton.IsEnabled = this.RenameSetlistButton.IsEnabled = this.SortSetlistButton.IsEnabled = !deleted;
		this.ArchiveSetlistButton.IsEnabled = !deleted;
		this.SetlistEntries.IsEnabled = !deleted && !this.bookMutationInProgress;
		this.SetlistEditorToolbar.IsEnabled = !this.bookMutationInProgress;
		this.ManagementBookToolbar.IsVisible = !editing || this.selectingSongs;
		this.ManagementTabs.IsVisible = this.ManagementTabs.IsEnabled = !editing;
		this.OpenBookButton.IsEnabled = this.NewBookButton.IsEnabled = this.RenameBookButton.IsEnabled
			= this.ImportButton.IsEnabled = !editing && !this.bookMutationInProgress;
		this.UpdateBackButton();
	}

	private async Task SaveSetlistDraftAsync()
	{
		SetlistEdit draft = this.setlistEdit ?? throw new InvalidOperationException("No setlist is being edited.");
		await this.session.SaveSetlistEditAsync(draft).ConfigureAwait(true);
		this.setlistEdit = null;
		this.editingSetlist = false;
		this.UpdateSetlistEditorControls();
		this.RefreshSetlists(draft.Id);
		if (this.currentSetlist is not null)
		{
			this.OpenSetlist(draft.Id);
		}
		else
		{
			this.ShowSetlistOverview();
		}

		this.Status.Text = draft.DeleteRequested ? "Setlist deleted." : "Setlist saved.";
	}

	private async Task LeaveSetlistEditorAsync()
	{
		if (this.leavingSetlistEdit || this.bookMutationInProgress || this.setlistEdit is not SetlistEdit draft)
		{
			return;
		}

		this.leavingSetlistEdit = true;
		try
		{
			await ScreenNavigation.LeaveEditorAsync(
				this,
				draft.HasChanges,
				() => this.RunBookMutationAsync(this.SaveSetlistDraftAsync),
				() =>
				{
					this.setlistEdit = null;
					this.editingSetlist = false;
					this.UpdateSetlistEditorControls();
					this.RefreshSetlists(draft.Id);
					this.OpenSetlist(draft.Id);
					return Task.CompletedTask;
				}).ConfigureAwait(true);
		}
		finally
		{
			this.leavingSetlistEdit = false;
			_ = this.ProcessPendingFilesAsync();
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
		if (this.editingSetlist && !this.bookMutationInProgress && this.currentSetlist is not null
			&& sender is Microsoft.Maui.Controls.Entry { BindingContext: SetlistEntryRow entry } input)
		{
			if (int.TryParse(input.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int position)
				&& position >= 1 && position <= this.selectedSetlistEntries.Count)
			{
				if (position != entry.Position)
				{
					await this.SetEntryPositionAsync(entry.EntryId, position).ConfigureAwait(true);
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

	private Task SetEntryPositionAsync(Guid entryId, int position)
	{
		SetlistEntryRow moved = this.selectedSetlistEntries.Single(entry => entry.EntryId == entryId);
		this.selectedSetlistEntries.Move(this.selectedSetlistEntries.IndexOf(moved), position - 1);
		this.setlistEdit!.SetOrder([.. this.selectedSetlistEntries.Select(entry => entry.EntryId)]);
		this.RenumberSetlistEntries();
		return Task.CompletedTask;
	}

	private async Task ProcessPendingFilesAsync()
	{
		if (!this.processingFiles && !this.FileOpeningBlocked)
		{
			this.processingFiles = true;
			try
			{
				while (!this.FileOpeningBlocked && this.pendingFiles.TryDequeue(out string? path))
				{
					await this.RunBookMutationAsync(() => this.OpenRequestedFileAsync(path)).ConfigureAwait(true);
					this.pendingFilePaths.Remove(path);
				}
			}
			finally
			{
				this.processingFiles = false;
				if (this.pendingFiles.Count == 0)
				{
					this.Window.Title = "Menees ChordBook";
				}
			}
		}
	}

	private async Task OpenRequestedFileAsync(string path)
	{
		try
		{
			using SongFileOpenResult result = await this.session.OpenSongFileAsync(path).ConfigureAwait(true);
			Guid? songId = result.SongId, sheetId = result.SongFileId;
			bool imported = result.Imported;
			if (songId is null && result.HashMatches.Count > 1)
			{
				string[] choices = [.. result.HashMatches.Select((match, index) =>
					$"{index + 1}. {match.Title} — {string.Join(", ", match.Artists)}" + (match.IsArchived ? " (archived)" : string.Empty))];
				string? choice = await this.DisplayActionSheetAsync("Choose the matching song", "Cancel", null, choices).ConfigureAwait(true);
				int index = Array.IndexOf(choices, choice);
				if (index >= 0)
				{
					songId = result.HashMatches[index].SongId;
					sheetId = result.HashMatches[index].SheetId;
				}
			}
			else if (result.Review is not null)
			{
				ImportReviewPage page = new(this.session, result.Review);
				await ScreenNavigation.PushAsync(this, page).ConfigureAwait(true);
				_ = await page.Completion.ConfigureAwait(true);
				BookImportResult? added = page.ImportedFiles.Count > 0 ? page.ImportedFiles[0] : null;
				songId = added?.SongId;
				sheetId = added?.SongFileId;
				imported = added is not null;
			}

			if (songId is Guid id)
			{
				if (imported)
				{
					this.RefreshSongs("File imported.");
				}

				SongRow song = this.session.SearchSongs(string.Empty, includeArchived: true).Single(item => item.Id == id);
				this.performanceSetlistId = null;
				this.performanceEntryIds = [];
				await this.ShowSongAsync(song, [song], 0, "Opened file", sheetOverride: sheetId).ConfigureAwait(true);
			}
		}
#pragma warning disable CA1031 // File activation is a final UI boundary; failures must be visible without losing the current song/editor.
		catch (Exception exception)
		{
			this.Status.Text = exception.Message;
			await this.DisplayAlertAsync("Could not open file", exception.Message, "OK").ConfigureAwait(true);
		}
#pragma warning restore CA1031
	}

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
			await this.ManageSongAsync(this.performanceSongs[this.currentSongIndex].Id, "Edit Song").ConfigureAwait(true);
		}
	}

	private async Task ManageSongAsync(Guid songId, string? requestedAction = null)
		=> await this.RunBookMutationAsync(async () =>
		{
			this.songListPosition.Capture(this.SongList, onlyIfEmpty: this.PerformanceSurface.IsVisible);
			string? action = requestedAction ?? await this.DisplayActionSheetAsync(
				"Song", "Cancel", null, "Edit Song", "Manage Sheets", "Display Settings", "Instrument Settings")
				.ConfigureAwait(true);
			bool saved = false;
			if (action == "Instrument Settings")
			{
				SetlistEntrySettings? entry = this.performanceSetlistId is Guid listId && this.currentSongIndex >= 0
					&& this.currentSongIndex < this.performanceEntryIds.Count
					? this.session.GetSetlistEntrySettings(listId, this.performanceEntryIds[this.currentSongIndex]) : null;
				InstrumentSettingsPage page = new(this.session, songId, entry);
				await ScreenNavigation.PushAsync(this, page).ConfigureAwait(true);
				saved = await page.Completion.ConfigureAwait(true);
			}
			else if (action == "Display Settings")
			{
				DisplaySettingsPage page = new(this.session, songId);
				await ScreenNavigation.PushAsync(this, page).ConfigureAwait(true);
				saved = await page.Completion.ConfigureAwait(true);
			}
			else if (action == "Edit Song")
			{
				this.metronome.Stop();
				SongEditDocument document = await this.session.GetSongEditAsync(songId).ConfigureAwait(true);
				SongEditorPage editor = new(document, this.session);
				await ScreenNavigation.PushAsync(this, editor).ConfigureAwait(true);
				saved = await editor.Completion.ConfigureAwait(true);
			}
			else if (action == "Manage Sheets")
			{
				SongFilesPage page = new(songId, this.session, this.picker);
				await ScreenNavigation.PushAsync(this, page).ConfigureAwait(true);
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
			else if (!this.PerformanceSurface.IsVisible)
			{
				this.songListPosition.Restore(this.SongList);
			}
		}).ConfigureAwait(true);

	private void RefreshCatalogAfterEdit(string status)
	{
		this.songListPosition.Capture(this.SongList, onlyIfEmpty: true);
		if (this.selectingSongs)
		{
			this.EndSongSelection();
		}

		this.allSongs = this.session.SearchSongs(string.Empty, includeArchived: true);
		this.ApplyFilterCore(this.SongSearch.Text, preserveScroll: true);
		this.RefreshSetlists();
		this.Status.Text = status;
		if (!this.PerformanceSurface.IsVisible)
		{
			this.songListPosition.Restore(this.SongList);
		}
	}

	#endregion
}
