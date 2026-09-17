namespace Menees.Chords.Book.Maui;

/// <summary>A stable numbered strip. Only the previous and current beat change during playback.</summary>
public sealed partial class MetronomeBeatView : HorizontalStackLayout
{
	private const int BoxSize = 28;
	private const int BoxSpacing = 4;
	private readonly List<Label> boxes = [];
	private int activeBeat;
	private bool activeAccent;

	public MetronomeBeatView()
	{
		this.Spacing = BoxSpacing;
		this.HorizontalOptions = LayoutOptions.Center;
	}

	public void Update(int count, int beat, bool accent)
	{
		if (this.boxes.Count != count)
		{
			this.Children.Clear();
			this.boxes.Clear();
			for (int index = 1; index <= count; index++)
			{
				Label box = new()
				{
					Text = index.ToString(System.Globalization.CultureInfo.CurrentCulture), WidthRequest = BoxSize, HeightRequest = BoxSize,
					HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center,
				};
				box.SetDynamicResource(Label.BackgroundColorProperty, "AppSection");
				box.SetDynamicResource(Label.TextColorProperty, "AppText");
				this.boxes.Add(box);
				this.Children.Add(box);
			}

			this.activeBeat = 0;
		}

		if (beat != this.activeBeat || accent != this.activeAccent)
		{
			if (this.activeBeat > 0 && this.activeBeat <= this.boxes.Count)
			{
				Label previous = this.boxes[this.activeBeat - 1];
				previous.SetDynamicResource(Label.BackgroundColorProperty, "AppSection");
				previous.SetDynamicResource(Label.TextColorProperty, "AppText");
				previous.FontAttributes = FontAttributes.None;
			}

			if (beat > 0 && beat <= this.boxes.Count)
			{
				Label current = this.boxes[beat - 1];
				current.SetDynamicResource(Label.BackgroundColorProperty, accent ? "ChordAccentBeat" : "ChordAccent");
				current.TextColor = Colors.White;
				current.FontAttributes = accent ? FontAttributes.Bold : FontAttributes.None;
			}

			this.activeBeat = beat;
			this.activeAccent = accent;
		}
	}
}
