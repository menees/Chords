using Menees.Chords.Book.Maui.Services;

namespace Menees.Chords.Book.Maui;

public sealed partial class MetronomePage : ContentPage
{
	private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly MetronomePanel panel;
	private readonly IMetronomeEngine engine;
	private bool leaving;

	public MetronomePage(Guid? songId, BookSession session, IMetronomeEngine engine)
	{
		this.InitializeComponent();
		this.engine = engine;
		this.panel = new(songId, session, engine, fullPage: true);
		FluentIconButton back = new();
		back.Clicked += this.HandleClose;
		FluentIconButton save = new() { Icon = "Save", Description = "Save and Return" };
		save.Clicked += async (_, _) => await this.SaveAndCloseAsync().ConfigureAwait(true);
		this.Content = ScreenLayout.Create("Metronome Defaults", new ScrollView { Content = this.panel }, back, save);
	}

	public Task Completion => this.completion.Task;

	protected override bool OnBackButtonPressed()
	{
		this.HandleClose(this, EventArgs.Empty);
		return true;
	}

	private async Task SaveAndCloseAsync()
	{
		if (await this.panel.SaveAsync().ConfigureAwait(true))
		{
			await this.CloseAsync().ConfigureAwait(true);
		}
	}

	private async void HandleClose(object? sender, EventArgs e)
	{
		if (!this.panel.IsBusy && !this.leaving)
		{
			this.leaving = true;
			this.IsEnabled = false;
			try
			{
				await ScreenNavigation.LeaveEditorAsync(this, this.panel.HasChanges, this.SaveAndCloseAsync, this.CloseAsync).ConfigureAwait(true);
			}
			finally
			{
				this.IsEnabled = true;
				this.leaving = false;
			}
		}
	}

	private async Task CloseAsync()
	{
		this.panel.Detach();
		this.engine.Stop();
		await this.Navigation.PopAsync().ConfigureAwait(true);
		this.completion.TrySetResult();
	}
}
