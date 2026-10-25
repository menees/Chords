namespace Menees.Chords.Book.Maui;

/// <summary>Keeps navigation fixed while the caption and action group can wrap beside it.</summary>
[ContentProperty(nameof(Actions))]
public sealed partial class ScreenHeader : Grid
{
	private const int Gap = 8;
	private readonly FlexLayout actions = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };

	public ScreenHeader()
		: this(new FluentIconButton())
	{
	}

	internal ScreenHeader(FluentIconButton back)
	{
		this.ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)];
		this.ColumnSpacing = Gap;
		back.SetIcon("ArrowLeft", "Back");
		back.Margin = 0;
		back.VerticalOptions = LayoutOptions.Start;
		this.Add(back);
		this.Add(this.actions, 1);
		this.actions.ChildAdded += (_, args) =>
		{
			if (args.Element is View view)
			{
				view.Margin = new Thickness(this.actions.Children.Count > 1 ? Gap : 0, 0, 0, 0);
			}
		};
		back.Clicked += (_, _) => this.BackRequested?.Invoke(this, EventArgs.Empty);
	}

	public event EventHandler? BackRequested;

	public IList<IView> Actions => this.actions.Children;
}
