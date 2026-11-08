#region Using Directives

using System.Diagnostics;
using System.IO;
using Menees.Chords.Book.Maui.Platforms.Windows;
using Menees.Chords.Db;
using Menees.Chords.Sync;

#endregion

namespace Menees.Chords.Book.Maui;

/// <summary>Manual synchronization with automatic sign-in and optional cloud-book/recovery actions.</summary>
public sealed partial class CloudSyncPage : ContentPage
{
	#region Private Data

	private const int PaddingSize = 8;
	private const int ControlSpacing = 8;
	private const int HeadingSize = 18;
	private const int StatusRow = 3;
	private const int ReportRow = 4;
	private const int ProgressHeight = 10;
	private const int IndicatorSize = 20;
	private readonly BookSession session;
	private readonly ICloudSyncConnection connection;
	private readonly ReplicaStateStore states;
	private readonly Label account = new();
	private readonly Label status = new();
	private readonly ProgressBar transferBar = new() { IsVisible = false, HeightRequest = ProgressHeight };
	private readonly ActivityIndicator working = new() { IsVisible = false, WidthRequest = IndicatorSize, HeightRequest = IndicatorSize };
	private readonly Picker direction = new() { Title = "Sync direction" };
	private readonly OutputEditor report = new();
	private readonly FluentIconButton connect = new() { Icon = "PlugConnected", Description = "Connect" };
	private readonly FluentIconButton disconnect = new() { Icon = "PlugDisconnected", Description = "Disconnect" };
	private readonly FluentIconButton download = new() { Icon = "CloudArrowDown", Description = "Download Book…" };
	private readonly FluentIconButton apply = new() { Icon = "ArrowSync", Description = "Sync Now", IsEnabled = false };
	private readonly FluentIconButton cancel = new() { Icon = "Stop", Description = "Cancel Operation", IsVisible = false };
	private readonly FluentIconButton close = new() { Icon = "ArrowLeft", Description = "Back" };
	private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private CancellationTokenSource? operation;
	private bool loaded;
	private bool downloading;

	#endregion

	#region Constructors

	public CloudSyncPage(BookSession session, ICloudSyncConnection connection)
	{
		this.session = session;
		this.connection = connection;
		this.states = new(Path.Combine(connection.StateDirectory, "Replicas"));
		this.Title = connection.ProviderName;
#if WINDOWS
		WindowsProgressBar.SetTrackHeight(this.transferBar, ProgressHeight);
#endif
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
			RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) },
		};
		layout.Add(ScreenLayout.Header(this.Title, this.close, this.apply, this.cancel));
		VerticalStackLayout setup = new()
		{
			Spacing = ControlSpacing,
			Children =
			{
				new Label { Text = this.connection.AccountLabel, FontSize = HeadingSize, FontAttributes = FontAttributes.Bold },
				this.account,
				new Label { Text = "Choose a direction and press Sync Now. Sign-in opens if needed. Use the same account on each device." },
				new HorizontalStackLayout { Spacing = ControlSpacing, Children = { this.connect, this.disconnect, this.download } },
			},
		};
		layout.Add(setup, 0, 1);
		layout.Add(this.direction, 0, 2);
		layout.Add(this.report, 0, ReportRow);
		Grid phase = new()
		{
			ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) },
			ColumnSpacing = ControlSpacing,
		};
		phase.Add(this.working);
		phase.Add(this.status, 1);
		VerticalStackLayout progressStatus = new()
		{
			Spacing = ControlSpacing,
			Children =
			{
				phase,
				this.transferBar,
			},
		};
		layout.Add(progressStatus, 0, StatusRow);
		return layout;
	}

	private async void HandleConnect(object? sender, EventArgs e)
		=> await this.RunAsync(async token =>
		{
			this.status.Text = $"Opening {this.connection.ProviderName} sign-in…";
			await Task.Run(() => this.connection.ConnectAsync(token), token).ConfigureAwait(true);
			this.status.Text = "Connected. Choose Sync Now, or download an existing cloud book.";
		}).ConfigureAwait(true);

	private async void HandleDisconnect(object? sender, EventArgs e)
		=> await this.RunAsync(async token =>
		{
			await this.connection.DisconnectAsync(token).ConfigureAwait(true);
			this.report.Text = string.Empty;
			this.status.Text = "Disconnected. Local and cloud books and pending transfers were retained.";
		}).ConfigureAwait(true);

	private async Task<CloudBookLease> AcquireCloudAsync(ICloudReplica transport, CancellationToken token)
	{
		CloudBookLease cloud;
		try
		{
			cloud = await CloudBookLease.AcquireAsync(transport, this.session.DeviceId, Environment.MachineName, cancellationToken: token)
				.ConfigureAwait(true);
		}
		catch (CloudBookLockedException exception) when (exception.CanTakeOver)
		{
			if (!await this.DisplayAlertAsync(
				"Recover interrupted cloud sync?",
				exception.Message + $" Take over and reconcile the uploads that reached {this.connection.ProviderName}?",
				"Take Over",
				"Cancel").ConfigureAwait(true))
			{
				throw;
			}

			cloud = await CloudBookLease.AcquireAsync(transport, this.session.DeviceId, Environment.MachineName, true, token).ConfigureAwait(true);
		}

		return cloud;
	}

	private async void HandleApply(object? sender, EventArgs e)
		=> await this.RunAsync(async token =>
		{
			this.status.Text = $"Checking {this.connection.ProviderName} sign-in…";
			await Task.Run(() => this.connection.EnsureConnectedAsync(token), token).ConfigureAwait(true);
			this.account.Text = this.connection.AccountName;
			if (EmptyBookAdoption.IsEligible(this.session.Database!))
			{
				await this.DownloadBookAsync(token).ConfigureAwait(true);
			}
			else
			{
				await this.SyncCurrentBookAsync(token).ConfigureAwait(true);
			}
		}).ConfigureAwait(true);

	private async Task SyncCurrentBookAsync(CancellationToken token)
	{
		Stopwatch timer = Stopwatch.StartNew();
		BookSyncPreview confirmed;
		(FileSystemBookStore store, BookLocation location) = this.session.GetSyncBook();
		BookSyncResult result;
		Progress<BookSyncProgress> progress = this.CreateTransferProgress();
		try
		{
			Guid bookId = this.session.Database!.Id;
			SyncDirection selectedDirection = (SyncDirection)this.direction.SelectedIndex;
			ICloudReplica transport = await Task.Run(() => this.connection.OpenBookAsync(bookId, token), token).ConfigureAwait(true);
			using IDisposable? transportLifetime = transport as IDisposable;
			using ReplicaStateLease lease = this.states.Acquire(new(bookId, this.session.DeviceId, transport.Identity));
			await using CloudBookLease cloud = await this.AcquireCloudAsync(transport, token).ConfigureAwait(true);
			this.status.Text = $"Comparing this device and {this.connection.ProviderName}…";
			confirmed = await Task.Run(
				async () =>
				{
					Guid epoch = await store.ReadRecoveryEpochAsync(location, token).ConfigureAwait(false);
					return await BookSyncCoordinator.PrepareWithProgressAsync(store, location, cloud, lease, selectedDirection, epoch, progress, token)
						.ConfigureAwait(false);
				},
				token).ConfigureAwait(true);
			this.report.Text = this.CreateReviewText(confirmed);
			Guid epoch = await store.ReadRecoveryEpochAsync(location, token).ConfigureAwait(true);
			if (epoch != confirmed.RecoveryEpoch)
			{
				throw new InvalidOperationException("This book was restored during comparison. Use Sync Now again.");
			}

			result = await Task.Run(
				() => BookSyncCoordinator.ExecuteWithProgressAsync(
					store, location, cloud, lease, confirmed, progress, this.connection.MaximumConcurrentTransfers, token),
				token).ConfigureAwait(true);
			cloud.Complete();
		}
		finally
		{
			await this.session.ReloadAfterSyncAsync(CancellationToken.None).ConfigureAwait(true);
		}

		timer.Stop();
		string elapsed = FormatElapsed(timer.Elapsed);
		this.report.Text = this.CreateReviewText(confirmed, completed: true) + $"\n\nCompleted {result.CompletedUtc.ToLocalTime():g}. Elapsed {elapsed}. "
			+ $"Uploaded {result.UploadedSheets} sheets and {confirmed.Changes.UploadedSetlists.Count} setlists; "
			+ $"downloaded {result.DownloadedSheets} sheets and {confirmed.Changes.DownloadedSetlists.Count} setlists; "
			+ $"retained {result.RecoveredSheets} recovery sheets.";
		this.status.Text = confirmed.HasChanges ? $"Sync completed in {elapsed}." : $"Already up to date. Elapsed {elapsed}.";
	}

	private async void HandleDownload(object? sender, EventArgs e)
		=> await this.RunAsync(this.DownloadBookAsync).ConfigureAwait(true);

	private async Task DownloadBookAsync(CancellationToken token)
	{
		this.report.Text = string.Empty;
		this.status.Text = "Finding cloud books…";
		await Task.Run(() => this.connection.EnsureConnectedAsync(token), token).ConfigureAwait(true);
		bool replaceEmpty = EmptyBookAdoption.IsEligible(this.session.Database!);
		IReadOnlyList<CloudBookChoice> available = await Task.Run(() => this.connection.ListBooksAsync(token), token).ConfigureAwait(true);
		CloudBookChoice[] books = [.. available.Where(book => !replaceEmpty || book.SongCount != 0)
			.OrderByDescending(book => book.SongCount)];
		if (books.Length == 0)
		{
			this.status.Text = replaceEmpty
				? "No cloud songs found. Import songs into this book first, or upload a book from the other PC. No empty book was uploaded."
				: "No uploaded books found. On the other PC, use Sync Now first.";
		}
		else
		{
			string[] choices = [.. books.Select((book, index) => $"{index + 1}. {book}")];
			string? chosen = await this.DisplayActionSheetAsync(
				replaceEmpty ? "Use a cloud book in this empty book" : "Download a cloud book", "Cancel", null, choices).ConfigureAwait(true);
			int index = Array.IndexOf(choices, chosen);
			if (index >= 0)
			{
				CloudBookChoice selected = books[index];
				if (selected.Id == this.session.Database!.Id)
				{
					if (replaceEmpty)
					{
						this.direction.SelectedIndex = (int)SyncDirection.UpdateThisDevice;
					}

					await this.SyncCurrentBookAsync(token).ConfigureAwait(true);
				}
				else
				{
					string confirmation = replaceEmpty
						? $"Download {selected.Name} ({(selected.SongCount < 0 ? "locked book" : $"{selected.SongCount} songs")}) into the current empty book? "
							+ "Its settings will replace the empty book's settings."
						: $"Download {selected.Name} ({(selected.SongCount < 0 ? "locked book" : $"{selected.SongCount} songs")}) and open a new local copy?";
					if (await this.DisplayAlertAsync("Download book?", confirmation, "Download", "Cancel").ConfigureAwait(true))
					{
						this.downloading = true;
						Stopwatch timer = Stopwatch.StartNew();
						ICloudReplica transport = this.connection.OpenFolder(selected.FolderId);
						using IDisposable? transportLifetime = transport as IDisposable;
						await using CloudBookLease cloud = await this.AcquireCloudAsync(transport, token).ConfigureAwait(true);
						await this.session.DownloadCloudBookAsync(
							cloud,
							selected.Id,
							this.states,
							Path.Combine(this.connection.StateDirectory, "Temporary"),
							this.CreateTransferProgress(),
							replaceEmpty,
							this.connection.MaximumConcurrentTransfers,
							token).ConfigureAwait(true);
						cloud.Complete();
						timer.Stop();
						this.status.Text = $"Downloaded and opened {selected.Name} in {FormatElapsed(timer.Elapsed)}.";
						BookSyncChanges changes = BookSyncChanges.FromDownloadedBook(this.session.Database!);
						int songCount = this.session.Database!.Songs.Count;
						this.report.Text = $"Book: {selected.Name}\nDownloaded {songCount} songs. Elapsed {FormatElapsed(timer.Elapsed)}.\n"
							+ BookSyncChangeReport.Format(changes, completed: true);
					}
				}
			}
		}
	}

	private string CreateReviewText(BookSyncPreview review, bool completed = false)
	{
		string result = $"Book: {this.session.Database!.Name}\nAccount: {this.connection.AccountName}\nDirection: {this.direction.Items[(int)review.Direction]}"
			+ $"\nUpload {review.UploadCount} sheets; download {review.DownloadCount} sheets; transfer {review.EstimatedBytes:N0} bytes."
			+ $"\nRetain {review.RecoveryCount} archived recovery sheets. Report {review.Conflicts.Count} conflicts."
			+ $"\nRemove songs: {review.LocalRemovedSongCount} on this device, {review.CloudRemovedSongCount} from {this.connection.ProviderName}."
			+ $"\nRemove sheets: {review.LocalRemovedSheetCount} on this device, {review.CloudRemovedSheetCount} from the {this.connection.ProviderName} book.";
		if (review.IsInitialPublication)
		{
			result += completed ? "\nPublished the first cloud copy of this book." : "\nThis sync will publish the first cloud copy of this book.";
		}

		foreach (string description in review.ConflictDescriptions)
		{
			result += "\n" + description;
		}

		return result + "\n" + BookSyncChangeReport.Format(review.Changes, completed);
	}

	private async Task RunAsync(Func<CancellationToken, Task> action)
	{
		if (this.operation is null)
		{
			using CancellationTokenSource cancellation = new();
			this.operation = cancellation;
			this.downloading = false;
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
				this.status.Text = this.downloading ? "Download canceled. Your current book was retained; choose Download Book to try again."
					: "Canceled. Transfer progress was retained; use Sync Now to resume.";
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
				this.transferBar.IsVisible = false;
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

	private void UpdateControls()
	{
		bool idle = this.operation is null;
		bool connected = this.connection.IsConnected;
		this.account.Text = this.connection.AccountName;
		this.connect.Description = (connected ? "Reconnect " : "Connect ") + this.connection.ProviderName;
		this.connect.IsEnabled = this.close.IsEnabled = this.direction.IsEnabled = idle;
		this.disconnect.IsEnabled = this.download.IsEnabled = idle && connected;
		this.apply.IsEnabled = idle;
		this.cancel.IsVisible = !idle;
	}

	private async void HandleClose(object? sender, EventArgs e)
	{
		if (this.operation is null)
		{
			this.connection.Dispose();
			await this.Navigation.PopAsync().ConfigureAwait(true);
			this.completion.TrySetResult();
		}
	}

	#endregion
}
