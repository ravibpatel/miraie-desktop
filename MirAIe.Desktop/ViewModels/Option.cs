namespace MirAIe.Desktop.ViewModels;

/// <summary>
/// A choice shown in a selection list. The non-generic base lets one data template display any option.
/// </summary>
public record Option(string Label);

public sealed record Option<T>(T Value, string Label) : Option(Label);
