namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Keeps browser state local to the device rather than beside distributed application binaries.</summary>
public static class WindowsWebViewProfile
{
	#region Public Methods

	public static void Configure(string? userDataFolder = null)
	{
		string directory = userDataFolder ?? Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Menees", "ChordBook", "WebView2");
		directory = Path.GetFullPath(directory);
		Directory.CreateDirectory(directory);
		Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", directory, EnvironmentVariableTarget.Process);
	}

	#endregion
}
