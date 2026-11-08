using NativeProgressBar = Microsoft.UI.Xaml.Controls.ProgressBar;

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Adjusts the native track and indicator together while retaining the WinUI progress control.</summary>
internal static class WindowsProgressBar
{
	public static void SetTrackHeight(ProgressBar view, double height)
	{
		view.HandlerChanged += (_, _) =>
		{
			if (view.Handler?.PlatformView is NativeProgressBar native)
			{
				native.Resources["ProgressBarTrackHeight"] = height;
				native.MinHeight = height;
			}
		};
	}
}
