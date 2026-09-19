namespace Menees.Chords.Book.Maui;

/// <summary>A key-aware transpose flyout with adjacent semitone controls.</summary>
public sealed partial class TransposePicker : View
{
	public static readonly BindableProperty OriginalKeyProperty = BindableProperty.Create(
		nameof(OriginalKey), typeof(string), typeof(TransposePicker), null);

	public static readonly BindableProperty OffsetProperty = BindableProperty.Create(
		nameof(Offset),
		typeof(int),
		typeof(TransposePicker),
		0,
		coerceValue: (_, value) => Math.Clamp((int)value, -11, 11),
		propertyChanged: (view, _, _) => ((TransposePicker)view).Changed?.Invoke(view, EventArgs.Empty));

	public TransposePicker()
	{
		const int Height = 32;
		const int Width = 180;
		this.HeightRequest = Height;
		this.WidthRequest = Width;
	}

	public event EventHandler? Changed;

	public string? OriginalKey { get => (string?)this.GetValue(OriginalKeyProperty); set => this.SetValue(OriginalKeyProperty, value); }

	public int Offset { get => (int)this.GetValue(OffsetProperty); set => this.SetValue(OffsetProperty, value); }
}
