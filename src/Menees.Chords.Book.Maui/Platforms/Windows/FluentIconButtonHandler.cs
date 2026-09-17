using Microsoft.Maui.Handlers;

namespace Menees.Chords.Book.Maui.Platforms.Windows;

public sealed class FluentIconButtonHandler : ButtonHandler
{
	private static readonly IPropertyMapper<IButton, IButtonHandler> IconMapper = new PropertyMapper<IButton, IButtonHandler>(Mapper)
	{
		[nameof(FluentIconButton.Icon)] = (handler, view) =>
		{
			if (view is FluentIconButton { Icon.Length: > 0 } button)
			{
				handler.PlatformView.Content = FluentIconSource.Create(button.Icon);
				handler.PlatformView.MinWidth = 0;
				handler.PlatformView.MinHeight = 0;
			}
		},
	};

	public FluentIconButtonHandler()
		: base(IconMapper)
	{
	}
}
