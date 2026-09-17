#region Using Directives

using System.Globalization;
using System.Runtime.InteropServices;

#endregion

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Persists the normal window bounds even when closing maximized or minimized.</summary>
internal static partial class WindowsWindowPlacement
{
	#region Implementation

	private const string PreferenceKey = "ChordBook.WindowPlacement";
	private const uint Normal = 1;
	private const uint Minimized = 2;
	private const uint Maximized = 3;
	private const uint RestoreToMaximized = 2;

	public static void Attach(Microsoft.UI.Xaml.Window window)
	{
		nint handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
		string[] values = Preferences.Default.Get(PreferenceKey, string.Empty).Split(',');
		const int ValueCount = 5;
		int[] numbers = new int[ValueCount];
		if (values.Length == ValueCount
			&& values.Select((value, index) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[index])).All(valid => valid)
			&& numbers[2] > numbers[0] && numbers[3] > numbers[1])
		{
			Placement placement = new()
			{
				Length = (uint)Marshal.SizeOf<Placement>(),
				ShowCommand = numbers[4] == 1 ? Maximized : Normal,
				NormalPosition = new() { Left = numbers[0], Top = numbers[1], Right = numbers[2], Bottom = numbers[3] },
			};
			_ = SetWindowPlacement(handle, ref placement);
		}

		window.AppWindow.Closing += (_, _) =>
		{
			Placement placement = new() { Length = (uint)Marshal.SizeOf<Placement>() };
			if (GetWindowPlacement(handle, ref placement) != 0)
			{
				bool maximized = placement.ShowCommand == Maximized
					|| (placement.ShowCommand == Minimized && (placement.Flags & RestoreToMaximized) != 0);
				Rectangle bounds = placement.NormalPosition;
				Preferences.Default.Set(PreferenceKey, FormattableString.Invariant(
					$"{bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom},{(maximized ? 1 : 0)}"));
			}
		};
	}

	[LibraryImport("user32.dll")]
	private static partial int GetWindowPlacement(nint window, ref Placement placement);

	[LibraryImport("user32.dll")]
	private static partial int SetWindowPlacement(nint window, ref Placement placement);

	#endregion

	#region Native Types

	[StructLayout(LayoutKind.Sequential)]
	private struct Point
	{
		public int X;
		public int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Rectangle
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Placement
	{
		public uint Length;
		public uint Flags;
		public uint ShowCommand;
		public Point MinimumPosition;
		public Point MaximumPosition;
		public Rectangle NormalPosition;
	}

	#endregion
}
