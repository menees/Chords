namespace Menees.Chords.Book.Maui;

/// <summary>Shared fixed upper-left dialog actions, outside scrolling content.</summary>
internal static class DialogLayout
{
	public static View Header(string title, params View[] actions)
	{
		const int HeadingSize = 22;
		FlexLayout header = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
		header.Children.Add(new Label { Text = title, FontSize = HeadingSize, VerticalOptions = LayoutOptions.Center });
		foreach (View action in actions)
		{
			header.Children.Add(action);
		}

		return header;
	}

	public static View Create(string title, View content, params View[] actions)
	{
		const int PaddingSize = 16;
		Grid layout = new() { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
		View header = Header(title, actions);
		header.Margin = new Thickness(PaddingSize, PaddingSize, PaddingSize, 0);
		layout.Add(header);
		layout.Add(content, 0, 1);
		return layout;
	}
}
