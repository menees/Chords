namespace Menees.Chords.Book.Maui;

/// <summary>A native menu action and its optional explanatory tooltip.</summary>
public sealed record FluentMenuItem(string Text, Action Invoke, string? Description = null, bool IsEnabled = true);
