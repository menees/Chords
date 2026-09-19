#region Using Directives

using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui.Services;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Maui;

public sealed partial class BookBackupPage : ContentPage
{
	#region Private Data

	private const int PagePadding = 16;
	private const int ControlSpacing = 12;
	private const int PageWidth = 660;
	private readonly BookSession session;
	private readonly IWindowsPicker picker;
	private readonly Button backup = new() { Text = "Backup Current Book…" };
	private readonly Button replace = new() { Text = "Replace Current Book…" };
	private readonly Button restore = new() { Text = "Restore as New Book…" };
	private readonly Button backupSettings = new() { Text = "Backup Settings…" };
	private readonly Button restoreSettings = new() { Text = "Restore Settings…" };
	private readonly Button cancel = new() { Text = "Cancel Operation", IsVisible = false };
	private readonly FluentIconButton close = new() { Icon = "Dismiss", Description = "Close" };
	private readonly Label status = new();
	private readonly ActivityIndicator progress = new();
	private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private CancellationTokenSource? operation;
	private bool changedBook;

	#endregion

	#region Constructors

	public BookBackupPage(BookSession session, IWindowsPicker picker)
	{
		this.session = session;
		this.picker = picker;
		this.Title = "Backup and Restore";
		ScrollView body = new()
		{
			Content = new VerticalStackLayout
			{
				Padding = PagePadding, Spacing = ControlSpacing, MaximumWidthRequest = PageWidth,
				Children =
				{
					new Label { Text = "A book backup includes its songs, sheets, setlists and book settings. Keep a copy on another drive for recovery." },
					this.backup,
					new Label { Text = "Restore validates the backup and creates a separate book. Your current book remains available in Recent books." },
					this.restore,
					new Label { Text = "Recover this book from an earlier backup. A safety backup is saved automatically before replacement." },
					this.replace,
					new Label { Text = "Transfer display, metronome, keyboard/pedal and title settings independently of your songs using a settings backup." },
					this.backupSettings, this.restoreSettings, this.progress, this.cancel, this.status,
				},
			},
		};
		this.Content = DialogLayout.Create(this.Title, body, this.close);
		this.backup.Clicked += this.HandleBackup;
		this.restore.Clicked += this.HandleRestore;
		this.replace.Clicked += this.HandleReplace;
		this.backupSettings.Clicked += this.HandleBackupSettings;
		this.restoreSettings.Clicked += this.HandleRestoreSettings;
		this.cancel.Clicked += (_, _) => this.operation?.Cancel();
		this.close.Clicked += this.HandleClose;
	}

	#endregion

	#region Public Properties

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

	private async void HandleBackupSettings(object? sender, EventArgs e)
		=> await this.RunAsync(async token =>
		{
			string? path = await this.picker.SaveFileAsync("ChordBook-settings", ".mcbsettings", "ChordBook settings", token).ConfigureAwait(true);
			if (path is not null)
			{
				await this.session.SettingsTransfer.ExportFileAsync(path, token).ConfigureAwait(true);
				this.status.Text = $"Settings saved: {path}";
			}
		}).ConfigureAwait(true);

	private async void HandleRestoreSettings(object? sender, EventArgs e)
		=> await this.RunAsync(async token =>
		{
			string? path = await this.picker.PickFileAsync(".mcbsettings", token).ConfigureAwait(true);
			if (path is not null)
			{
				PortableBookSettings settings = await SettingsTransferService.ReadFileAsync(path, token).ConfigureAwait(true);
				Guid bookId = this.session.Database!.Id;
				long revision = this.session.Database.BookSettings.Revision.Revision;
				string preview = $"Restore settings for {this.session.Database.Name}?\n\n"
					+ $"Display: {settings.Display.Theme}, {settings.Display.FontSize} pt, {settings.Display.NotationSystem}\n"
					+ $"Metronome: {settings.Metronome.BeatsPerMinute} BPM, {settings.Metronome.BeatsPerMeasure}/{settings.Metronome.BeatUnit}\n"
					+ $"Keyboard/pedal bindings: {settings.InputBindings.Count}\n\n"
					+ "This replaces book defaults and bindings. Songs, setlists, song overrides and device preferences are kept.";
				if (await this.DisplayAlertAsync("Restore Settings", preview, "Restore", "Cancel").ConfigureAwait(true))
				{
					await Task.Run(() => this.session.SettingsTransfer.ApplyAsync(settings, bookId, revision, this.session.DeviceId, token), token)
						.ConfigureAwait(true);
					this.status.Text = "Book settings restored.";
				}
			}
		}).ConfigureAwait(true);

	private async void HandleBackup(object? sender, EventArgs e)
		=> await this.RunAsync(async token =>
		{
			string? path = await this.picker.SaveFileAsync($"ChordBook-{DateTime.UtcNow:yyyyMMdd-HHmmss}", ".mcbbak", "ChordBook backup", token)
				.ConfigureAwait(true);
			if (path is not null)
			{
				this.status.Text = "Validating and backing up the current book…";
				await this.session.CreateBackupAsync(path, token).ConfigureAwait(true);
				this.status.Text = $"Backup saved: {path}";
			}
			else
			{
				this.status.Text = "Backup canceled.";
			}
		}).ConfigureAwait(true);

	private async void HandleRestore(object? sender, EventArgs e)
		=> await this.RunAsync(async token =>
		{
			string? path = await this.picker.PickFileAsync(".mcbbak", token).ConfigureAwait(true);
			if (path is not null)
			{
				await this.RestoreAsNewAsync(path, token).ConfigureAwait(true);
			}
			else
			{
				this.status.Text = "Restore canceled.";
			}
		}).ConfigureAwait(true);

	private async Task RestoreAsNewAsync(string path, CancellationToken token)
	{
		string? name = await this.DisplayPromptAsync("Restore as New Book", "Name for the restored book:", initialValue: "Restored ChordBook")
			.ConfigureAwait(true);
		if (!string.IsNullOrWhiteSpace(name))
		{
			this.status.Text = "Validating the backup and restoring its sheets…";
			await this.session.RestoreBackupAsNewAsync(path, name, token).ConfigureAwait(true);
			this.changedBook = true;
			this.status.Text = $"Restored and opened {this.session.Database!.Name}. Your previous book is still available in Recent books.";
		}
		else
		{
			this.status.Text = "Restore canceled.";
		}
	}

	private async void HandleReplace(object? sender, EventArgs e)
		=> await this.RunAsync(async token =>
		{
			string? path = await this.picker.PickFileAsync(".mcbbak", token).ConfigureAwait(true);
			if (path is null)
			{
				this.status.Text = "Recovery canceled.";
			}
			else
			{
				this.status.Text = "Validating the selected backup…";
				using BookBackupReview review = await Task.Run(() => BookBackup.ReviewFileAsync(path, token), token).ConfigureAwait(true);
				if (review.BookId != this.session.Database!.Id)
				{
					if (await this.DisplayAlertAsync(
						"Different Book",
						"This backup belongs to a different book. Restore a separate copy?",
						"Restore as New Book",
						"Cancel").ConfigureAwait(true))
					{
						await this.RestoreAsNewAsync(path, token).ConfigureAwait(true);
					}
					else
					{
						this.status.Text = "Recovery canceled.";
					}
				}
				else
				{
					await this.ConfirmReplaceAsync(review, token).ConfigureAwait(true);
				}
			}
		}).ConfigureAwait(true);

	private async Task ConfirmReplaceAsync(BookBackupReview review, CancellationToken token)
	{
		ChordDatabase current = this.session.Database!;
		long revision = current.Revision.Revision;
		string preview = $"Replace {current.Name} with the backup of {review.Name}?\n\n"
			+ $"Backup: {review.SongCount} songs, {review.SheetCount} sheets, {review.SetlistCount} setlists.\n"
			+ $"Current: {current.Songs.Count} songs, {current.SongFiles.Count} sheets, {current.Setlists.Count} setlists.\n\n"
			+ "Songs, sheets, setlists and book settings will return to the backup's contents. "
			+ "An automatic safety backup is saved first. Device preferences and connections are kept.";
		if (await this.DisplayAlertAsync("Replace Current Book", preview, "Replace", "Cancel").ConfigureAwait(true))
		{
			this.status.Text = "Saving a safety backup and restoring the book…";
			string safetyBackup = await this.session.ReplaceFromBackupAsync(review, revision, token).ConfigureAwait(true);
			this.changedBook = true;
			this.status.Text = $"Book restored. Safety backup: {safetyBackup}";
		}
		else
		{
			this.status.Text = "Recovery canceled.";
		}
	}

	private async void HandleClose(object? sender, EventArgs e)
	{
		if (this.operation is null)
		{
			await this.Navigation.PopModalAsync().ConfigureAwait(true);
			this.completion.TrySetResult(this.changedBook);
		}
	}

	private async Task RunAsync(Func<CancellationToken, Task> action)
	{
		if (this.operation is null)
		{
			using CancellationTokenSource cancellation = new();
			this.operation = cancellation;
			this.backup.IsEnabled = this.restore.IsEnabled = this.replace.IsEnabled = this.close.IsEnabled = false;
			this.backupSettings.IsEnabled = this.restoreSettings.IsEnabled = false;
			this.progress.IsRunning = this.cancel.IsVisible = true;
			try
			{
				await action(cancellation.Token).ConfigureAwait(true);
			}
			catch (OperationCanceledException)
			{
				this.status.Text = "Operation canceled.";
			}
#pragma warning disable CA1031 // Present archive, picker and storage failures at the UI boundary.
			catch (Exception exception)
			{
				this.status.Text = exception.Message;
			}
#pragma warning restore CA1031
			finally
			{
				this.operation = null;
				this.progress.IsRunning = this.cancel.IsVisible = false;
				this.backup.IsEnabled = this.restore.IsEnabled = this.replace.IsEnabled = this.close.IsEnabled = true;
				this.backupSettings.IsEnabled = this.restoreSettings.IsEnabled = true;
			}
		}
	}

	#endregion
}
