#region Using Directives

using System.Diagnostics;
using Menees.Chords.Book.Maui.Services;
using Microsoft.Maui.Platform;
using Windows.Storage.Pickers;
using WinRT.Interop;

#endregion

namespace Menees.Chords.Book.Maui.Platforms.Windows;

public sealed class WindowsPicker : IWindowsPicker
{
	#region Public API

	public async Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken cancellationToken)
	{
		FileOpenPicker picker = new()
		{
			SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
			ViewMode = PickerViewMode.List,
		};
		picker.FileTypeFilter.Add("*");
		InitializeWithWindow.Initialize(picker, GetWindowHandle());
		IReadOnlyList<global::Windows.Storage.StorageFile> files = await picker.PickMultipleFilesAsync()
			.AsTask(cancellationToken).ConfigureAwait(true);
		return [.. files.Select(file => file.Path)];
	}

	public async Task<string?> PickFolderAsync(CancellationToken cancellationToken)
	{
		FolderPicker picker = new();
		picker.FileTypeFilter.Add("*");
		InitializeWithWindow.Initialize(picker, GetWindowHandle());
		global::Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync()
			.AsTask(cancellationToken).ConfigureAwait(true);
		return folder?.Path;
	}

	public Task OpenFolderAsync(string path, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		cancellationToken.ThrowIfCancellationRequested();
		if (!Directory.Exists(path))
		{
			throw new DirectoryNotFoundException($"The book folder does not exist: {path}");
		}

		using Process? process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
		return Task.CompletedTask;
	}

	#endregion

	#region Private Methods

	private static IntPtr GetWindowHandle()
	{
		MauiWinUIWindow window = Application.Current?.Windows[0].Handler?.PlatformView as MauiWinUIWindow
			?? throw new InvalidOperationException("The MAUI WinUI window is not available.");
		return WindowNative.GetWindowHandle(window);
	}

	#endregion
}
