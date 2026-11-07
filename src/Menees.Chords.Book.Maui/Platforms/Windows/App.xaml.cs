namespace Menees.Chords.Book.Maui.WinUI;

public partial class App : MauiWinUIApplication
{
	#region Private Data

	private readonly Platforms.Windows.WindowsFileActivation fileActivation = new();

	#endregion

	#region Constructors

	public App()
	{
		Platforms.Windows.WindowsWebViewProfile.Configure();
		this.InitializeComponent();
	}

	#endregion

	#region Protected Methods

	protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
	{
		if (!await this.fileActivation.StartAsync(paths => MainThread.BeginInvokeOnMainThread(() =>
		{
			if (global::Microsoft.Maui.Controls.Application.Current is global::Menees.Chords.Book.Maui.App app)
			{
				app.OpenSongFiles(paths);
			}
		})).ConfigureAwait(true))
		{
			this.Exit();
		}
		else
		{
			base.OnLaunched(args);
			this.fileActivation.DeliverInitial();
		}
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	#endregion
}
