using System.Collections.Generic;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;

public enum AlertRole
{
    Dismiss,
    Cancel,
    Affirm,
    Danger,
    Ack
}

internal enum AlertSlot
{
    Left,
    Right
}

public readonly record struct AlertButton<T>(string Text, T Value, AlertRole Role);

public sealed class AlertBuilder<T>
{
    private readonly List<AlertButton<T>> _buttons = [];
    private T _closeValue = default!;
    private bool _hasCloseValue;

    private AlertBuilder(string title, string message)
    {
        Title = title;
        Message = message;
    }

    public string Title { get; }

    public string Message { get; }

    internal T CloseValue => _closeValue;

    internal bool HasCloseValue => _hasCloseValue;

    public static AlertBuilder<T> Create(string title, string message)
        => new(title, message);

    public AlertBuilder<T> Close(T value)
    {
        _closeValue = value;
        _hasCloseValue = true;
        return this;
    }

    public AlertBuilder<T> Dismiss(string text, T value)
        => Button(AlertRole.Dismiss, text, value);

    public AlertBuilder<T> Cancel(string text, T value)
        => Button(AlertRole.Cancel, text, value);

    public AlertBuilder<T> Affirm(string text, T value)
        => Button(AlertRole.Affirm, text, value);

    public AlertBuilder<T> Danger(string text, T value)
        => Button(AlertRole.Danger, text, value);

    public AlertBuilder<T> Ack(string text, T value)
        => Button(AlertRole.Ack, text, value);

    public AlertBuilder<T> Button(AlertRole role, string text, T value)
    {
        _buttons.Add(new AlertButton<T>(text, value, role));
        return this;
    }

    internal IReadOnlyList<AlertButton<T>> Buttons => _buttons;
}

public static class AlertBuilderExtensions
{
    public static AlertBuilder<bool?> DiscardOrSave(
        this AlertBuilder<bool?> builder,
        string affirmText,
        string dismissText)
        => builder.Close(null).Dismiss(dismissText, false).Affirm(affirmText, true);

    public static AlertBuilder<bool?> SaveConflict(
        this AlertBuilder<bool?> builder,
        string dismissText,
        string forceSaveText)
        => builder.Close(null).Dismiss(dismissText, false).Danger(forceSaveText, true);
}

internal static class AlertLayout
{
    internal static AlertSlot Slot(AlertRole role)
        => role is AlertRole.Affirm or AlertRole.Danger or AlertRole.Ack
            ? AlertSlot.Right
            : AlertSlot.Left;

    internal static DialogButtonStyle Style(AlertRole role)
        => role switch
        {
            AlertRole.Dismiss => DialogButtonStyle.Ghost,
            AlertRole.Cancel => DialogButtonStyle.Ghost,
            AlertRole.Affirm => DialogButtonStyle.Primary,
            AlertRole.Danger => DialogButtonStyle.Destructive,
            AlertRole.Ack => DialogButtonStyle.Outline,
            _ => DialogButtonStyle.Ghost
        };
}
