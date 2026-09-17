using Menees.Chords.Book.Maui.Services;

namespace Menees.Chords.Book.Maui;

public sealed partial class MetronomePage : ContentPage
{
	private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly MetronomePanel panel;
	private readonly IMetronomeEngine engine;

	public MetronomePage(Guid? songId, BookSession session, IMetronomeEngine engine)
	{
		this.InitializeComponent();
		this.engine = engine;
		this.panel = new(songId, session, engine);
		this.panel.Closed += this.HandleClose;
		this.Content = this.panel;
	}

	public Task Completion => this.completion.Task;

	protected override bool OnBackButtonPressed()
	{
		this.HandleClose(this, EventArgs.Empty);
		return true;
	}

	private async void HandleClose(object? sender, EventArgs e)
	{
		if (!this.panel.IsBusy)
		{
			this.panel.Detach();
			this.engine.Stop();
			await this.Navigation.PopModalAsync().ConfigureAwait(true);
			this.completion.TrySetResult();
		}
	}
}