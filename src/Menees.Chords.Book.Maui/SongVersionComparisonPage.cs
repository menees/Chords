#region Using Directives

using System.IO;
using System.Net;
using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui.Platforms.Windows;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Maui;

/// <summary>Read-only source/PDF comparison with explicit, confirmed version retention.</summary>
internal sealed partial class SongVersionComparisonPage : ContentPage
{
	#region Private Data

	private const int Gap = 8;
	private const int PaddingSize = 8;
	private const int ContentRow = 2;
	private const int StatusRow = 3;
	private readonly SongVersionComparison comparison;
	private readonly BookSession session;
	private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly FluentIconButton close = new() { Icon = "ArrowLeft", Description = "Back" };
	private readonly FluentIconButton cancel = new() { Icon = "Stop", Description = "Cancel Operation", IsVisible = false };
	private readonly Button keepLeft = new() { Text = "Keep Left" };
	private readonly Button keepRight = new() { Text = "Keep Right" };
	private readonly Button keepBoth = new() { Text = "Keep Both" };
	private readonly Button copyLeft = new() { Text = "Copy Left" };
	private readonly Button copyRight = new() { Text = "Copy Right" };
	private readonly Label status = new() { Text = "Select and copy text from either side. Close this comparison before editing a retained version." };
	private readonly List<WindowsDocumentViewer> documentViewers = [];
	private readonly List<(WebView View, SongVersionSide Side)> sideViews = [];
	private WebView? textView;
	private CancellationTokenSource? operation;
	private bool loaded;
	private bool changed;

	#endregion

	#region Constructors

	public SongVersionComparisonPage(SongVersionComparison comparison, BookSession session)
	{
		this.comparison = comparison;
		this.session = session;
		this.Title = "Compare Sheets";
		this.Content = this.CreateLayout();
		this.close.Clicked += this.HandleClose;
		this.cancel.Clicked += (_, _) => this.operation?.Cancel();
		this.keepLeft.Clicked += async (_, _) => await this.KeepAsync(SongVersionChoice.KeepLeft).ConfigureAwait(true);
		this.keepRight.Clicked += async (_, _) => await this.KeepAsync(SongVersionChoice.KeepRight).ConfigureAwait(true);
		this.keepBoth.Clicked += async (_, _) => await this.KeepAsync(SongVersionChoice.KeepBoth).ConfigureAwait(true);
		this.copyLeft.Clicked += async (_, _) =>
			await this.RunAsync(_ => Clipboard.Default.SetTextAsync(comparison.Left.Text ?? string.Empty)).ConfigureAwait(true);
		this.copyRight.Clicked += async (_, _) =>
			await this.RunAsync(_ => Clipboard.Default.SetTextAsync(comparison.Right.Text ?? string.Empty)).ConfigureAwait(true);
		this.Loaded += async (_, _) =>
		{
			if (!this.loaded)
			{
				this.loaded = true;
				await this.RunAsync(this.LoadViewsAsync).ConfigureAwait(true);
			}
		};
		this.Unloaded += (_, _) =>
		{
			this.operation?.Cancel();
			foreach (WindowsDocumentViewer viewer in this.documentViewers)
			{
				viewer.Dispose();
			}
		};
		this.UpdateControls();
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

	private static Label DescribeSide(string caption, SongVersionSide side)
	{
		string description = caption + ": " + side.Name + "\n"
			+ (side.IsArchived ? "Archived" : "Active") + $" · edited {side.ModifiedUtc.ToLocalTime():g}";
		if (side.RecoveredUtc is DateTimeOffset recovered)
		{
			description += $"\nPreserved by sync {recovered.ToLocalTime():g}";
		}

		if (side.SavedUtc is DateTimeOffset saved)
		{
			description += $"\nRetained before save {saved.ToLocalTime():g}";
		}

		return new() { Text = description, LineBreakMode = LineBreakMode.WordWrap };
	}

	private Grid CreateLayout()
	{
		Grid layout = new()
		{
			Padding = PaddingSize,
			RowSpacing = Gap,
			RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
		};
		layout.Add(ScreenLayout.Header(
			"Compare Sheets",
			this.close,
			this.keepLeft,
			this.keepRight,
			this.keepBoth,
			this.copyLeft,
			this.copyRight,
			this.cancel));
		Grid labels = new() { ColumnSpacing = Gap, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
		labels.Add(DescribeSide("Left", this.comparison.Left));
		labels.Add(DescribeSide("Right", this.comparison.Right), 1, 0);
		layout.Add(labels, 0, 1);
		if (this.comparison.Left.Text is not null && this.comparison.Right.Text is not null)
		{
			this.textView = new();
			layout.Add(this.textView, 0, ContentRow);
		}
		else
		{
			Grid previews = new() { ColumnSpacing = Gap, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
			foreach (SongVersionSide side in new[] { this.comparison.Left, this.comparison.Right })
			{
				WebView view = new();
				this.sideViews.Add((view, side));
				previews.Add(view, this.sideViews.Count - 1, 0);
			}

			layout.Add(previews, 0, ContentRow);
		}

		layout.Add(this.status, 0, StatusRow);
		return layout;
	}

	private async Task LoadViewsAsync(CancellationToken token)
	{
		if (this.textView is not null)
		{
			string html = await Task.Run(() => SongVersionComparisonHtml.Create(this.comparison, token), token).ConfigureAwait(true);
			WindowsDocumentViewer viewer = new(this.textView);
			this.documentViewers.Add(viewer);
			await viewer.LoadReadOnlyHtmlAsync(html, token).ConfigureAwait(true);
		}
		else
		{
			foreach ((WebView view, SongVersionSide side) in this.sideViews)
			{
				token.ThrowIfCancellationRequested();
				if (side.MediaKind == MediaKind.Pdf)
				{
					string path = Path.Combine(this.session.DirectoryPath!, side.Name);
					WindowsDocumentViewer viewer = new(view);
					this.documentViewers.Add(viewer);
					await viewer.LoadAsync(new(null, () => File.OpenRead(path)), generation: 1, startAtEnd: false, token).ConfigureAwait(true);
					viewer.ApplyTheme();
				}
				else
				{
					string html = "<!doctype html><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" "
						+ "content=\"default-src 'none'; style-src 'unsafe-inline'\"><style>body{color-scheme:light dark;"
						+ "background:Canvas;color:CanvasText}pre{white-space:pre;overflow:auto}</style><pre>"
						+ WebUtility.HtmlEncode(side.Text) + "</pre>";
					WindowsDocumentViewer viewer = new(view);
					this.documentViewers.Add(viewer);
					await viewer.LoadReadOnlyHtmlAsync(html, token).ConfigureAwait(true);
				}
			}
		}
	}

	private Task KeepAsync(SongVersionChoice choice)
		=> this.RunAsync(async token =>
		{
			string message = choice == SongVersionChoice.KeepBoth
				? "Restore both sheets as active versions? Their source bytes will be retained."
				: $"Keep {(choice == SongVersionChoice.KeepLeft ? this.comparison.Left.Name : this.comparison.Right.Name)} "
					+ "as the default active sheet and permanently delete the other? Preferences for the deleted sheet will use the retained sheet.";
			if (await this.DisplayAlertAsync("Keep compared versions?", message, "Keep", "Cancel").ConfigureAwait(true))
			{
				await this.session.ResolveSongVersionsAsync(this.comparison, choice, token).ConfigureAwait(true);
				this.changed = true;
				this.status.Text = "Versions retained as selected. Close this comparison to continue editing or sync the resolution.";
			}
		});

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
				this.status.Text = "Canceled. Both versions were retained.";
			}
#pragma warning disable CA1031 // Keep comparison/persistence failures visible at the UI boundary.
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
		this.keepLeft.IsEnabled = this.keepRight.IsEnabled = this.keepBoth.IsEnabled = idle && !this.changed;
		this.copyLeft.IsEnabled = idle && this.comparison.Left.Text is not null;
		this.copyRight.IsEnabled = idle && this.comparison.Right.Text is not null;
		this.close.IsEnabled = idle;
		this.cancel.IsVisible = !idle;
	}

	private async void HandleClose(object? sender, EventArgs e)
	{
		if (this.operation is null)
		{
			await this.Navigation.PopAsync().ConfigureAwait(true);
			this.completion.TrySetResult(this.changed);
		}
	}

	#endregion
}
