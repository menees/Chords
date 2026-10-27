#region Using Directives

using System.IO;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Maui;

/// <summary>Reviews a batch without mutations; explicit actions commit together on Apply.</summary>
public sealed partial class ImportReviewPage : ContentPage
{
	#region Private Data

	private readonly BookSession session;
	private readonly IReadOnlyList<string> paths;
	private readonly TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private BookImportReview? review;
	private IReadOnlyList<ImportReviewRow> rows = [];
	private CancellationTokenSource? operation;
	private bool synchronizing;
	private bool loaded;

	#endregion

	#region Constructors

	public ImportReviewPage(BookSession session, IReadOnlyList<string> paths)
	{
		this.session = session;
		this.paths = paths;
		this.InitializeComponent();
		this.filter.ItemsSource = new[] { "Possible conflicts", "New songs", "Exact duplicates", "All files" };
		this.action.ItemsSource = new[] { "Skip", "Import as Separate Song", "Add as Sheet to Existing Song" };
		this.Loaded += async (_, _) =>
		{
			if (!this.loaded)
			{
				this.loaded = true;
				await this.RunAsync(async token =>
				{
					this.review = await this.session.ReviewImportAsync(this.paths, token).ConfigureAwait(true);
					this.rows = [.. this.review.Items.Select(item => new ImportReviewRow(item))];
					this.summary.Text = $"{this.rows.Count:N0} selected · "
						+ $"{this.rows.Count(row => row.Item.Conflict == BookImportConflict.NewSong):N0} new · "
						+ $"{this.rows.Count(row => row.Item.Conflict == BookImportConflict.ExactDuplicate):N0} exact duplicates · "
						+ $"{this.rows.Count(row => IsConflict(row.Item)):N0} possible conflicts";
					this.filter.SelectedIndex = this.rows.Any(row => IsConflict(row.Item)) ? 0 : 1;
					this.UpdateSummary();
				}).ConfigureAwait(true);
			}
		};
	}

	#endregion

	#region Public Properties

	public Task<string?> Completion => this.completion.Task;

	#endregion

	#region Protected Methods

	protected override bool OnBackButtonPressed()
	{
		this.HandleBack(this, EventArgs.Empty);
		return true;
	}

	#endregion

	#region Private Methods

	private static bool IsConflict(BookImportItem item) => item.Conflict is BookImportConflict.IdenticalContent or BookImportConflict.PossibleVersion;

	private void HandleFilterChanged(object? sender, EventArgs e)
	{
		IReadOnlyList<ImportReviewRow> visible = this.filter.SelectedIndex switch
		{
			0 => [.. this.rows.Where(row => IsConflict(row.Item))],
			1 => [.. this.rows.Where(row => row.Item.Conflict == BookImportConflict.NewSong)],
			2 => [.. this.rows.Where(row => row.Item.Conflict == BookImportConflict.ExactDuplicate)],
			_ => this.rows,
		};
		this.files.ItemsSource = visible;
		this.files.SelectedItem = null;
		this.selection.IsVisible = false;
		this.compare.IsEnabled = false;
	}

	private void HandleSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		ImportReviewRow? row = this.files.SelectedItem as ImportReviewRow;
		this.selection.IsVisible = row is not null;
		this.synchronizing = true;
		try
		{
			if (row is not null)
			{
				this.selectedName.Text = row.Title + " · " + row.Description;
				this.candidate.ItemsSource = row.Item.Candidates.Select(value => value.Title + " — " + string.Join(", ", value.Artists)
					+ " · " + Path.GetFileName(value.SheetName).Replace($" [{value.SheetId:D}]", string.Empty, StringComparison.OrdinalIgnoreCase)
					+ (value.IsArchived ? " (archived song)" : value.IsIncoming ? " (earlier selected file)" : string.Empty)).ToArray();
				this.candidate.SelectedIndex = row.Item.Candidates.Count == 1 ? 0 : -1;
				if (row.Decision.TargetSongId is Guid target)
				{
					this.candidate.SelectedIndex = row.Item.Candidates.ToList().FindIndex(value => value.SongId == target);
				}

				this.candidate.IsVisible = row.Item.Candidates.Count > 0;
				this.action.SelectedIndex = (int)row.Decision.Action;
			}
		}
		finally
		{
			this.synchronizing = false;
		}

		this.UpdateControls();
	}

	private void HandleDecisionChanged(object? sender, EventArgs e)
	{
		if (!this.synchronizing && this.files.SelectedItem is ImportReviewRow row && this.action.SelectedIndex >= 0)
		{
			BookImportAction choice = (BookImportAction)this.action.SelectedIndex;
			Guid? target = choice == BookImportAction.AddSheet && this.candidate.SelectedIndex >= 0
				? row.Item.Candidates[this.candidate.SelectedIndex].SongId : null;
			row.Decision = new(row.Item.Id, choice, target);
			this.UpdateSummary();
			this.UpdateControls();
		}
	}

	private void UpdateSummary()
	{
		this.status.Text = $"Will create {this.rows.Count(row => row.Decision.Action == BookImportAction.ImportNewSong):N0} songs, "
			+ $"add {this.rows.Count(row => row.Decision.Action == BookImportAction.AddSheet):N0} sheets, "
			+ $"skip {this.rows.Count(row => row.Decision.Action == BookImportAction.Skip):N0} files. Nothing changes until Apply.";
	}

	private async void HandleCompare(object? sender, EventArgs e)
	{
		if (this.review is BookImportReview snapshot && this.files.SelectedItem is ImportReviewRow row && this.candidate.SelectedIndex >= 0)
		{
			BookImportCandidate match = row.Item.Candidates[this.candidate.SelectedIndex];
			await this.RunAsync(async token =>
			{
				var comparison = await this.session.CompareImportAsync(snapshot, row.Item.Id, match.SheetId, token).ConfigureAwait(true);
				HashSet<Guid> incoming = [.. snapshot.Items.Select(item => item.Id)];
				SongVersionComparisonPage page = new(comparison, this.session, side => incoming.Contains(side.Id)
					? snapshot.OpenCapturedSource(side.Id) : File.OpenRead(Path.Combine(this.session.DirectoryPath!, side.Name)));
				await this.Navigation.PushAsync(page).ConfigureAwait(true);
				await page.Completion.ConfigureAwait(true);
			}).ConfigureAwait(true);
		}
	}

	private async void HandleApply(object? sender, EventArgs e)
	{
		if (this.review is BookImportReview snapshot)
		{
			string? result = null;
			await this.RunAsync(async token =>
			{
				IReadOnlyList<BookImportDecision> choices = [.. this.rows.Select(row => row.Decision)];
				IReadOnlyList<BookImportResult> added = await this.session.ApplyImportAsync(snapshot, choices, token).ConfigureAwait(true);
				HashSet<Guid> created = [.. this.rows.Where(row => row.Decision.Action == BookImportAction.ImportNewSong).Select(row => row.Item.Id)];
				int newSongs = added.Select(item => item.SongId).Where(created.Contains).Distinct().Count();
				int existingSongs = added.Select(item => item.SongId).Distinct().Count() - newSongs;
				result = $"Imported {added.Count:N0} sheets into {newSongs:N0} new songs and {existingSongs:N0} existing songs; "
					+ $"skipped {this.rows.Count - added.Count:N0} files. Source bytes and existing song metadata retained.";
			}).ConfigureAwait(true);
			if (result is not null)
			{
				await this.CloseAsync(result).ConfigureAwait(true);
			}
		}
	}

	private void HandleCancel(object? sender, EventArgs e) => this.operation?.Cancel();

	private async void HandleBack(object? sender, EventArgs e)
	{
		if (this.operation is null)
		{
			await this.CloseAsync(null).ConfigureAwait(true);
		}
	}

	private async Task CloseAsync(string? result)
	{
		this.review?.Dispose();
		this.review = null;
		await this.Navigation.PopAsync().ConfigureAwait(true);
		this.completion.TrySetResult(result);
	}

	private async Task RunAsync(Func<CancellationToken, Task> action)
	{
		if (this.operation is null)
		{
			using CancellationTokenSource cancellation = new();
			this.operation = cancellation;
			this.UpdateControls();
			try
			{
				await action(cancellation.Token).ConfigureAwait(true);
			}
			catch (OperationCanceledException)
			{
				this.status.Text = "Import canceled. The book is unchanged.";
			}
#pragma warning disable CA1031 // Keep failures visible in the review, retaining all choices for inspection or retry.
			catch (Exception exception)
			{
				this.status.Text = exception.Message;
			}
#pragma warning restore CA1031
			finally
			{
				this.operation = null;
				this.UpdateControls();
			}
		}
	}

	private void UpdateControls()
	{
		bool idle = this.operation is null;
		this.files.IsEnabled = this.filter.IsEnabled = this.selection.IsEnabled = idle;
		this.apply.IsEnabled = idle && this.review is not null
			&& this.rows.All(row => row.Decision.Action != BookImportAction.AddSheet || row.Decision.TargetSongId is not null);
		this.compare.IsEnabled = idle && this.files.SelectedItem is ImportReviewRow row && this.candidate.SelectedIndex >= 0
			&& row.Item.Candidates[this.candidate.SelectedIndex].CanCompare;
		this.cancel.IsVisible = !idle;
	}

	#endregion
}