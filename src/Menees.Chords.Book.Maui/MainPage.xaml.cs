#region Using Directives

using System.Globalization;
using System.Text;
using Menees.Chords.Book.Maui.Services;

#endregion

namespace Menees.Chords.Book.Maui;

public partial class MainPage : ContentPage
{
	#region Private Data

	private const double JumpButtonHeight = 27;
	private const double JumpButtonFontSize = 12;
	private readonly BookSession session;
	private readonly IWindowsPicker picker;
	private IReadOnlyList<SongRow> allSongs = [];
	private IReadOnlyList<SongGroup> songGroups = [];
	private IReadOnlyList<SongRow> visibleSongs = [];
	private bool bookMutationInProgress;
	private int currentSongIndex = -1;
	private bool refreshingRecentBooks;
	private bool showingHtmlChart;

	#endregion

	#region Constructors

	public MainPage(BookSession session, IWindowsPicker picker)
	{
		this.InitializeComponent();
		this.session = session;
		this.picker = picker;
		this.Loaded += this.HandleLoaded;
	}

	#endregion

	#region Private Methods

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

	private async void HandleRecentBookSelected(object? sender, EventArgs e)
	{
		if (!this.refreshingRecentBooks && this.RecentBooks.SelectedItem is RecentBook book
			&& !StringComparer.OrdinalIgnoreCase.Equals(book.Path, this.session.DirectoryPath))
		{
			await this.RunBookMutationAsync(async () =>
			{
				await this.session.OpenRecentAsync(book).ConfigureAwait(true);
				this.RefreshSongs(this.IncludeMetadataRefresh("Recent book opened."));
			}).ConfigureAwait(true);
		}
	}

	private async void HandleRenameBookClicked(object? sender, EventArgs e)
	{
		await this.RunBookMutationAsync(async () =>
		{
			string? name = await this.DisplayPromptAsync(
				"Rename Book",
				"Enter the user-facing name for this chord book.",
				initialValue: this.session.Database?.Name).ConfigureAwait(true);
			if (string.IsNullOrWhiteSpace(name))
			{
				this.Status.Text = "Rename Book canceled.";
			}
			else
			{
				await this.session.RenameAsync(name).ConfigureAwait(true);
				this.RefreshSongs("Book renamed.");
			}
		}).ConfigureAwait(true);
	}

	private void HandleSearchTextChanged(object? sender, TextChangedEventArgs e) => this.ApplyFilter(e.NewTextValue);

	private void HandleShowArchivedChanged(object? sender, CheckedChangedEventArgs e) => this.ApplyFilter(this.SongSearch.Text);

	private async void HandleSongSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (e.CurrentSelection.Count > 0 && e.CurrentSelection[0] is SongRow song)
		{
			await this.ShowSongAsync(song).ConfigureAwait(true);
		}
	}

	private void HandleExitPerformanceClicked(object? sender, EventArgs e) => this.ExitPerformanceMode();

	private async void HandleNextSongClicked(object? sender, EventArgs e)
	{
		if (this.currentSongIndex >= 0 && this.currentSongIndex + 1 < this.visibleSongs.Count)
		{
			await this.ShowSongAsync(this.visibleSongs[this.currentSongIndex + 1]).ConfigureAwait(true);
		}
	}

	private async void HandlePreviousSongClicked(object? sender, EventArgs e)
	{
		if (this.currentSongIndex > 0)
		{
			await this.ShowSongAsync(this.visibleSongs[this.currentSongIndex - 1]).ConfigureAwait(true);
		}
	}

	private async void HandleSongViewerNavigated(object? sender, WebNavigatedEventArgs e)
	{
		if (e.Result == WebNavigationResult.Success)
		{
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
		this.visibleSongs = this.session.SearchSongs(query, this.ShowArchived.IsChecked);
		this.songGroups =
		[
			.. this.visibleSongs
				.GroupBy(song => GetSectionKey(song.Title), StringComparer.Ordinal)
				.Select(group => new SongGroup(group.Key, group))
				.OrderBy(group => group.Key == "#" ? 0 : 1)
				.ThenBy(group => group.Key, StringComparer.Ordinal),
		];
		this.SongList.ItemsSource = this.songGroups;
		this.RefreshJumpLetters();
		this.Status.Text = $"Showing {this.visibleSongs.Count:N0} of {this.allSongs.Count:N0} songs.";
	}

	private void ExitPerformanceMode()
	{
		this.PerformanceSurface.IsVisible = false;
		this.ManagementSurface.IsVisible = true;
		this.SongList.SelectedItem = null;
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
		this.allSongs = this.session.SearchSongs(string.Empty, includeArchived: true);
		this.BookName.Text = this.session.Database?.Name;
		this.BookPath.Text = this.session.DirectoryPath;
		this.RefreshRecentBooks();
		this.SongSearch.Text = string.Empty;
		this.ApplyFilter(string.Empty);
		this.Status.Text = $"{status} {this.allSongs.Count:N0} song(s).";
	}

	private void RefreshRecentBooks()
	{
		this.refreshingRecentBooks = true;
		try
		{
			List<RecentBook> books = [.. BookSession.GetRecentBooks()];
			this.RecentBooks.ItemsSource = books;
			this.RecentBooks.SelectedItem = books.FirstOrDefault(
				book => StringComparer.OrdinalIgnoreCase.Equals(book.Path, this.session.DirectoryPath));
		}
		finally
		{
			this.refreshingRecentBooks = false;
		}
	}

	private void RefreshJumpLetters()
	{
		this.JumpLetters.Children.Clear();
		foreach (SongGroup group in this.songGroups)
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
				TextColor = Color.FromArgb("#332E38"),
			};
			SemanticProperties.SetDescription(button, $"Jump to songs beginning with {group.Key}");
			button.Clicked += this.HandleJumpLetterClicked;
			this.JumpLetters.Children.Add(button);
		}
	}

	private void HandleJumpLetterClicked(object? sender, EventArgs e)
	{
		if (sender is Button { CommandParameter: SongGroup group } && group.Count > 0)
		{
			this.SongList.ScrollTo(group[0], group, ScrollToPosition.Start, animate: false);
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

	private async Task ShowSongAsync(SongRow song)
	{
		await this.RunUiOperationAsync(async () =>
		{
			SongPresentation presentation = await this.session.GetPresentationAsync(song.Id).ConfigureAwait(true);
			this.currentSongIndex = this.FindVisibleSongIndex(song.Id);
			this.PerformanceTitle.Text = presentation.Title;
			this.PerformancePosition.Text = this.currentSongIndex >= 0
				? $"{this.currentSongIndex + 1:N0} / {this.visibleSongs.Count:N0}"
				: string.Empty;
			this.PreviousSongButton.IsEnabled = this.currentSongIndex > 0;
			this.NextSongButton.IsEnabled = this.currentSongIndex >= 0 && this.currentSongIndex + 1 < this.visibleSongs.Count;
			this.showingHtmlChart = false;
			if (presentation.PdfPath is not null)
			{
				this.SongViewer.Source = new UrlWebViewSource { Url = new Uri(presentation.PdfPath).AbsoluteUri };
				this.Status.Text = "Showing the managed PDF.";
			}
			else
			{
				this.showingHtmlChart = true;
				this.SongViewer.Source = new HtmlWebViewSource { Html = presentation.Html ?? string.Empty };
				this.Status.Text = "Rendered the managed text chart.";
			}

			this.ManagementSurface.IsVisible = false;
			this.PerformanceSurface.IsVisible = true;
			await Task.Yield();
			this.FocusSongViewer();
		}).ConfigureAwait(true);
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
			this.ImportButton.IsEnabled = false;
			this.NewBookButton.IsEnabled = false;
			this.OpenBookButton.IsEnabled = false;
			this.RenameBookButton.IsEnabled = false;
			this.RecentBooks.IsEnabled = false;
			try
			{
				await this.RunUiOperationAsync(operation).ConfigureAwait(true);
			}
			finally
			{
				this.OpenBookButton.IsEnabled = true;
				this.RecentBooks.IsEnabled = true;
				this.RenameBookButton.IsEnabled = true;
				this.NewBookButton.IsEnabled = true;
				this.ImportButton.IsEnabled = true;
				this.bookMutationInProgress = false;
			}
		}
	}

	private async Task SyncSongViewerPageHeightAsync()
	{
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
			_ = await this.SongViewer.EvaluateJavaScriptAsync(script).ConfigureAwait(true);
		}
	}

	#endregion
}
