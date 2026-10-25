namespace Menees.Chords.Book.Maui;

/// <summary>Shared screen headers and content, outside scrolling areas.</summary>
internal static class ScreenLayout
{
	public static ScreenHeader Header(string title, FluentIconButton back, params View[] actions)
	{
		const int HeadingSize = 22;
		ScreenHeader header = new(back);
		header.Actions.Add(new Label { Text = title, FontSize = HeadingSize, VerticalOptions = LayoutOptions.Center });
		foreach (View action in actions)
		{
			header.Actions.Add(action);
		}

		return header;
	}

	public static View Create(string title, View content, FluentIconButton back, params View[] actions)
	{
		const int PaddingSize = 8;
		Grid layout = new() { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
		View header = Header(title, back, actions);
		header.Margin = new Thickness(PaddingSize, PaddingSize, PaddingSize, 0);
		layout.Add(header);
		layout.Add(content, 0, 1);
		return layout;
	}
}
