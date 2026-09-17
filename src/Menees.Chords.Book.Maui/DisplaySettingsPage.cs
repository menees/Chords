#region Using Directives

using System.Globalization;
using Menees.Chords.Book.Application;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Maui;

public sealed partial class DisplaySettingsPage : ContentPage
{
	#region Private Data

	private const int PagePadding = 24;
	private const int PreviewHeight = 240;
	private const int ControlSpacing = 12;
	private const int HeadingSize = 24;

	private readonly BookSession session;
	private readonly Guid? songId;
	private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Picker theme = new() { Title = "Chart theme", ItemsSource = new[] { "Default", "Light", "Dark" } };
	private readonly Microsoft.Maui.Controls.Entry size = new() { Keyboard = Keyboard.Numeric };
	private readonly Microsoft.Maui.Controls.Entry spacing = new() { Keyboard = Keyboard.Numeric };
	private readonly Picker columns = new() { Title = "Columns", ItemsSource = new[] { "Automatic (fill width)", "1", "2", "3", "4" } };
	private readonly CheckBox chords = new();
	private readonly Picker notation = new() { Title = "Chord notation", ItemsSource = new[] { "Letter", "Nashville", "Roman" } };
	private readonly Label status = new();
	private readonly WebView preview = new() { HeightRequest = PreviewHeight };
	private readonly IDispatcherTimer previewTimer;
	private bool busy;

	#endregion

	#region Public API

	public DisplaySettingsPage(BookSession session, Guid? songId)
	{
		this.session = session;
		this.songId = songId;
		DisplayProfile profile = session.GetDisplaySettings(songId);
		this.theme.SelectedItem = profile.Theme;
		this.size.Text = profile.FontSize.ToString(CultureInfo.CurrentCulture);
		this.spacing.Text = profile.LineSpacing.ToString(CultureInfo.CurrentCulture);
		this.columns.SelectedIndex = profile.AutoColumns ? 0 : profile.Columns;
		this.chords.IsChecked = profile.ShowChords;
		this.notation.SelectedItem = profile.NotationSystem;
		Button save = new() { Text = "Save" };
		save.Clicked += async (_, _) => await this.SaveAsync(false).ConfigureAwait(true);
		Button reset = new() { Text = "Reset to Book Defaults", IsVisible = songId.HasValue };
		reset.Clicked += async (_, _) => await this.SaveAsync(true).ConfigureAwait(true);
		Button cancel = new() { Text = "Cancel" };
		cancel.Clicked += async (_, _) => await this.CloseAsync(false).ConfigureAwait(true);
		this.Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Padding = PagePadding,
				Spacing = ControlSpacing,
				Children =
				{
					new Label { Text = songId.HasValue ? "Song Display" : "Book Display Defaults", FontSize = HeadingSize },
					new Label { Text = "Text sheets only. PDF appearance is unchanged. Default theme follows the device theme." },
					this.theme, new Label { Text = "Text size (8–72 px)" }, this.size,
					new Label { Text = "Line spacing (0.5–3)" }, this.spacing, this.columns,
					new HorizontalStackLayout
					{
						Children = { this.chords, new Label { Text = "Show chord symbols and diagrams", VerticalOptions = LayoutOptions.Center } },
					},
					this.notation, new Label { Text = "Live preview" }, this.preview, this.status, save, reset, cancel,
				},
			},
		};
		this.previewTimer = this.Dispatcher.CreateTimer();
		this.previewTimer.Interval = TimeSpan.FromMilliseconds(200);
		this.previewTimer.Tick += (_, _) =>
		{
			this.previewTimer.Stop();
			this.RefreshPreview();
		};
		this.size.TextChanged += this.HandlePreviewChanged;
		this.spacing.TextChanged += this.HandlePreviewChanged;
		this.theme.SelectedIndexChanged += this.HandlePreviewChanged;
		this.columns.SelectedIndexChanged += this.HandlePreviewChanged;
		this.notation.SelectedIndexChanged += this.HandlePreviewChanged;
		this.chords.CheckedChanged += this.HandlePreviewChanged;
		this.Loaded += (_, _) => this.RefreshPreview();
		this.Unloaded += (_, _) => this.previewTimer.Stop();
	}

	public Task<bool> Completion => this.completion.Task;

	#endregion

	#region Protected Methods

	protected override bool OnBackButtonPressed()
	{
		_ = this.CloseAsync(false);
		return true;
	}

	#endregion

	#region Private Methods

	private DisplayProfile ReadProfile() => new()
	{
		Theme = (string?)this.theme.SelectedItem ?? "Default",
		FontSize = double.Parse(this.size.Text, CultureInfo.CurrentCulture),
		LineSpacing = double.Parse(this.spacing.Text, CultureInfo.CurrentCulture),
		Columns = Math.Max(1, this.columns.SelectedIndex),
		AutoColumns = this.columns.SelectedIndex == 0,
		ShowChords = this.chords.IsChecked,
		NotationSystem = (string?)this.notation.SelectedItem ?? "Letter",
	};

	private void HandlePreviewChanged(object? sender, EventArgs e)
	{
		this.previewTimer.Stop();
		this.previewTimer.Start();
	}

	private void RefreshPreview()
	{
		try
		{
			DisplayProfile profile = this.ReadProfile();
			if (profile.Theme == "Default")
			{
				var app = global::Microsoft.Maui.Controls.Application.Current!;
				AppTheme theme = app.UserAppTheme == AppTheme.Unspecified ? app.RequestedTheme : app.UserAppTheme;
				profile.Theme = theme == AppTheme.Dark ? "Dark" : "Light";
			}

			this.preview.Source = new HtmlWebViewSource { Html = SongDisplaySettings.RenderPreview(profile) };
			this.status.Text = string.Empty;
		}
		catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
		{
			this.status.Text = exception.Message;
		}
	}

	private async Task SaveAsync(bool reset)
	{
		if (!this.busy)
		{
			this.busy = true;
			try
			{
				DisplayProfile? profile = reset ? null : this.ReadProfile();
				await this.session.SaveDisplaySettingsAsync(this.songId, profile).ConfigureAwait(true);
				await this.Navigation.PopModalAsync().ConfigureAwait(true);
				this.completion.TrySetResult(true);
			}
			catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException
				or IOException or UnauthorizedAccessException)
			{
				this.status.Text = exception.Message;
			}
			finally
			{
				this.busy = false;
			}
		}
	}

	private async Task CloseAsync(bool saved)
	{
		if (!this.busy)
		{
			await this.Navigation.PopModalAsync().ConfigureAwait(true);
			this.completion.TrySetResult(saved);
		}
	}

	#endregion
}
