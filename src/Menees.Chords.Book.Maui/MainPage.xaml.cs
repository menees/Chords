#region Using Directives

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui.Platforms.Windows;
using Menees.Chords.Book.Maui.Services;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Maui;

public partial class MainPage : ContentPage
{
	#region Private Data

	private const double JumpButtonHeight = 27;
	private const double JumpButtonFontSize = 12;
	private const int RecentTabIndex = 2;
	private const int ArtistsTabIndex = 3;
	private const int HistoryFlushSeconds = 30;
	private readonly IDispatcherTimer historyTimer;
	private readonly IDispatcherTimer metronomeTimer;
	private readonly BookSession session;
	private readonly IWindowsPicker picker;
	private readonly IMetronomeEngine metronome;
	private readonly WindowsDocumentViewer documentViewer;
	private readonly DocumentPositionStore positionStore = new();
	private readonly PerformanceSessionStore performanceStore = new();
	private DocumentPositionKey? currentPositionKey;
	private IReadOnlyList<SetlistRow> allSetlists = [];
	private IReadOnlyList<SongRow> allSongs = [];
	private Guid? bulkTargetSetlistId;
	private IReadOnlyList<SongGroup> songGroups = [];
	private IReadOnlyList<SetlistGroup> setlistGroups = [];
	private SetlistRow? currentSetlist;
	private IReadOnlyList<SongRow> visibleSongs = [];
	private IReadOnlyList<SetlistRow> visibleSetlists = [];
	private bool bookMutationInProgress;
	private int currentSongIndex = -1;
	private bool editingSetlist;
	private string? performanceContextName;
	private IReadOnlyList<SongRow> performanceSongs = [];
	private ObservableCollection<SetlistEntryRow> selectedSetlistEntries = [];
	private bool selectingSongs;
	private bool showingHtmlChart;
	private bool viewerReady;
	private bool navigatingSong;
	private bool executingInput;
	private MetronomePanel? metronomePanel;
	private Guid? performanceSetlistId;
	private IReadOnlyList<Guid> performanceEntryIds = [];
	private bool performanceLocked;
	private bool confirmingPerformanceExit;
	private bool? previousKeepScreenOn;
	private bool windowActive = true;
	private int viewerGeneration;

	#endregion

	#region Constructors

	public MainPage(BookSession session, IWindowsPicker picker, IMetronomeEngine metronome)
	{
		this.InitializeComponent();
		string[] songActions = ["Manage Sheets", "Display Settings", "Instrument Settings"];
		this.PerformanceSongButton.MenuItems = [.. songActions
			.Select(action => new FluentMenuItem(action, async () =>
			{
				if (this.currentSongIndex >= 0 && this.currentSongIndex < this.performanceSongs.Count)
				{
					await this.ManageSongAsync(this.performanceSongs[this.currentSongIndex].Id, action).ConfigureAwait(true);
				}
			}))];
		this.OpenBookButton.RecentBookSelected += async (_, book) => await this.OpenRecentBookAsync(book).ConfigureAwait(true);
		this.documentViewer = new WindowsDocumentViewer(this.SongViewer);
		this.QuickNotation.ItemsSource = new[] { "Default", "Letter", "Nashville", "Roman" };
		this.QuickNotation.SelectedIndex = 0;
		this.session = session;
		this.picker = picker;
		this.metronome = metronome;
		this.metronomeTimer = this.Dispatcher.CreateTimer();
		this.metronomeTimer.Interval = TimeSpan.FromMilliseconds(50);
		this.metronomeTimer.Tick += (_, _) =>
		{
			this.PerformanceBeats.IsVisible = this.metronomePanel is null && this.metronome.IsRunning && this.metronome.VisualEnabled;
			if (this.PerformanceBeats.IsVisible)
			{
				this.PerformanceBeats.Update(
					this.metronome.BeatsPerMeasure,
					this.metronome.CurrentBeat,
					this.metronome.AccentFirstBeat && this.metronome.CurrentBeat == 1);
			}
		};
		this.historyTimer = this.Dispatcher.CreateTimer();
		this.historyTimer.Interval = TimeSpan.FromSeconds(HistoryFlushSeconds);
		this.historyTimer.Tick += (_, _) =>
		{
			this.session.FlushRecentHistory();
			this.positionStore.Flush();
		};
		this.Unloaded += (_, _) =>
		{
			this.metronome.Stop();
			this.metronomeTimer.Stop();
			this.historyTimer.Stop();
			this.session.FlushRecentHistory();
			this.RestoreScreenPolicy();
			this.positionStore.Flush();
		};
		this.Loaded += async (_, _) =>
		{
			this.historyTimer.Start();
			if (this.PerformanceSurface.IsVisible && !this.viewerReady && this.currentSongIndex >= 0 && this.currentSongIndex < this.performanceSongs.Count)
			{
				await this.ShowSongAsync(
					this.performanceSongs[this.currentSongIndex],
					this.performanceSongs,
					this.currentSongIndex,
					this.performanceContextName)
					.ConfigureAwait(true);
			}
		};
		this.ShowManagementTab(showSetlists: false);
		this.Loaded += this.HandleLoaded;
	}

	#endregion

	#region Public Methods

	public void ApplyViewerTheme() => this.documentViewer.ApplyTheme();

	public void SetWindowActive(bool active)
	{
		this.windowActive = active;
		if (!active)
		{
			this.metronome.Stop();
		}

		this.UpdateScreenPolicy();
	}

	#endregion

	#region Protected Methods

	protected override bool OnBackButtonPressed()
	{
		bool handled = this.TryNavigateBack();
		bool result = handled || base.OnBackButtonPressed();
		return result;
	}

	#endregion

	#region Private Methods

	private static void HandleJumpViewportChanged(object? sender, EventArgs e)
	{
		if (sender is Grid grid && grid.Height > 0)
		{
			foreach (ScrollView scroll in grid.Children.OfType<ScrollView>())
			{
				scroll.HeightRequest = grid.Height;
			}
		}
	}

	private static string GetSectionKey(string title)
	{
		string result = "#";
		foreach (char character in title.TrimStart().Normalize(NormalizationForm.FormD))
		{
			if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
			{
				char upper = char.ToUpperInvariant(character);
				result = upper is >= 'A' and <= 'Z' ? upper.ToString() : "#";
				break;
			}
		}

		return result;
	}

	private async void HandleLoaded(object? sender, EventArgs e)
	{
		this.Loaded -= this.HandleLoaded;
		await this.RunUiOperationAsync(async () =>
		{
			await this.session.InitializeAsync().ConfigureAwait(true);
			this.RefreshSongs(this.IncludeMetadataRefresh("Ready."));
		}).ConfigureAwait(true);
	}

	private async void HandleBookPathTapped(object? sender, TappedEventArgs e)
	{
		if (this.session.DirectoryPath is string path)
		{
			await this.RunUiOperationAsync(async () =>
			{
				await this.picker.OpenFolderAsync(path, CancellationToken.None).ConfigureAwait(true);
				this.Status.Text = "Opened the current book folder in File Explorer.";
			}).ConfigureAwait(true);
		}
	}

	private async void HandleImportClicked(object? sender, EventArgs e)
	{
		await this.RunBookMutationAsync(async () =>
		{
			IReadOnlyList<string> paths = await this.picker.PickFilesAsync(CancellationToken.None).ConfigureAwait(true);
			if (paths.Count == 0)
			{
				this.Status.Text = "Import canceled.";
			}
			else
			{
				this.Status.Text = $"Importing {paths.Count:N0} selected file(s)…";
				await Task.Yield();
				int count = await this.session.ImportAsync(paths).ConfigureAwait(true);
				this.RefreshSongs($"Imported {count:N0} new file(s) from {paths.Count:N0} selected without modifying their bytes.");
			}
		}).ConfigureAwait(true);
	}

	private async void HandleNewBookClicked(object? sender, EventArgs e)
	{
		await this.RunBookMutationAsync(async () =>
		{
			string? name = await this.DisplayPromptAsync(
				"New Book",
				"Enter a name for the new chord book.",
				initialValue: "My ChordBook").ConfigureAwait(true);
			if (string.IsNullOrWhiteSpace(name))
			{
				this.Status.Text = "New Book canceled.";
			}
			else
			{
				await this.session.CreateAsync(name).ConfigureAwait(true);
				this.RefreshSongs(this.IncludeMetadataRefresh("Book created."));
			}
		}).ConfigureAwait(true);
	}

	private async void HandleOpenBookClicked(object? sender, EventArgs e)
	{
		await this.RunBookMutationAsync(async () =>
		{
			string? path = await this.picker.PickFolderAsync(CancellationToken.None).ConfigureAwait(true);
			if (path is null)
			{
				this.Status.Text = "Open Book canceled.";
			}
			else
			{
				await this.session.OpenAsync(path).ConfigureAwait(true);
				this.RefreshSongs(this.IncludeMetadataRefresh("Book opened."));
			}
		}).ConfigureAwait(true);
	}

	private async Task OpenRecentBookAsync(RecentBook book)
	{
		if (!StringComparer.OrdinalIgnoreCase.Equals(book.Path, this.session.DirectoryPath))
		{
			await this.RunBookMutationAsync(async () =>
			{
				await this.session.OpenRecentAsync(book).ConfigureAwait(true);
				this.RefreshSongs(this.IncludeMetadataRefresh("Recent book opened."));
			}).ConfigureAwait(true);
		}
	}

	private async Task RenameCurrentBookAsync()
	{
		string? name = await this.DisplayPromptAsync(
			"Rename Book",
			"Enter the user-facing name for this chord book.",
			initialValue: this.session.Database?.Name).ConfigureAwait(true);
		if (!string.IsNullOrWhiteSpace(name))
		{
			await this.session.RenameAsync(name).ConfigureAwait(true);
			this.RefreshSongs("Book renamed.");
		}
	}

	private void HandleSearchTextChanged(object? sender, TextChangedEventArgs e) => this.ApplyFilter(e.NewTextValue);

	private void HandleShowArchivedChanged(object? sender, CheckedChangedEventArgs e) => this.ApplyFilter(this.SongSearch.Text);

	private async void HandleSongSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (this.selectingSongs)
		{
			HashSet<SongRow> selected = [.. e.CurrentSelection.OfType<SongRow>()];
			foreach (SongRow row in this.visibleSongs)
			{
				row.IsSelected = selected.Contains(row);
			}

			this.SelectedSongCount.Text = $"{selected.Count:N0} song{(selected.Count == 1 ? string.Empty : "s")} selected";
			this.UpdateSongSelectionActions();
		}
		else if (e.CurrentSelection.Count > 0 && e.CurrentSelection[0] is SongRow song)
		{
			this.performanceSetlistId = null;
			this.performanceEntryIds = [];
			await this.ShowSongAsync(song, this.visibleSongs, this.FindVisibleSongIndex(song.Id), null).ConfigureAwait(true);
		}
	}

	private void HandleBackClicked(object? sender, EventArgs e) => this.TryNavigateBack();

	private void HandleSelectSongsClicked(object? sender, EventArgs e) => this.BeginSongSelection(null);

	private void HandleCancelSongSelectionClicked(object? sender, EventArgs e) => this.EndSongSelection();

	private async void HandleAddSelectedSongsClicked(object? sender, EventArgs e)
	{
		IReadOnlyList<SongRow> selected = [.. this.visibleSongs.Where(song => song.IsSelected)];
		if (selected.Count > 0)
		{
			await this.RunBookMutationAsync(async () =>
			{
				SetlistRow? target = this.bulkTargetSetlistId is Guid targetId
					? this.session.GetSetlists().SingleOrDefault(setlist => setlist.Id == targetId)
					: await this.ChooseSetlistAsync().ConfigureAwait(true);
				if (target is not null)
				{
					_ = await this.session.AddSongsToSetlistAsync(target.Id, [.. selected.Select(song => song.Id)])
						.ConfigureAwait(true);
					this.EndSongSelection();
					this.RefreshSetlists(target.Id);
					this.ShowManagementTab(showSetlists: true);
					this.OpenSetlist(target.Id);
					this.Status.Text = $"Added {selected.Count:N0} song(s) to {target.Name}.";
				}
				else
				{
					this.Status.Text = "Add to Setlist canceled.";
				}
			}).ConfigureAwait(true);
		}
	}

	private void HandleSongsTabClicked(object? sender, EventArgs e) => this.ShowManagementTab(showSetlists: false);

	private void HandleSetlistsTabClicked(object? sender, EventArgs e)
	{
		if (this.selectingSongs)
		{
			this.EndSongSelection();
		}

		this.ShowManagementTab(showSetlists: true);
	}

	private void HandleSetlistSearchTextChanged(object? sender, TextChangedEventArgs e)
		=> this.ApplySetlistFilter(e.NewTextValue);

	private void HandleShowArchivedSetlistsChanged(object? sender, CheckedChangedEventArgs e)
		=> this.RefreshSetlists();

	private async void HandleArchiveSetlistClicked(object? sender, EventArgs e)
	{
		if (this.currentSetlist is SetlistRow setlist)
		{
			await this.RunBookMutationAsync(async () =>
			{
				await this.session.SetSetlistArchivedAsync(setlist.Id, !setlist.IsArchived).ConfigureAwait(true);
				this.RefreshSetlists();
				this.ShowSetlistOverview();
				this.Status.Text = setlist.IsArchived
					? $"Restored {setlist.Name}."
					: $"Archived {setlist.Name}. Enable Show archived to restore it.";
			}).ConfigureAwait(true);
		}
	}

	private async void HandleNewSetlistClicked(object? sender, EventArgs e)
	{
		await this.RunBookMutationAsync(async () =>
		{
			string? name = await this.DisplayPromptAsync(
				"New Setlist",
				"Enter a name for the setlist.",
				initialValue: "New Setlist").ConfigureAwait(true);
			if (string.IsNullOrWhiteSpace(name))
			{
				this.Status.Text = "New Setlist canceled.";
			}
			else
			{
				Guid id = await this.session.CreateSetlistAsync(name).ConfigureAwait(true);
				this.RefreshSetlists(id);
				this.OpenSetlist(id);
				this.Status.Text = "Setlist created.";
			}
		}).ConfigureAwait(true);
	}

	private async void HandleRenameSetlistClicked(object? sender, EventArgs e)
	{
		if (this.currentSetlist is SetlistRow setlist)
		{
			await this.RunBookMutationAsync(async () =>
			{
				string? name = await this.DisplayPromptAsync(
					"Rename Setlist",
					"Enter a new name for this setlist.",
					initialValue: setlist.Name).ConfigureAwait(true);
				if (string.IsNullOrWhiteSpace(name))
				{
					this.Status.Text = "Rename Setlist canceled.";
				}
				else
				{
					await this.session.RenameSetlistAsync(setlist.Id, name).ConfigureAwait(true);
					this.RefreshSetlists(setlist.Id);
					this.Status.Text = "Setlist renamed.";
				}
			}).ConfigureAwait(true);
		}
	}

	private void HandleSetlistSelected(object? sender, SelectionChangedEventArgs e)
	{
		if (e.CurrentSelection.Count > 0 && e.CurrentSelection[0] is SetlistRow setlist)
		{
			this.OpenSetlist(setlist.Id);
		}
	}

	private void HandleEditSetlistClicked(object? sender, EventArgs e)
	{
		this.editingSetlist = !this.editingSetlist;
		this.EditSetlistButton.Text = this.editingSetlist ? "Done" : "Edit";
		this.AddSetlistSongsButton.IsVisible = this.editingSetlist;
		this.RenameSetlistButton.IsVisible = this.editingSetlist;
		this.ArchiveSetlistButton.IsVisible = this.editingSetlist;
		this.DeleteSetlistButton.IsVisible = this.editingSetlist && this.currentSetlist?.IsArchived == true;
		this.RefreshSelectedSetlist();
	}

	private void HandleAddSetlistSongsClicked(object? sender, EventArgs e)
	{
		if (this.currentSetlist is SetlistRow setlist)
		{
			this.BeginSongSelection(setlist.Id);
			this.ShowManagementTab(showSetlists: false);
		}
	}

	private async void HandleSetlistEntrySelected(object? sender, SelectionChangedEventArgs e)
	{
		if (!this.editingSetlist && e.CurrentSelection.Count > 0 && e.CurrentSelection[0] is SetlistEntryRow selected
			&& this.currentSetlist is SetlistRow setlist)
		{
			int index = this.FindSetlistEntryIndex(selected.EntryId);
			this.performanceSetlistId = setlist.Id;
			this.performanceEntryIds = [.. this.selectedSetlistEntries.Select(entry => entry.EntryId)];
			await this.ShowSongAsync(
				selected.Song,
				[.. this.selectedSetlistEntries.Select(entry => entry.Song)],
				index,
				setlist.Name).ConfigureAwait(true);
		}
	}

	private async void HandleRemoveSetlistEntryClicked(object? sender, EventArgs e)
	{
		if (sender is Button { CommandParameter: SetlistEntryRow entry }
			&& this.currentSetlist is SetlistRow setlist)
		{
			await this.RunBookMutationAsync(async () =>
			{
				await this.session.RemoveSetlistEntryAsync(setlist.Id, entry.EntryId).ConfigureAwait(true);
				this.RefreshSetlists(setlist.Id);
				this.Status.Text = "Song removed from the setlist.";
			}).ConfigureAwait(true);
		}
	}

	private async void HandleMoveSetlistEntryUpClicked(object? sender, EventArgs e)
		=> await this.MoveSetlistEntryAsync(sender, -1).ConfigureAwait(true);

	private async void HandleMoveSetlistEntryDownClicked(object? sender, EventArgs e)
		=> await this.MoveSetlistEntryAsync(sender, 1).ConfigureAwait(true);

	private async void HandleAddToSetlistClicked(object? sender, EventArgs e)
	{
		if (this.currentSongIndex >= 0 && this.currentSongIndex < this.performanceSongs.Count)
		{
			await this.RunBookMutationAsync(async () =>
			{
				SetlistRow? target = await this.ChooseSetlistAsync().ConfigureAwait(true);
				if (target is not null)
				{
					SongRow song = this.performanceSongs[this.currentSongIndex];
					_ = await this.session.AddSongToSetlistAsync(target.Id, song.Id).ConfigureAwait(true);
					this.RefreshSetlists(target.Id);
					this.Status.Text = $"Added {song.Title} to {target.Name}.";
				}
				else
				{
					this.Status.Text = "Add to Setlist canceled.";
				}
			}).ConfigureAwait(true);
		}
	}

	private async void HandleNextSongClicked(object? sender, EventArgs e)
		=> await this.MovePerformanceAsync(1, false).ConfigureAwait(true);

	private async void HandlePreviousSongClicked(object? sender, EventArgs e)
		=> await this.MovePerformanceAsync(-1, false).ConfigureAwait(true);

	private async Task MovePerformanceAsync(int direction, bool fromBoundary)
	{
		int next = this.currentSongIndex + direction;
		if (!this.navigatingSong && this.PerformanceSurface.IsVisible && (!fromBoundary || this.viewerReady)
			&& next >= 0 && next < this.performanceSongs.Count)
		{
			this.navigatingSong = true;
			try
			{
				await this.ShowSongAsync(
					this.performanceSongs[next],
					this.performanceSongs,
					next,
					this.performanceContextName,
					startAtEnd: fromBoundary && direction < 0,
					restorePosition: !fromBoundary).ConfigureAwait(true);
			}
			finally
			{
				this.navigatingSong = false;
			}
		}
	}

	private async Task ExecutePerformanceCommandAsync(PerformanceCommand command)
	{
		if (!this.executingInput && this.viewerReady && this.PerformanceSurface.IsVisible)
		{
			this.executingInput = true;
			try
			{
				await this.RunUiOperationAsync(async () =>
				{
					switch (command)
					{
						case PerformanceCommand.NextViewport:
						case PerformanceCommand.PreviousViewport:
							await this.documentViewer.MoveViewportAsync(command == PerformanceCommand.NextViewport ? 1 : -1).ConfigureAwait(true);
							break;
						case PerformanceCommand.NextSong:
						case PerformanceCommand.PreviousSong:
							await this.MovePerformanceAsync(command == PerformanceCommand.NextSong ? 1 : -1, false).ConfigureAwait(true);
							break;
						case PerformanceCommand.ToggleMetronome:
							if (this.metronome.IsRunning)
							{
								this.metronome.Stop();
							}
							else if (this.currentSongIndex >= 0)
							{
								await this.metronome.StartAsync(this.session.GetMetronomeSettings(this.performanceSongs[this.currentSongIndex].Id))
									.ConfigureAwait(true);
							}

							break;
					}
				}).ConfigureAwait(true);
			}
			finally
			{
				this.executingInput = false;
			}
		}
	}

	private async void HandleSongViewerNavigating(object? sender, WebNavigatingEventArgs e)
	{
		if (e.Url.StartsWith("chordbook:", StringComparison.OrdinalIgnoreCase))
		{
			e.Cancel = true;
			if (TextViewerBridge.TryReadBoundary(e.Url, this.viewerGeneration, out int direction))
			{
				await this.MovePerformanceAsync(direction, true).ConfigureAwait(true);
			}
			else if (PerformanceInputService.TryReadCommand(e.Url, this.viewerGeneration, out PerformanceCommand command))
			{
				await this.ExecutePerformanceCommandAsync(command).ConfigureAwait(true);
			}
			else if (TextViewerBridge.IsReadyMessage(e.Url, this.viewerGeneration))
			{
				this.viewerReady = true;
				this.FocusSongViewer();
			}
			else if (this.currentPositionKey is not null && this.session.Database is not null
				&& TextViewerBridge.TryReadPosition(e.Url, this.viewerGeneration, out DocumentViewerPosition? position))
			{
				this.positionStore.GetHistory(this.session.Database.Id).Record(this.currentPositionKey, position!);
			}
		}
		else
		{
			this.viewerReady = false;
		}
	}

	private void HandleSongViewerUnloaded(object? sender, EventArgs e) => this.viewerReady = false;

	private async void HandleSongViewerNavigated(object? sender, WebNavigatedEventArgs e)
	{
		this.ApplyViewerTheme();
		if (this.PerformanceSurface.IsVisible && e.Result == WebNavigationResult.Success)
		{
			this.viewerReady = this.showingHtmlChart;
			if (this.showingHtmlChart)
			{
				await this.RunUiOperationAsync(this.SyncSongViewerPageHeightAsync).ConfigureAwait(true);
			}

			this.FocusSongViewer();
		}
	}

	private async void HandleSongViewerSizeChanged(object? sender, EventArgs e)
	{
		if (this.showingHtmlChart)
		{
			await this.RunUiOperationAsync(this.SyncSongViewerPageHeightAsync).ConfigureAwait(true);
		}
	}

	private void ApplyFilter(string? query)
	{
		if (this.CurrentCustomTab is { IsSupported: false })
		{
			this.visibleSongs = [];
			this.songGroups = [];
			this.SongList.ItemsSource = this.songGroups;
			this.JumpLetters.Children.Clear();
			this.Status.Text = "This saved tab uses a filter or grouping not yet supported by this client.";
		}
		else
		{
			this.ApplySupportedFilter(query);
		}
	}

	private void ApplySupportedFilter(string? query)
	{
		int tab = this.ManagementTabs.SelectedIndex;
		this.visibleSongs = tab == RecentTabIndex
			? this.session.SearchRecentSongs(query, this.ShowArchived.IsChecked)
			: this.session.SearchSongs(query, this.ShowArchived.IsChecked);
		if (tab == RecentTabIndex)
		{
			this.songGroups = this.visibleSongs.Count == 0 ? [] : [new SongGroup("Recently opened", this.visibleSongs)];
		}
		else if (this.GroupSongsByArtist)
		{
			var artists = this.session.Database!.Songs.ToDictionary(song => song.Id, song => song.Artists);
			this.songGroups = [.. this.visibleSongs
				.SelectMany(row => artists[row.Id].DefaultIfEmpty("Unknown artist").Distinct(StringComparer.CurrentCultureIgnoreCase)
					.Select(artist => (Artist: artist, Row: row)))
				.GroupBy(item => item.Artist, StringComparer.CurrentCultureIgnoreCase)
				.OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
				.Select(group => new SongGroup(group.Key, group.Select(item => item.Row)
					.OrderBy(song => song.Title, StringComparer.CurrentCultureIgnoreCase)))];
			this.visibleSongs = [.. this.songGroups.SelectMany(group => group).DistinctBy(song => song.Id)];
		}
		else
		{
			this.songGroups = [.. this.visibleSongs
				.GroupBy(song => GetSectionKey(song.Title), StringComparer.Ordinal)
				.Select(group => new SongGroup(group.Key, group))
				.OrderBy(group => group.Key == "#" ? 0 : 1).ThenBy(group => group.Key, StringComparer.Ordinal)];
		}

		this.SongList.ItemsSource = this.songGroups;
		this.RefreshJumpLetters();
		this.Status.Text = $"Showing {this.visibleSongs.Count:N0} of {this.allSongs.Count:N0} songs.";
	}

	private void ApplySetlistFilter(string? query)
	{
		string filter = query?.Trim() ?? string.Empty;
		this.visibleSetlists =
		[
			.. this.allSetlists.Where(setlist => string.IsNullOrEmpty(filter)
					|| setlist.SearchText.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
				.OrderBy(setlist => setlist.Name, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(setlist => setlist.Id),
		];
		this.setlistGroups =
		[
			.. this.visibleSetlists
				.GroupBy(setlist => GetSectionKey(setlist.Name), StringComparer.Ordinal)
				.Select(group => new SetlistGroup(group.Key, group))
				.OrderBy(group => group.Key == "#" ? 0 : 1)
				.ThenBy(group => group.Key, StringComparer.Ordinal),
		];
		this.SetlistList.ItemsSource = this.setlistGroups;
		this.RefreshSetlistJumpLetters();
		this.Status.Text = $"Showing {this.visibleSetlists.Count:N0} of {this.allSetlists.Count:N0} setlists.";
	}

	private void BeginSongSelection(Guid? targetSetlistId)
	{
		this.bulkTargetSetlistId = targetSetlistId;
		this.selectingSongs = true;
		this.SongSearch.IsEnabled = false;
		this.ShowArchived.IsEnabled = false;
		this.SongToolbar.IsVisible = false;
		this.SongSelectionToolbar.IsVisible = true;
		this.SongList.SelectionMode = SelectionMode.Multiple;
		this.SongList.SelectedItems = [];
		foreach (SongRow song in this.visibleSongs)
		{
			song.IsSelectionMode = true;
			song.IsSelected = false;
		}

		this.SelectedSongCount.Text = "0 songs selected";
		this.UpdateSongSelectionActions();
		this.UpdateBackButton();
	}

	private async Task<SetlistRow?> ChooseSetlistAsync()
	{
		IReadOnlyList<SetlistRow> setlists = this.session.GetSetlists();
		const string NewSetlistChoice = "New Setlist…";
		Dictionary<string, SetlistRow> choices = setlists
			.Select((setlist, index) => new KeyValuePair<string, SetlistRow>(
				$"{index + 1}. {setlist.DisplayText}",
				setlist))
			.ToDictionary(pair => pair.Key, pair => pair.Value);
		string? choice = await this.DisplayActionSheetAsync(
			"Add to Setlist",
			"Cancel",
			null,
			[NewSetlistChoice, .. choices.Keys]).ConfigureAwait(true);
		SetlistRow? result = null;
		if (StringComparer.Ordinal.Equals(choice, NewSetlistChoice))
		{
			string? name = await this.DisplayPromptAsync(
				"New Setlist",
				"Enter a name for the new setlist.",
				initialValue: "New Setlist").ConfigureAwait(true);
			if (!string.IsNullOrWhiteSpace(name))
			{
				Guid id = await this.session.CreateSetlistAsync(name).ConfigureAwait(true);
				result = this.session.GetSetlists().Single(setlist => setlist.Id == id);
			}
		}
		else if (choice is not null)
		{
			_ = choices.TryGetValue(choice, out result);
		}

		return result;
	}

	private void EndSongSelection()
	{
		foreach (SongRow song in this.visibleSongs)
		{
			song.IsSelectionMode = false;
			song.IsSelected = false;
		}

		this.SongList.SelectedItems = [];
		this.SongList.SelectionMode = SelectionMode.Single;
		this.SongSelectionToolbar.IsVisible = false;
		this.SongToolbar.IsVisible = true;
		this.SongSearch.IsEnabled = true;
		this.ShowArchived.IsEnabled = true;
		this.selectingSongs = false;
		this.bulkTargetSetlistId = null;
		this.UpdateBackButton();
	}

	private void ExitPerformanceMode()
	{
		this.CloseMetronomePanel();
		this.SetPerformanceLocked(false);
		this.RestoreScreenPolicy();
		this.metronome.Stop();
		this.session.FlushRecentHistory();

		// Hiding the native WebView can raise SizeChanged while its browser is being detached.
		this.viewerGeneration++;
		this.viewerReady = false;
		this.documentViewer.Clear();
		this.positionStore.Flush();
		this.currentPositionKey = null;
		this.showingHtmlChart = false;
		this.PerformanceSurface.IsVisible = false;
		this.metronomeTimer.Stop();
		this.ManagementSurface.IsVisible = true;
		this.SongList.SelectedItem = null;
		this.SetlistEntries.SelectedItem = null;
		if (this.ManagementTabs.SelectedIndex == RecentTabIndex && this.session.Database is not null)
		{
			this.ApplyFilter(this.SongSearch.Text);
		}

		this.UpdateBackButton();
	}

	private void FocusSongViewer()
	{
		if (this.PerformanceSurface.IsVisible)
		{
			this.SongViewer.Focus();
		}
	}

	private void RefreshSongs(string status)
	{
		this.ExitPerformanceMode();
		if (this.selectingSongs)
		{
			this.EndSongSelection();
		}

		this.allSongs = this.session.SearchSongs(string.Empty, includeArchived: true);
		this.BookName.Text = this.session.Database?.Name;
		this.RefreshCustomTabs();
		this.BookPath.Text = this.session.DirectoryPath;
		this.RefreshRecentBooks();
		this.RefreshSetlists();
		this.ShowSetlistOverview();
		this.SongSearch.Text = this.CurrentCustomTab?.Search ?? string.Empty;
		this.ApplyFilter(this.SongSearch.Text);
		this.Status.Text = $"{status} {this.allSongs.Count:N0} song(s).";
	}

	private void RefreshSetlists(Guid? selectedId = null)
	{
		Guid? id = selectedId ?? this.currentSetlist?.Id;
		this.allSetlists = this.session.GetSetlists(this.ShowArchivedSetlists.IsChecked);
		this.SetlistHeading.Text = $"Setlists ({this.allSetlists.Count:N0})";

		this.ApplySetlistFilter(this.SetlistSearch.Text);
		this.currentSetlist = id is Guid currentId
			? this.allSetlists.FirstOrDefault(setlist => setlist.Id == currentId)
			: null;
		this.RefreshSelectedSetlist();
	}

	private void RefreshSelectedSetlist()
	{
		if (this.currentSetlist is SetlistRow setlist)
		{
			this.selectedSetlistEntries = new(this.session.GetSetlistEntries(setlist.Id, this.editingSetlist));
			this.SetlistDetailName.Text = setlist.Name;
			this.SetlistDetailMetadata.Text = setlist.MetadataText;
			this.ArchiveSetlistButton.Text = setlist.IsArchived ? "Restore" : "Archive";
		}
		else
		{
			this.selectedSetlistEntries = [];
			this.SetlistDetailName.Text = string.Empty;
			this.SetlistDetailMetadata.Text = string.Empty;
		}

		this.SetlistEntries.ItemsSource = this.selectedSetlistEntries;
		this.SetlistEntries.CanReorderItems = this.editingSetlist && !this.bookMutationInProgress;
	}

	private void OpenSetlist(Guid setlistId)
	{
		this.currentSetlist = this.allSetlists.Single(setlist => setlist.Id == setlistId);
		this.editingSetlist = false;
		this.EditSetlistButton.Text = "Edit";
		this.AddSetlistSongsButton.IsVisible = false;
		this.RenameSetlistButton.IsVisible = false;
		this.ArchiveSetlistButton.IsVisible = false;
		this.DeleteSetlistButton.IsVisible = false;
		this.RefreshSelectedSetlist();
		this.SetlistList.SelectedItem = null;
		this.SetlistOverview.IsVisible = false;
		this.SetlistDetail.IsVisible = true;
		this.UpdateBackButton();
	}

	private void ShowSetlistOverview()
	{
		this.editingSetlist = false;
		this.SetlistDetail.IsVisible = false;
		this.SetlistOverview.IsVisible = true;
		this.ApplySetlistFilter(this.SetlistSearch.Text);
		this.UpdateBackButton();
	}

	private bool TryNavigateBack()
	{
		bool result = true;
		if (this.PerformanceSurface.IsVisible)
		{
			if (this.performanceLocked)
			{
				_ = this.ConfirmPerformanceExitAsync();
			}
			else
			{
				this.ExitPerformanceMode();
			}
		}
		else if (this.selectingSongs)
		{
			this.EndSongSelection();
		}
		else if (this.SetlistDetail.IsVisible)
		{
			this.ShowSetlistOverview();
		}
		else
		{
			result = false;
		}

		return result;
	}

	private void UpdateBackButton()
		=> this.ManagementBackButton.IsVisible = this.selectingSongs || this.SetlistDetail.IsVisible;

	private async Task MoveSetlistEntryAsync(object? sender, int offset)
	{
		if (sender is Button { CommandParameter: SetlistEntryRow entry }
			&& this.currentSetlist is SetlistRow setlist)
		{
			await this.SetEntryPositionAsync(setlist.Id, entry.EntryId, entry.Position + offset).ConfigureAwait(true);
		}
	}

	private void RefreshRecentBooks() => this.OpenBookButton.RecentBooks = BookSession.GetRecentBooks();

	private void RefreshJumpLetters()
	{
		this.JumpLetters.Children.Clear();
		IEnumerable<SongGroup> jumps = this.ManagementTabs.SelectedIndex == RecentTabIndex ? []
			: this.GroupSongsByArtist ? this.songGroups.DistinctBy(group => GetSectionKey(group.Key)) : this.songGroups;
		foreach (SongGroup group in jumps)
		{
			Button button = new()
			{
				Text = this.GroupSongsByArtist ? GetSectionKey(group.Key) : group.Key,
				CommandParameter = group,
				Padding = 0,
				HeightRequest = JumpButtonHeight,
				MinimumHeightRequest = JumpButtonHeight,
				FontSize = JumpButtonFontSize,
				BackgroundColor = Colors.Transparent,
			};
			button.SetDynamicResource(Button.TextColorProperty, "AppText");
			SemanticProperties.SetDescription(button, $"Jump to songs beginning with {group.Key}");
			button.Clicked += this.HandleJumpLetterClicked;
			this.JumpLetters.Children.Add(button);
		}
	}

	private void RefreshSetlistJumpLetters()
	{
		this.SetlistJumpLetters.Children.Clear();
		foreach (SetlistGroup group in this.setlistGroups)
		{
			Button button = new()
			{
				Text = group.Key,
				CommandParameter = group,
				Padding = 0,
				HeightRequest = JumpButtonHeight,
				MinimumHeightRequest = JumpButtonHeight,
				FontSize = JumpButtonFontSize,
				BackgroundColor = Colors.Transparent,
			};
			button.SetDynamicResource(Button.TextColorProperty, "AppText");
			SemanticProperties.SetDescription(button, $"Jump to setlists beginning with {group.Key}");
			button.Clicked += this.HandleSetlistJumpLetterClicked;
			this.SetlistJumpLetters.Children.Add(button);
		}
	}

	private void HandleJumpLetterClicked(object? sender, EventArgs e)
	{
		if (sender is Button { CommandParameter: SongGroup group } && group.Count > 0)
		{
			this.SongList.ScrollTo(group[0], group, ScrollToPosition.Start, animate: false);
		}
	}

	private void HandleSetlistJumpLetterClicked(object? sender, EventArgs e)
	{
		if (sender is Button { CommandParameter: SetlistGroup group } && group.Count > 0)
		{
			this.SetlistList.ScrollTo(group[0], group, ScrollToPosition.Start, animate: false);
		}
	}

	private async Task RunUiOperationAsync(Func<Task> operation)
	{
		try
		{
			await operation().ConfigureAwait(true);
		}
#pragma warning disable CA1031 // This is the final UI event boundary; report failures without terminating the app.
		catch (Exception exception)
		{
			this.Status.Text = exception.Message;
		}
#pragma warning restore CA1031
	}

	private async void HandleQuickDisplayChanged(object? sender, EventArgs e)
	{
		if (this.PerformanceSurface.IsVisible && this.showingHtmlChart && !this.performanceLocked
			&& this.currentSongIndex >= 0 && this.currentSongIndex < this.performanceSongs.Count)
		{
			await this.ShowSongAsync(
				this.performanceSongs[this.currentSongIndex],
				this.performanceSongs,
				this.currentSongIndex,
				this.performanceContextName,
				displayOnly: true).ConfigureAwait(true);
		}
	}

	private async Task ShowSongAsync(
		SongRow song,
		IReadOnlyList<SongRow> context,
		int index,
		string? contextName,
		bool startAtEnd = false,
		bool restorePosition = true,
		bool displayOnly = false)
	{
		if (!displayOnly)
		{
			this.CloseMetronomePanel();
		}

		int generation = ++this.viewerGeneration;
		this.viewerReady = false;
		await this.RunUiOperationAsync(async () =>
		{
			Guid? entryId = this.performanceSetlistId.HasValue && index >= 0 && index < this.performanceEntryIds.Count
				? this.performanceEntryIds[index] : null;
			if (entryId.HasValue && this.currentPositionKey?.EntryId is Guid previousEntry && previousEntry != entryId
				&& this.session.Database!.BookSettings.StopMetronomeOnSetlistTransition)
			{
				this.metronome.Stop();
			}

			SongPresentation presentation = await this.session.GetPresentationAsync(
				song.Id,
				setlistId: this.performanceSetlistId,
				entryId: entryId,
				notationOverride: this.QuickNotation.SelectedIndex > 0 ? (string)this.QuickNotation.SelectedItem : null,
				transposeOffset: this.QuickTranspose.Offset).ConfigureAwait(true);
			if (generation == this.viewerGeneration)
			{
				this.viewerReady = false;
				this.performanceSongs = context;
				this.currentSongIndex = index;
				this.performanceContextName = contextName;
				this.PerformanceTitle.Text = presentation.Title;
				this.QuickTranspose.OriginalKey = presentation.OriginalKey;
				this.PerformancePosition.Text = this.currentSongIndex >= 0
					? $"{(contextName is null ? string.Empty : contextName + " · ")}{this.currentSongIndex + 1:N0} / {context.Count:N0}"
					: string.Empty;
				this.PreviousSongButton.IsEnabled = this.currentSongIndex > 0;
				this.NextSongButton.IsEnabled = this.currentSongIndex >= 0 && this.currentSongIndex + 1 < context.Count;
				this.ManagementSurface.IsVisible = false;
				this.PerformanceSurface.IsVisible = true;
				this.metronomeTimer.Start();
				this.UpdateScreenPolicy();
				await Task.Yield();
				if (generation == this.viewerGeneration)
				{
					this.showingHtmlChart = presentation.PdfPath is null;
					this.QuickNotation.IsEnabled = this.showingHtmlChart && !this.performanceLocked;
					this.QuickTranspose.IsEnabled = this.QuickNotation.IsEnabled;
					Func<Stream>? openPdf = presentation.PdfPath is string path
						? () => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan) : null;
					this.currentPositionKey = new(song.Id, entryId, presentation.FileId);
					DocumentViewerPosition? savedPosition = restorePosition
						? this.positionStore.GetHistory(this.session.Database!.Id).Find(this.currentPositionKey) : null;
					DocumentViewerContent content = new(presentation.Html, openPdf, savedPosition, this.session.Database!.BookSettings.InputBindings);
					await this.documentViewer.LoadAsync(content, generation, startAtEnd).ConfigureAwait(true);
					if (generation == this.viewerGeneration)
					{
						this.Status.Text = "Showing the selected sheet.";
						this.FocusSongViewer();
						if (!displayOnly)
						{
							this.session.RecordSongAccess(song.Id);
						}

						try
						{
							if (!displayOnly)
							{
								await this.performanceStore.RecordAsync(
									this.session.Database!.Id, this.performanceSetlistId, context, index, entryId, contextName)
									.ConfigureAwait(true);
							}
						}
						catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
						{
							this.Status.Text = "Showing the sheet. The last performance could not be saved on this device.";
						}

						if (!displayOnly && generation == this.viewerGeneration && this.metronome.IsRunning)
						{
							await this.metronome.StartAsync(this.session.GetMetronomeSettings(song.Id)).ConfigureAwait(true);
						}
					}
				}
			}
		}).ConfigureAwait(true);
	}

	private int FindSetlistEntryIndex(Guid entryId)
	{
		int result = -1;
		for (int index = 0; index < this.selectedSetlistEntries.Count; index++)
		{
			if (this.selectedSetlistEntries[index].EntryId == entryId)
			{
				result = index;
				break;
			}
		}

		return result;
	}

	private int FindVisibleSongIndex(Guid songId)
	{
		int result = -1;
		for (int index = 0; index < this.visibleSongs.Count; index++)
		{
			if (this.visibleSongs[index].Id == songId)
			{
				result = index;
				break;
			}
		}

		return result;
	}

	private string IncludeMetadataRefresh(string status)
	{
		int updatedSongCount = this.session.LastMetadataRefresh?.UpdatedSongCount ?? 0;
		return updatedSongCount > 0
			? $"{status} Refreshed directive metadata for {updatedSongCount:N0} song(s)."
			: status;
	}

	private async Task RunBookMutationAsync(Func<Task> operation)
	{
		if (this.bookMutationInProgress)
		{
			this.Status.Text = "Another book operation is still running.";
		}
		else
		{
			this.bookMutationInProgress = true;
			this.SetlistEntries.CanReorderItems = false;
			this.ImportButton.IsEnabled = false;
			this.NewBookButton.IsEnabled = false;
			this.OpenBookButton.IsEnabled = false;
			this.RenameBookButton.IsEnabled = false;
			try
			{
				await this.RunUiOperationAsync(operation).ConfigureAwait(true);
			}
			finally
			{
				this.OpenBookButton.IsEnabled = true;
				this.RenameBookButton.IsEnabled = true;
				this.NewBookButton.IsEnabled = true;
				this.ImportButton.IsEnabled = true;
				this.bookMutationInProgress = false;
				this.SetlistEntries.CanReorderItems = this.editingSetlist;
			}
		}
	}

	private async Task SyncSongViewerPageHeightAsync()
	{
		if (!this.viewerReady || !this.showingHtmlChart || !this.PerformanceSurface.IsVisible
			|| this.SongViewer.Handler is null)
		{
			return;
		}

		int generation = this.viewerGeneration;
		int pageHeight = (int)Math.Round(this.SongViewer.Height, MidpointRounding.AwayFromZero);
		if (pageHeight > 0)
		{
			string height = pageHeight.ToString(CultureInfo.InvariantCulture);
			string script = $$"""
				(() => {
					document.documentElement.style.setProperty('--page-block-size', '{{height}}px');
					document.querySelector('.chord-sheet')?.dispatchEvent(new Event('menees-chords-repaginate'));
				})();
				""";
			try
			{
				_ = await this.SongViewer.EvaluateJavaScriptAsync(script).ConfigureAwait(true);
			}
			catch (InvalidOperationException) when (generation != this.viewerGeneration || !this.PerformanceSurface.IsVisible)
			{
				// A pending resize belongs to a viewer that has since been closed or replaced.
			}
			catch (System.Runtime.InteropServices.COMException) when (generation != this.viewerGeneration || !this.viewerReady)
			{
				// Windows may report native browser teardown after the resize was dispatched.
			}
		}
	}

	private void ShowManagementTab(bool showSetlists, bool preserveSongTab = false)
	{
		this.SongToolbar.IsVisible = !showSetlists && !this.selectingSongs;
		this.SongSelectionToolbar.IsVisible = !showSetlists && this.selectingSongs;
		this.SongSurface.IsVisible = !showSetlists;
		this.SetlistSurface.IsVisible = showSetlists;
		if (showSetlists || !preserveSongTab)
		{
			this.ManagementTabs.SelectedIndex = showSetlists ? 1 : 0;
		}
	}

	#endregion
}
