#region Using Directives

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Windows.AppLifecycle;

#endregion

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Routes launch/file activations to one process per executable location.</summary>
public sealed partial class WindowsFileActivation
{
	#region Private Data

	private AppActivationArguments? initial;
	private Action<IReadOnlyList<string>>? receive;

	#endregion

	#region Public Methods

	/// <summary>Uses Windows command-line parsing so spaces, Unicode and quoted paths remain single filenames.</summary>
	public static IReadOnlyList<string> ParseCommandLine(string commandLine)
	{
		ArgumentNullException.ThrowIfNull(commandLine);
		List<string> paths = [];
		if (!string.IsNullOrWhiteSpace(commandLine))
		{
			nint arguments = CommandLineToArgvW(commandLine, out int count);
			if (arguments == 0)
			{
				throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
			}

			try
			{
				string? executable = Environment.ProcessPath;
				for (int index = 0; index < count; index++)
				{
					string value = Marshal.PtrToStringUni(Marshal.ReadIntPtr(arguments, index * IntPtr.Size)) ?? string.Empty;
					if (value.Length > 0 && value != "--open" && !string.Equals(value, executable, StringComparison.OrdinalIgnoreCase))
					{
						paths.Add(Path.GetFullPath(value));
					}
				}
			}
			finally
			{
				_ = LocalFree(arguments);
			}
		}

		return [.. paths.Distinct(StringComparer.OrdinalIgnoreCase)];
	}

	public async Task<bool> StartAsync(Action<IReadOnlyList<string>> receive, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(receive);
		AppInstance current = AppInstance.GetCurrent();
		AppActivationArguments args = current.GetActivatedEventArgs();
		string executable = Path.GetFullPath(Environment.ProcessPath ?? throw new InvalidOperationException("The app executable could not be located."));
		string key = "Menees.ChordBook." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(executable.ToUpperInvariant())));
		AppInstance instance = AppInstance.FindOrRegisterForKey(key);
		if (!instance.IsCurrent)
		{
			_ = AllowSetForegroundWindow(instance.ProcessId);
			await instance.RedirectActivationToAsync(args).AsTask(cancellationToken).ConfigureAwait(true);
		}
		else
		{
			this.receive = receive;
			this.initial = args;
			current.Activated += (_, activated) => this.Deliver(activated);
		}

		return instance.IsCurrent;
	}

	public void DeliverInitial()
	{
		if (this.initial is AppActivationArguments args)
		{
			this.initial = null;
			this.Deliver(args);
		}
	}

	#endregion

	#region Private Methods

	[LibraryImport("user32.dll")]
	private static partial int AllowSetForegroundWindow(uint processId);

	[LibraryImport("shell32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
	private static partial nint CommandLineToArgvW(string commandLine, out int count);

	[LibraryImport("kernel32.dll")]
	private static partial nint LocalFree(nint memory);

	private void Deliver(AppActivationArguments args)
	{
		IReadOnlyList<string> paths = args.Data switch
		{
			global::Windows.ApplicationModel.Activation.IFileActivatedEventArgs files => [.. files.Files.Select(file => file.Path).Where(path => path.Length > 0)],
			global::Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch => ParseCommandLine(launch.Arguments),
			_ => [],
		};
		this.receive?.Invoke(paths);
	}

	#endregion
}
