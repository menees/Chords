namespace Menees.Chords.Db;

/// <summary>Maps one key combination to a performance command.</summary>
/// <param name="Gesture">The key combination to match.</param>
/// <param name="Command">The command to execute.</param>
public sealed record PerformanceKeyBinding(PerformanceKeyGesture Gesture, PerformanceCommand Command)
{
	/// <summary>Creates independently editable default page-turn bindings.</summary>
	public static List<PerformanceKeyBinding> CreateDefaults() =>
	[
		new(new("PageDown"), PerformanceCommand.NextViewport),
		new(new("PageUp"), PerformanceCommand.PreviousViewport),
		new(new("ArrowDown"), PerformanceCommand.NextViewport),
		new(new("ArrowUp"), PerformanceCommand.PreviousViewport),
		new(new("ArrowRight"), PerformanceCommand.NextViewport),
		new(new("ArrowLeft"), PerformanceCommand.PreviousViewport),
		new(new(" "), PerformanceCommand.NextViewport),
	];
}
