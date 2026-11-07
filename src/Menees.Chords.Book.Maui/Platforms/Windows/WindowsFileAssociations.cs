#region Using Directives

using System.Runtime.InteropServices;
using Microsoft.Win32;

#endregion

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Per-user Open With/default-app registration. Windows owns the user's default choice.</summary>
public static partial class WindowsFileAssociations
{
	#region Private Data

	private const string ApplicationName = "Menees ChordBook";
	private const string ProgId = "Menees.ChordBook.Song";
	private const string Capabilities = @"Software\Menees\ChordBook\Capabilities";
	private const uint AssociationsChanged = 0x0800_0000;
	private static readonly string[] Extensions = [".cho", ".chopro", ".chordpro", ".crd"];

	#endregion

	#region Public Methods

	public static async Task ConfigureAsync(CancellationToken cancellationToken = default)
	{
		string executable = Environment.ProcessPath ?? throw new InvalidOperationException("The app executable could not be located.");
		await Task.Run(() => Register(Registry.CurrentUser, executable), cancellationToken).ConfigureAwait(true);
		SHChangeNotify(AssociationsChanged, 0, 0, 0);
		Uri settings = new("ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(ApplicationName));
		if (!await global::Windows.System.Launcher.LaunchUriAsync(settings).AsTask(cancellationToken).ConfigureAwait(true))
		{
			throw new InvalidOperationException("ChordBook is registered. Open Windows Settings > Apps > Default apps to choose it for .cho files.");
		}
	}

	/// <summary>Registers beneath the supplied user root; tests use a disposable isolated registry root.</summary>
	public static void Register(RegistryKey userRoot, string executable)
	{
		ArgumentNullException.ThrowIfNull(userRoot);
		ArgumentException.ThrowIfNullOrWhiteSpace(executable);
		executable = Path.GetFullPath(executable);
		if (!File.Exists(executable) || !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("Choose an existing ChordBook executable.", nameof(executable));
		}

		string command = $"\"{executable}\" --open \"%1\"";
		void Set(string path, string name, string value)
		{
			using RegistryKey key = userRoot.CreateSubKey(path);
			key.SetValue(name, value, RegistryValueKind.String);
		}

		string application = @"Software\Classes\Applications\" + Path.GetFileName(executable);
		Set(application, "FriendlyAppName", ApplicationName);
		Set(application + @"\shell\open\command", string.Empty, command);
		Set(@"Software\Classes\" + ProgId, string.Empty, "ChordBook song");
		Set(@"Software\Classes\" + ProgId + @"\DefaultIcon", string.Empty, $"\"{executable}\",0");
		Set(@"Software\Classes\" + ProgId + @"\shell\open\command", string.Empty, command);
		Set(Capabilities, "ApplicationName", ApplicationName);
		Set(Capabilities, "ApplicationDescription", "Open chord sheets in the current ChordBook.");
		Set(@"Software\RegisteredApplications", ApplicationName, Capabilities);
		foreach (string extension in Extensions)
		{
			Set(application + @"\SupportedTypes", extension, string.Empty);
			Set(Capabilities + @"\FileAssociations", extension, ProgId);
			using RegistryKey available = userRoot.CreateSubKey(@"Software\Classes\" + extension + @"\OpenWithProgids");
			available.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
		}
	}

	#endregion

	#region Private Methods

	[LibraryImport("shell32.dll")]
	private static partial void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);

	#endregion
}
