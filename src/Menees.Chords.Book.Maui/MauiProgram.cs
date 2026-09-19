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
		builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<TransposePicker, TransposePickerHandler>());
		builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<FluentIconButton, FluentIconButtonHandler>());
		builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<ArchiveToggle, FluentIconToggleHandler>()
			.AddHandler<FluentIconToggle, FluentIconToggleHandler>());
		builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<BookTabs, BookTabsHandler>());
		builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<PositionEntry, PositionEntryHandler>());
		builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<OpenBookSplitButton, FluentIconSplitButtonHandler>()
			.AddHandler<FluentIconSplitButton, FluentIconSplitButtonHandler>());
		Microsoft.Maui.Handlers.CheckBoxHandler.Mapper.AppendToMapping("CompactCheckBox", (handler, _) =>
		{
			handler.PlatformView.MinWidth = 0;
			handler.PlatformView.MinHeight = 0;
		});
		Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping(
			"CompactEntry", (handler, _) => handler.PlatformView.MinWidth = 0);
		Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping(
			"CompactPicker", (handler, _) => handler.PlatformView.MinWidth = 0);
		Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("EditorScrollbars", (handler, _) =>
		{
			Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(
				handler.PlatformView, Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Visible);
			Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollMode(
				handler.PlatformView, Microsoft.UI.Xaml.Controls.ScrollMode.Enabled);
		});
		Microsoft.Maui.Handlers.ButtonHandler.Mapper.AppendToMapping("CompactIconButton", (handler, view) =>
		{
			if (view is FluentIconButton)
			{
				handler.PlatformView.MinWidth = 0;
				handler.PlatformView.MinHeight = 0;
			}
		});
		builder.Services.AddSingleton<IWindowsPicker, WindowsPicker>();
		builder.Services.AddSingleton<IMetronomeEngine, WindowsMetronomeEngine>();
		builder.Services.AddSingleton<BookApplicationSession>();
		builder.Services.AddSingleton<BookSession>();
		builder.Services.AddSingleton<MainPage>();
		return builder.Build();
	}

	#endregion
}
