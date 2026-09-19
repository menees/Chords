namespace Menees.Chords.Book.Maui;

/// <summary>A compact primary action with related actions in its native drop-down menu.</summary>
public partial class FluentIconSplitButton : View
{
	public static readonly BindableProperty IconProperty = BindableProperty.Create(nameof(Icon), typeof(string), typeof(FluentIconSplitButton), string.Empty);

	public static readonly BindableProperty DescriptionProperty = BindableProperty.Create(
		nameof(Description), typeof(string), typeof(FluentIconSplitButton), string.Empty);

	public static readonly BindableProperty MenuItemsProperty = BindableProperty.Create(
		nameof(MenuItems), typeof(IReadOnlyList<FluentMenuItem>), typeof(FluentIconSplitButton), Array.Empty<FluentMenuItem>());

	public FluentIconSplitButton()
	{
		const int ButtonHeight = 32;
		this.HeightRequest = ButtonHeight;
		this.MinimumHeightRequest = ButtonHeight;
		this.VerticalOptions = LayoutOptions.Center;
	}

	public event EventHandler? Clicked;

	public string Icon { get => (string)this.GetValue(IconProperty); set => this.SetValue(IconProperty, value); }

	public string Description { get => (string)this.GetValue(DescriptionProperty); set => this.SetValue(DescriptionProperty, value); }

	public IReadOnlyList<FluentMenuItem> MenuItems
	{
		get => (IReadOnlyList<FluentMenuItem>)this.GetValue(MenuItemsProperty);
		set => this.SetValue(MenuItemsProperty, value);
	}

	internal void Invoke() => this.Clicked?.Invoke(this, EventArgs.Empty);
}
