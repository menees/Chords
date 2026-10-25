using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Retains MAUI's page stack while the app's shared headers own all navigation chrome.</summary>
internal static class WindowsNavigationChrome
{
	public static void Attach(NavigationPage navigation)
	{
		navigation.Loaded += (_, _) => Apply(navigation);
		navigation.Pushed += (_, _) => Apply(navigation);
		navigation.Popped += (_, _) => Apply(navigation);
	}

	private static void Apply(NavigationPage navigation)
	{
		if (navigation.Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window window && window.Content is DependencyObject content)
		{
			WindowRootView? root = FindRoot(content);
			if (root?.NavigationViewControl is RootNavigationView view)
			{
				view.IsPaneOpen = false;
				view.IsPaneVisible = false;
				view.IsPaneToggleButtonVisible = false;
				view.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
			}
		}
	}

	private static WindowRootView? FindRoot(DependencyObject element)
	{
		WindowRootView? result = element as WindowRootView;
		for (int index = 0; result is null && index < VisualTreeHelper.GetChildrenCount(element); index++)
		{
			result = FindRoot(VisualTreeHelper.GetChild(element, index));
		}

		return result;
	}
}
