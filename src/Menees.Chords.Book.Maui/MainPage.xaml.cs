#region Using Directives

using System.Globalization;
using Menees.Chords.Book.Maui.Services;

#endregion

namespace Menees.Chords.Book.Maui;

public partial class MainPage : ContentPage
{
	#region Private Data

	private readonly BookSession session;
	private readonly IWindowsPicker picker;
	private IReadOnlyList<SongRow> allSongs = [];
	private bool bookMutationInProgress;
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

	private async void HandleLoaded(object? sender, EventArgs e)
	{
		this.Loaded -= this.HandleLoaded;
		await this.RunUiOperationAsync(async () =>
		{
			await this.session.InitializeAsync().ConfigureAwait(true);
			this.RefreshSongs("Ready.");
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
				this.RefreshSongs("Book created.");
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
				this.RefreshSongs("Book opened.");
			}
		}).ConfigureAwait(true);
	}

	private void HandleSearchTextChanged(object? sender, TextChangedEventArgs e) => this.ApplyFilter(e.NewTextValue);

	private async void HandleSongSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (e.CurrentSelection.Count > 0 && e.CurrentSelection[0] is SongRow song)
		{
			await this.RunUiOperationAsync(async () =>
			{
				SongPresentation presentation = await this.session.GetPresentationAsync(song.Id).ConfigureAwait(true);
				this.SelectedTitle.Text = presentation.Title;
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
			}).ConfigureAwait(true);
		}
	}

	private async void HandleSongViewerNavigated(object? sender, WebNavigatedEventArgs e)
	{
		if (this.showingHtmlChart && e.Result == WebNavigationResult.Success)
		{
			await this.RunUiOperationAsync(this.SyncSongViewerPageHeightAsync).ConfigureAwait(true);
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
		IReadOnlyList<SongRow> matches = string.IsNullOrWhiteSpace(query)
			? this.allSongs
			:
			[
				.. this.allSongs.Where(song => song.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
					|| song.Artists.Contains(query, StringComparison.OrdinalIgnoreCase)),
			];
		this.SongList.ItemsSource = matches;
		this.Status.Text = $"Showing {matches.Count:N0} of {this.allSongs.Count:N0} songs.";
	}

	private void RefreshSongs(string status)
	{
		this.allSongs = this.session.GetSongs();
		this.BookName.Text = this.session.Database?.Name;
		this.BookPath.Text = this.session.DirectoryPath;
		this.SongSearch.Text = string.Empty;
		this.ApplyFilter(string.Empty);
		this.Status.Text = $"{status} {this.allSongs.Count:N0} song(s).";
		this.SongList.SelectedItem = this.allSongs.Count > 0 ? this.allSongs[0] : null;
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
			try
			{
				await this.RunUiOperationAsync(operation).ConfigureAwait(true);
			}
			finally
			{
				this.OpenBookButton.IsEnabled = true;
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
