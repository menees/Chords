#region Using Directives

using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Maui;

public sealed partial class BookReconciliationPage : ContentPage
{
	#region Private Data

	private readonly BookSession session;
	private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private BookReconcilePreview preview;
	private List<ReconciliationRow> rows = [];
	private bool busy;
	private bool changed;

	#endregion

	#region Public API

	public BookReconciliationPage(BookSession session, BookReconcilePreview preview)
	{
		this.session = session;
		this.preview = preview;
		this.InitializeComponent();
		this.ShowPreview();
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

	private void ShowPreview()
	{
		this.rows = [.. this.preview.Changes.Select(change => new ReconciliationRow
		{
			Title = change.CurrentName.Replace($" [{change.FileId:D}]", string.Empty, StringComparison.OrdinalIgnoreCase),
			Description = (change.ContentChanged ? "Source content changed. " : string.Empty)
				+ (change.PreviousName != change.CurrentName ? "Renamed from " + change.PreviousName : string.Empty),
		})];
		this.rows.AddRange(this.preview.Conflicts.Select(conflict => new ReconciliationRow
		{
			Title = conflict.SongTitle + " — " + conflict.Field,
			Description = $"Catalog: {conflict.CatalogValue}\nSource: {conflict.SourceValue}",
			ConflictKey = conflict.Key,
		}));
		this.rows.AddRange(this.preview.Problems.Select(problem => new ReconciliationRow
		{
			Title = problem.RelativePath,
			Description = "Needs attention: " + problem.Message,
		}));
		this.items.ItemsSource = this.rows;
		this.apply.IsEnabled = this.preview.HasChanges;
		this.status.Text = this.preview.HasChanges || this.rows.Count > 0
			? $"{this.preview.Changes.Count:N0} changed sheets; {this.preview.Conflicts.Count:N0} catalog conflicts; {this.preview.Problems.Count:N0} issues."
			: "No external changes found.";
	}

	private async void HandleRefresh(object? sender, EventArgs e)
		=> await this.RunAsync(async () =>
		{
			this.preview = await this.session.PreviewReconcileAsync().ConfigureAwait(true);
			this.ShowPreview();
		}).ConfigureAwait(true);

	private async void HandleApply(object? sender, EventArgs e)
		=> await this.RunAsync(async () =>
		{
			HashSet<string> useSource = [.. this.rows.Where(row => row.UseSourceValue && row.ConflictKey is not null).Select(row => row.ConflictKey!)];
			BookReconcileResult result = await this.session.ApplyReconcileAsync(this.preview, useSource).ConfigureAwait(true);
			this.changed = true;
			this.apply.IsEnabled = false;
			this.status.Text = $"Applied {result.RenamedFileCount:N0} renames and {result.ChangedFileCount:N0} content updates."
				+ " Unresolved issues remain for review.";
		}).ConfigureAwait(true);

	private async Task RunAsync(Func<Task> action)
	{
		if (!this.busy)
		{
			this.busy = true;
			this.actions.IsEnabled = false;
			this.items.IsEnabled = false;
			try
			{
				await action().ConfigureAwait(true);
			}
#pragma warning disable CA1031 // Keep the review open and show recoverable filesystem/concurrency failures.
			catch (Exception exception)
			{
				this.status.Text = exception.Message + " Refresh the review before applying again.";
				this.apply.IsEnabled = false;
			}
#pragma warning restore CA1031
			finally
			{
				this.actions.IsEnabled = true;
				this.items.IsEnabled = true;
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
