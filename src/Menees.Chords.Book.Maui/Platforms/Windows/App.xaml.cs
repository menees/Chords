namespace Menees.Chords.Book.Maui.WinUI;

public partial class App : MauiWinUIApplication
{
	#region Constructors

	public App()
	{
		Platforms.Windows.WindowsWebViewProfile.Configure();
		this.InitializeComponent();
	}

	#endregion

	#region Protected Methods

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	#endregion
}
