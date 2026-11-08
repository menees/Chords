namespace Menees.Chords.Book.Maui;

public sealed partial class BookIntegrityPage : ContentPage
{
	#region Private Data

	private const int PagePadding = 8;
	private const int ControlSpacing = 8;
	private const int ReportFontSize = 14;
	private const int FooterRow = 3;
	private readonly BookSession session;
	private readonly Editor report = new() { IsReadOnly = true, AutoSize = EditorAutoSizeOption.Disabled, FontSize = ReportFontSize };
	private readonly Label status = new();
	private readonly FluentIconButton scan = new() { Icon = "ArrowClockwise", Description = "Validate Again" };
	private readonly FluentIconButton cancel = new() { Icon = "Stop", Description = "Cancel Validation", IsVisible = false };
	private readonly FluentIconButton close = new() { Icon = "ArrowLeft", Description = "Back" };
	private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private CancellationTokenSource? operation;
	private bool started;

	#endregion

	#region Constructors

	public BookIntegrityPage(BookSession session)
	{
		this.session = session;
		this.Title = "Validate Book";
		Grid layout = new()
		{
			Padding = PagePadding,
			RowSpacing = ControlSpacing,
			RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
		};
		layout.Add(ScreenLayout.Header("Validate Book", this.close, this.scan, this.cancel), 0, 0);
		Label description = new()
		{
			Text = "Checks every sheet, including archived and recovery sheets. Files remain unchanged.",
		};
		layout.Add(description, 0, 1);
		layout.Add(this.report, 0, 2);
		layout.Add(this.status, 0, FooterRow);
		this.Content = layout;
		this.scan.Clicked += this.HandleScan;
		this.cancel.Clicked += (_, _) => this.operation?.Cancel();
		this.close.Clicked += this.HandleClose;
		this.Loaded += (_, _) =>
		{
			if (!this.started)
			{
				this.started = true;
				this.HandleScan(this, EventArgs.Empty);
			}
		};
	}

	#endregion

	#region Public Properties

	public Task Completion => this.completion.Task;

	#endregion

	#region Protected Methods

	protected override bool OnBackButtonPressed()
	{
		this.HandleClose(this, EventArgs.Empty);
		return true;
	}

	#endregion

	#region Private Methods

	private async void HandleScan(object? sender, EventArgs e)
		=> await this.RunAsync(async token =>
		{
			this.status.Text = "Checking sheet contents…";
			this.report.Text = await this.session.CreateIntegrityReportAsync(token).ConfigureAwait(true);
			this.status.Text = "Check complete.";
		}).ConfigureAwait(true);

	private async void HandleClose(object? sender, EventArgs e)
	{
		if (this.operation is null)
		{
			await this.Navigation.PopAsync().ConfigureAwait(true);
			this.completion.TrySetResult();
		}
	}

	private async Task RunAsync(Func<CancellationToken, Task> action)
	{
		if (this.operation is null)
		{
			using CancellationTokenSource cancellation = new();
			this.operation = cancellation;
			this.scan.IsEnabled = this.close.IsEnabled = false;
			this.cancel.IsVisible = true;
			try
			{
				await action(cancellation.Token).ConfigureAwait(true);
			}
			catch (OperationCanceledException)
			{
				this.status.Text = "Check canceled.";
			}
#pragma warning disable CA1031 // Report storage failures at the UI boundary.
			catch (Exception exception)
			{
				this.status.Text = exception.Message;
			}
#pragma warning restore CA1031
			finally
			{
				this.operation = null;
				this.scan.IsEnabled = this.close.IsEnabled = true;
				this.cancel.IsVisible = false;
			}
		}
	}

	#endregion
}