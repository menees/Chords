#region Using Directives

using System.IO;
using Menees.Chords.Book.Maui.Services;

#endregion

namespace Menees.Chords.Book.Maui;

public sealed partial class BookIntegrityPage : ContentPage
{
	#region Private Data

	private const int PagePadding = 16;
	private const int ControlSpacing = 8;
	private const int ReportFontSize = 14;
	private const int HeadingFontSize = 24;
	private const int FooterRow = 3;
	private readonly BookSession session;
	private readonly IWindowsPicker picker;
	private readonly Editor report = new() { IsReadOnly = true, AutoSize = EditorAutoSizeOption.Disabled, FontSize = ReportFontSize };
	private readonly Label status = new();
	private readonly Button scan = new() { Text = "Check Again" };
	private readonly Button save = new() { Text = "Save Report…", IsEnabled = false };
	private readonly Button cancel = new() { Text = "Cancel Check", IsVisible = false };
	private readonly Button close = new() { Text = "Close" };
	private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private CancellationTokenSource? operation;
	private bool started;

	#endregion

	#region Constructors

	public BookIntegrityPage(BookSession session, IWindowsPicker picker)
	{
		this.session = session;
		this.picker = picker;
		this.Title = "Check Book";
		Grid layout = new()
		{
			Padding = PagePadding,
			RowSpacing = ControlSpacing,
			RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
		};
		layout.Add(new Label { Text = "Check Book", FontSize = HeadingFontSize }, 0, 0);
		layout.Add(new Label { Text = "Checks every recorded sheet and looks for duplicate or unreferenced files. Your files are kept unchanged." }, 0, 1);
		layout.Add(this.report, 0, 2);
		VerticalStackLayout footer = new() { Spacing = ControlSpacing };
		HorizontalStackLayout buttons = new() { Spacing = ControlSpacing, Children = { this.scan, this.save, this.cancel, this.close } };
		footer.Children.Add(this.status);
		footer.Children.Add(buttons);
		layout.Add(footer, 0, FooterRow);
		this.Content = layout;
		this.scan.Clicked += this.HandleScan;
		this.save.Clicked += this.HandleSave;
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

	private async void HandleSave(object? sender, EventArgs e)
		=> await this.RunAsync(async token =>
		{
			string? path = await this.picker.SaveFileAsync("ChordBook-integrity", ".txt", "Integrity report", token).ConfigureAwait(true);
			if (path is not null)
			{
				string temporary = path + $".{Guid.NewGuid():N}.tmp";
				try
				{
					await File.WriteAllTextAsync(temporary, this.report.Text, token).ConfigureAwait(true);
					token.ThrowIfCancellationRequested();
					File.Move(temporary, path, overwrite: true);
					this.status.Text = $"Report saved: {path}";
				}
				finally
				{
					File.Delete(temporary);
				}
			}
		}).ConfigureAwait(true);

	private async void HandleClose(object? sender, EventArgs e)
	{
		if (this.operation is null)
		{
			await this.Navigation.PopModalAsync().ConfigureAwait(true);
			this.completion.TrySetResult();
		}
	}

	private async Task RunAsync(Func<CancellationToken, Task> action)
	{
		if (this.operation is null)
		{
			using CancellationTokenSource cancellation = new();
			this.operation = cancellation;
			this.scan.IsEnabled = this.save.IsEnabled = this.close.IsEnabled = false;
			this.cancel.IsVisible = true;
			try
			{
				await action(cancellation.Token).ConfigureAwait(true);
			}
			catch (OperationCanceledException)
			{
				this.status.Text = "Check canceled.";
			}
#pragma warning disable CA1031 // Report native picker and storage failures at the UI boundary.
			catch (Exception exception)
			{
				this.status.Text = exception.Message;
			}
#pragma warning restore CA1031
			finally
			{
				this.operation = null;
				this.scan.IsEnabled = this.close.IsEnabled = true;
				this.save.IsEnabled = !string.IsNullOrEmpty(this.report.Text);
				this.cancel.IsVisible = false;
			}
		}
	}

	#endregion
}