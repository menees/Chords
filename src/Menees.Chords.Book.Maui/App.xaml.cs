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
		this.UserAppTheme = Enum.TryParse(Preferences.Default.Get("ChordBook.AppTheme", "Unspecified"), out AppTheme saved) && Enum.IsDefined(saved)
			? saved : AppTheme.Unspecified;
		this.RequestedThemeChanged += (_, _) => this.ApplyPalette();
		this.ApplyPalette();
	}

	#endregion

	#region Public Methods

	public void SetTheme(AppTheme theme)
	{
		this.UserAppTheme = theme;
		Preferences.Default.Set("ChordBook.AppTheme", theme.ToString());
		this.ApplyPalette();
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
		window.Created += (_, _) =>
		{
			this.ApplyPalette();
			if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
			{
				Platforms.Windows.WindowsWindowPlacement.Attach(native);
			}
		};
		window.Activated += (_, _) => this.mainPage.SetWindowActive(true);
		window.Deactivated += (_, _) => this.mainPage.SetWindowActive(false);
		window.Destroying += (_, _) => this.mainPage.SetWindowActive(false);
		return window;
	}

	#endregion

	#region Private Methods

	private void ApplyPalette()
	{
		bool dark = (this.UserAppTheme == AppTheme.Unspecified ? this.RequestedTheme : this.UserAppTheme) == AppTheme.Dark;
		this.Resources["AppBackground"] = Color.FromArgb(dark ? "#202020" : "#FAFAFA");
		this.Resources["AppSurface"] = Color.FromArgb(dark ? "#292929" : "#F0F0F0");
		this.Resources["AppSection"] = Color.FromArgb(dark ? "#383838" : "#E0E0E0");
		this.Resources["AppText"] = Color.FromArgb(dark ? "#F2F2F2" : "#202020");
		this.Resources["AppMuted"] = Color.FromArgb(dark ? "#BEBEBE" : "#606060");
		this.Resources["AppControl"] = Color.FromArgb(dark ? "#353535" : "#FFFFFF");
		foreach (Window window in this.Windows)
		{
			if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native
				&& native.Content is Microsoft.UI.Xaml.FrameworkElement root)
			{
				root.RequestedTheme = this.UserAppTheme == AppTheme.Unspecified ? Microsoft.UI.Xaml.ElementTheme.Default
					: dark ? Microsoft.UI.Xaml.ElementTheme.Dark : Microsoft.UI.Xaml.ElementTheme.Light;
			}
		}

		this.mainPage.ApplyViewerTheme();
	}

	#endregion
}
