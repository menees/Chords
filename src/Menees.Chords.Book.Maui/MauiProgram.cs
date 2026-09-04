#region Using Directives

using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui.Platforms.Windows;
using Menees.Chords.Book.Maui.Services;

#endregion

namespace Menees.Chords.Book.Maui;

public static class MauiProgram
{
	#region Public API

	public static MauiApp CreateMauiApp()
	{
		MauiAppBuilder builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<App>();
		builder.Services.AddSingleton<IWindowsPicker, WindowsPicker>();
		builder.Services.AddSingleton<BookApplicationSession>();
		builder.Services.AddSingleton<BookSession>();
		builder.Services.AddSingleton<MainPage>();
		return builder.Build();
	}

	#endregion
}
