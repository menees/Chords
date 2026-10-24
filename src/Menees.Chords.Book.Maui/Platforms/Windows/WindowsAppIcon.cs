namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Uses the same size-specific artwork for the running window and executable.</summary>
internal static class WindowsAppIcon
{
	#region Public Methods

	public static void Apply(Microsoft.UI.Xaml.Window window)
	{
		window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ChordBook.ico"));
	}

	#endregion
}
