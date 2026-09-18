namespace Menees.Chords.Book.Maui;

/// <summary>A compact Fluent System Icon toggle with an accessible name and tooltip.</summary>
public partial class FluentIconToggle : CheckBox
{
	public static readonly BindableProperty IconProperty = BindableProperty.Create(nameof(Icon), typeof(string), typeof(FluentIconToggle), string.Empty);
	public static readonly BindableProperty DescriptionProperty = BindableProperty.Create(
		nameof(Description),
		typeof(string),
		typeof(FluentIconToggle),
		string.Empty,
		propertyChanged: (view, _, value) =>
		{
			SemanticProperties.SetDescription(view, (string)value);
			ToolTipProperties.SetText(view, (string)value);
		});

	private const int ButtonSize = 32;

	public FluentIconToggle()
	{
		this.WidthRequest = ButtonSize;
		this.HeightRequest = ButtonSize;
	}

	public string Icon { get => (string)this.GetValue(IconProperty); set => this.SetValue(IconProperty, value); }

	public string Description { get => (string)this.GetValue(DescriptionProperty); set => this.SetValue(DescriptionProperty, value); }
}
