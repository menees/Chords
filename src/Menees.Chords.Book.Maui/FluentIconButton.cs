namespace Menees.Chords.Book.Maui;

/// <summary>A compact Fluent System Icon button with an accessible name and tooltip.</summary>
public sealed partial class FluentIconButton : Button
{
	public const string Play = "Play";
	public const string Stop = "Stop";
	public const string Save = "Save";
	public const string Close = "Dismiss";
	public static readonly BindableProperty IconProperty = BindableProperty.Create(nameof(Icon), typeof(string), typeof(FluentIconButton), string.Empty);
	public static readonly BindableProperty DescriptionProperty = BindableProperty.Create(
		nameof(Description),
		typeof(string),
		typeof(FluentIconButton),
		string.Empty,
		propertyChanged: (view, _, value) =>
		{
			SemanticProperties.SetDescription(view, (string)value);
			ToolTipProperties.SetText(view, (string)value);
		});

	private const int ButtonSize = 32;

	public FluentIconButton()
	{
		this.WidthRequest = ButtonSize;
		this.HeightRequest = ButtonSize;
		this.Padding = 0;
	}

	public string Icon { get => (string)this.GetValue(IconProperty); set => this.SetValue(IconProperty, value); }

	public string Description { get => (string)this.GetValue(DescriptionProperty); set => this.SetValue(DescriptionProperty, value); }

	public void SetIcon(string icon, string description)
	{
		this.Icon = icon;
		this.Description = description;
	}
}
