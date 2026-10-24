#region Using Directives

using System.Diagnostics;
using System.IO;
using Menees.Chords.Book.Maui.Platforms.Windows;
using Menees.Chords.Db;
using Menees.Chords.Sync;
using Menees.Chords.Sync.OneDrive;

#endregion

namespace Menees.Chords.Book.Maui;

/// <summary>Manual synchronization with automatic sign-in and optional cloud-book/recovery actions.</summary>
internal sealed partial class OneDriveSyncPage : ContentPage
{
	#region Private Data

	private const int WindowsTransferConcurrency = 4;

	private const int PaddingSize = 16;
	private const int ControlSpacing = 8;
	private const int HeadingSize = 18;
	private const int ReportRow = 3;
	private const int StatusRow = 4;
	private readonly BookSession session;
	private readonly WindowsOneDriveConnection connection = new();
	private readonly ReplicaStateStore states = new(Path.Combine(WindowsOneDriveConnection.LocalDirectory, "Replicas"));
	private readonly Label account = new();
	private readonly Label status = new();
	private readonly ProgressBar transferBar = new() { IsVisible = false };
	private readonly ActivityIndicator working = new() { IsVisible = false };
	private readonly Picker direction = new() { Title = "Sync direction" };
	private readonly Editor report = new() { IsReadOnly = true, AutoSize = EditorAutoSizeOption.Disabled };
	private readonly FluentIconButton connect = new() { Icon = "PlugConnected", Description = "Connect OneDrive" };
	private readonly FluentIconButton disconnect = new() { Icon = "PlugDisconnected", Description = "Disconnect OneDrive" };
	private readonly FluentIconButton download = new() { Icon = "CloudArrowDown", Description = "Download Book…" };
	private readonly FluentIconButton apply = new() { Icon = "ArrowSync", Description = "Sync Now", IsEnabled = false };
	private readonly FluentIconButton cancel = new() { Icon = "Stop", Description = "Cancel Operation", IsVisible = false };
	private readonly FluentIconButton close = new() { Icon = "Dismiss", Description = "Close" };
	private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private OneDriveHttpTransport? replica;
	private ReplicaStateLease? lease;
	private BookSyncPreview? preview;
	private CancellationTokenSource? operation;
	private bool loaded;

	#endregion

	#region Constructors

	public OneDriveSyncPage(BookSession session)
	{
		this.session = session;
		this.Title = "OneDrive";
		this.direction.ItemsSource = new[] { "Two-Way", "Update This Device", "Update Cloud" };
		this.direction.SelectedIndex = 0;
		this.Content = this.CreateLayout();
		this.connect.Clicked += this.HandleConnect;
		this.disconnect.Clicked += this.HandleDisconnect;
		this.download.Clicked += this.HandleDownload;
		this.apply.Clicked += this.HandleApply;
		this.cancel.Clicked += (_, _) => this.operation?.Cancel();
		this.close.Clicked += this.HandleClose;
		this.direction.SelectedIndexChanged += (_, _) =>
		{
			if (this.operation is null)
			{
				this.ReleaseReview();
				this.report.Text = string.Empty;
				this.UpdateControls();
			}
		};
		this.Loaded += async (_, _) =>
		{
			if (!this.loaded)
			{
				this.loaded = true;
				await this.RunAsync(async token =>
				{
					await Task.Run(() => this.connection.LoadAsync(token), token).ConfigureAwait(true);
				}).ConfigureAwait(true);
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

	private static string FormatElapsed(TimeSpan elapsed)
		=> FormattableString.Invariant($"{(long)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}");

	private Grid CreateLayout()
	{
		Grid layout = new()
		{
			Padding = PaddingSize,
			RowSpacing = ControlSpacing,
			RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
		};
		layout.Add(DialogLayout.Header("OneDrive", this.close, this.apply, this.cancel));
		VerticalStackLayout setup = new()
		{
			Spacing = ControlSpacing,
			Children =
			{
				new Label { Text = "Microsoft account", FontSize = HeadingSize, FontAttributes = FontAttributes.Bold },
				this.account,
				new Label { Text = "Choose a direction and press Sync Now. Sign-in opens if needed. Use the same Microsoft account on each device." },
				new HorizontalStackLayout { Spacing = ControlSpacing, Children = { this.connect, this.disconnect, this.download } },
			},
		};
		layout.Add(setup, 0, 1);
		layout.Add(this.direction, 0, 2);
		layout.Add(this.report, 0, ReportRow);
		VerticalStackLayout progressStatus = new()
		{
			Spacing = ControlSpacing,
			Children = { this.transferBar, this.working, this.status },
		};
		layout.Add(progressStatus, 0, StatusRow);
		return layout;
	}

	private async void HandleConnect(object? sender, EventArgs e)
		=> await this.RunAsync(async token =>
		{
			this.ReleaseReview();
			this.status.Text = "Opening Microsoft sign-in…";
			await Task.Run(() => this.connection.ConnectAsync(token), token).ConfigureAwait(true);
			this.status.Text = "Connected. Choose Sync Now, or download an existing cloud book.";
		}).ConfigureAwait(true);

	private async void HandleDisconnect(object? sender, EventArgs e)
		=> await this.RunAsync(async token =>
		{
			this.ReleaseReview();
			await this.connection.DisconnectAsync(token).ConfigureAwait(true);
			this.report.Text = string.Empty;
			this.status.Text = "Disconnected. Local and cloud books and pending transfers were retained.";
		}).ConfigureAwait(true);

	private async Task ReviewAsync(CancellationToken token)
	{
		this.ReleaseReview();
		(FileSystemBookStore store, BookLocation location) = this.session.GetSyncBook();
		Guid bookId = this.session.Database!.Id;
		SyncDirection selectedDirection = (SyncDirection)this.direction.SelectedIndex;
		this.status.Text = "Comparing this device and OneDrive…";
		this.replica = await Task.Run(() => this.connection.OpenBookAsync(bookId, token), token).ConfigureAwait(true);
		this.lease = this.states.Acquire(new(bookId, this.session.DeviceId, this.replica.Identity));

		this.preview = await Task.Run(
			async () =>
		{
			Guid epoch = await store.ReadRecoveryEpochAsync(location, token).ConfigureAwait(false);
			return await BookSyncCoordinator.PrepareAsync(
				store, location, this.replica, this.lease, selectedDirection, epoch, token).ConfigureAwait(false);
		},
			token).ConfigureAwait(true);
		this.direction.SelectedIndex = (int)this.preview.Direction;
		this.report.Text = this.CreateReviewText(this.preview);
		this.status.Text = !this.preview.HasChanges ? "Already up to date." : this.preview.IsResuming
			? "An interrupted confirmed sync is ready to resume." : "Comparison ready.";
	}

	private async void HandleApply(object? sender, EventArgs e)
		=> await this.RunAsync(async token =>
		{
			this.status.Text = "Checking OneDrive sign-in…";
			await Task.Run(() => this.connection.EnsureConnectedAsync(token), token).ConfigureAwait(true);
			this.account.Text = this.connection.AccountName;
			Stopwatch timer = Stopwatch.StartNew();
			BookSyncPreview confirmed;
			(FileSystemBookStore store, BookLocation location) = this.session.GetSyncBook();
			BookSyncResult result;
			Progress<BookSyncProgress> progress = this.CreateTransferProgress();
			try
			{
				await this.ReviewAsync(token).ConfigureAwait(true);
				confirmed = this.preview!;
				Guid epoch = await store.ReadRecoveryEpochAsync(location, token).ConfigureAwait(true);
				if (epoch != confirmed.RecoveryEpoch)
				{
					throw new InvalidOperationException("This book was restored during comparison. Use Sync Now again.");
				}

				result = await Task.Run(
					() => BookSyncCoordinator.ExecuteWithProgressAsync(
						store, location, this.replica!, this.lease!, confirmed, progress, WindowsTransferConcurrency, token),
					token).ConfigureAwait(true);
				this.ReleaseReview();
			}
			finally
			{
				await this.session.ReloadAfterSyncAsync(CancellationToken.None).ConfigureAwait(true);
			}

			this.transferBar.Progress = 1;
			this.transferBar.IsVisible = true;
			timer.Stop();
			string elapsed = FormatElapsed(timer.Elapsed);
			this.report.Text += $"\n\nCompleted {result.CompletedUtc.ToLocalTime():g}. Elapsed {elapsed}. "
				+ $"Uploaded {result.UploadedSheets}, downloaded {result.DownloadedSheets}, retained {result.RecoveredSheets} recovery sheets.";
			this.status.Text = confirmed.HasChanges ? $"Sync completed in {elapsed}." : $"Already up to date. Elapsed {elapsed}.";
		}).ConfigureAwait(true);

	private async void HandleDownload(object? sender, EventArgs e)
		=> await this.RunAsync(async token =>
		{
			this.ReleaseReview();
			this.status.Text = "Finding cloud books…";
			IReadOnlyList<OneDriveBookChoice> books = await Task.Run(() => this.connection.ListBooksAsync(token), token).ConfigureAwait(true);
			if (books.Count == 0)
			{
				this.status.Text = "No uploaded books found. On the other PC, use Sync Now first.";
			}
			else
			{
				string[] choices = [.. books.Select((book, index) => $"{index + 1}. {book}")];
				string? chosen = await this.DisplayActionSheetAsync("Download a cloud book", "Cancel", null, choices).ConfigureAwait(true);
				int index = Array.IndexOf(choices, chosen);
				if (index >= 0)
				{
					OneDriveBookChoice selected = books[index];
					if (selected.Id == this.session.Database!.Id)
					{
						throw new InvalidOperationException("This book is already open. Use Sync Now to update it.");
					}

					string confirmation = $"Download {selected.Name} ({selected.SongCount} songs) and open its local copy?";
					if (await this.DisplayAlertAsync("Download book?", confirmation, "Download", "Cancel").ConfigureAwait(true))
					{
						using OneDriveHttpTransport transport = this.connection.OpenFolder(selected.FolderId);
						Progress<string> progress = this.CreateProgress();
						await this.session.DownloadCloudBookAsync(
							transport,
							selected.Id,
							this.states,
							Path.Combine(WindowsOneDriveConnection.LocalDirectory, "Temporary"),
							progress,
							token).ConfigureAwait(true);
						this.status.Text = $"Downloaded and opened {selected.Name}.";
						this.report.Text = string.Empty;
					}
				}
			}
		}).ConfigureAwait(true);

	private string CreateReviewText(BookSyncPreview review)
	{
		string result = $"Book: {this.session.Database!.Name}\nAccount: {this.connection.AccountName}\nDirection: {this.direction.Items[(int)review.Direction]}"
			+ $"\nUpload {review.UploadCount} sheets; download {review.DownloadCount} sheets; transfer {review.EstimatedBytes:N0} bytes."
			+ $"\nRetain {review.RecoveryCount} archived recovery sheets. Report {review.Conflicts.Count} conflicts."
			+ $"\nRemove songs: {review.LocalRemovedSongCount} on this device, {review.CloudRemovedSongCount} from OneDrive."
			+ $"\nRemove sheets: {review.LocalRemovedSheetCount} on this device, {review.CloudRemovedSheetCount} from the OneDrive book.";
		if (review.IsInitialPublication)
		{
			result += "\nThis sync will publish the first cloud copy of this book.";
		}

		foreach (string description in review.ConflictDescriptions)
		{
			result += "\n" + description;
		}

		return result;
	}

	private async Task RunAsync(Func<CancellationToken, Task> action)
	{
		if (this.operation is null)
		{
			using CancellationTokenSource cancellation = new();
			this.operation = cancellation;
			this.transferBar.Progress = 0;
			this.transferBar.IsVisible = false;
			this.working.IsVisible = this.working.IsRunning = true;
			this.UpdateControls();
			try
			{
				await action(cancellation.Token).ConfigureAwait(true);
			}
			catch (OperationCanceledException)
			{
				this.status.Text = "Canceled. Transfer progress was retained; use Sync Now to resume.";
			}
#pragma warning disable CA1031 // Report authentication, provider and persistence failures at the UI boundary.
			catch (Exception exception)
			{
				this.status.Text = exception.Message;
			}
#pragma warning restore CA1031
			finally
			{
				this.operation = null;
				this.working.IsVisible = this.working.IsRunning = false;
				this.UpdateControls();
			}
		}
	}

	private Progress<BookSyncProgress> CreateTransferProgress()
	{
		CancellationTokenSource? owner = this.operation;
		return new Progress<BookSyncProgress>(update =>
		{
			if (ReferenceEquals(owner, this.operation) && owner?.IsCancellationRequested == false)
			{
				this.status.Text = update.Message;
				bool counted = update.TotalSheets is > 0 && update.CompletedSheets.HasValue;
				this.transferBar.IsVisible = counted;
				SemanticProperties.SetDescription(this.transferBar, update.Message);
				this.working.IsVisible = this.working.IsRunning = !counted;
				if (counted)
				{
					this.transferBar.Progress = (double)update.CompletedSheets!.Value / update.TotalSheets!.Value;
				}
			}
		});
	}

	private Progress<string> CreateProgress()
	{
		CancellationTokenSource? owner = this.operation;
		return new Progress<string>(message =>
		{
			if (ReferenceEquals(owner, this.operation) && owner?.IsCancellationRequested == false)
			{
				this.status.Text = message;
			}
		});
	}

	private void UpdateControls()
	{
		bool idle = this.operation is null;
		bool connected = this.connection.AccountId is not null;
		this.account.Text = this.connection.AccountName;
		this.connect.Description = connected ? "Reconnect OneDrive" : "Connect OneDrive";
		this.connect.IsEnabled = this.close.IsEnabled = this.direction.IsEnabled = idle;
		this.disconnect.IsEnabled = this.download.IsEnabled = idle && connected;
		this.apply.IsEnabled = idle;
		this.cancel.IsVisible = !idle;
	}

	private void ReleaseReview()
	{
		this.preview = null;
		this.lease?.Dispose();
		this.lease = null;
		this.replica?.Dispose();
		this.replica = null;
	}

	private async void HandleClose(object? sender, EventArgs e)
	{
		if (this.operation is null)
		{
			this.ReleaseReview();
			this.connection.Dispose();
			await this.Navigation.PopModalAsync().ConfigureAwait(true);
			this.completion.TrySetResult();
		}
	}

	#endregion
}
