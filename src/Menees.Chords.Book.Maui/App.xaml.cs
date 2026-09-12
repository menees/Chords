namespace Menees.Chords.Book.Maui;

public partial class App : global::Microsoft.Maui.Controls.Application
{
	#region Private Data

	private const double InitialWindowHeight = 900;
	private const double InitialWindowWidth = 1440;
	private readonly MainPage mainPage;

	#endregion

	#region Constructors

	public App(MainPage mainPage)
	{
		this.InitializeComponent();
		this.mainPage = mainPage;
	}

	#endregion

	#region Protected Methods

	protected override Window CreateWindow(IActivationState? activationState)
	{
		Window window = new(this.mainPage)
		{
			Title = "ChordBook",
			Width = InitialWindowWidth,
			Height = InitialWindowHeight,
		};
		window.Activated += (_, _) => this.mainPage.SetWindowActive(true);
		window.Deactivated += (_, _) => this.mainPage.SetWindowActive(false);
		window.Destroying += (_, _) => this.mainPage.SetWindowActive(false);
		return window;
	}

	#endregion
}
