using Menees.Chords.Book.Application;

namespace Menees.Chords.Book.Maui;

/// <summary>Switches between the instruments assigned to the current song.</summary>
public sealed partial class InstrumentSelector : ContentView
{
	private readonly FluentIconSplitButton button = new();
	private IReadOnlyList<InstrumentProfileInfo> profiles = [];

	public InstrumentSelector()
	{
		this.Content = this.button;
		this.button.Clicked += (_, _) =>
		{
			int index = this.profiles.ToList().FindIndex(profile => profile.Id == this.SelectedId);
			if (this.profiles.Count > 1)
			{
				this.Select(this.profiles[(index + 1) % this.profiles.Count].Id);
			}
		};
	}

	public event EventHandler? Changed;

	public Guid? SelectedId { get; private set; }

	public void Update(IReadOnlyList<InstrumentProfileInfo> instruments, InstrumentProfileInfo? current)
	{
		this.profiles = instruments;
		this.SelectedId = current?.Id;
		this.IsVisible = instruments.Count > 1;
		this.button.Icon = current is null ? string.Empty : GetIcon(current);
		this.button.Description = current is null ? "Instrument" : current.Name + " — next instrument";
		this.button.MenuItems = [.. instruments.Select(profile => new FluentMenuItem(
			profile.Name, () => this.Select(profile.Id), Icon: GetIcon(profile)))];
	}

	private static string GetIcon(InstrumentProfileInfo profile)
	{
		string? name = StandardInstruments.All.FirstOrDefault(item => item.Id == profile.Id).Name;
		name ??= StandardInstruments.All.FirstOrDefault(item => item.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)).Name;
		return name?.Replace(" ", string.Empty, StringComparison.Ordinal) ?? string.Empty;
	}

	private void Select(Guid id)
	{
		if (this.SelectedId != id)
		{
			this.SelectedId = id;
			this.Changed?.Invoke(this, EventArgs.Empty);
		}
	}
}
