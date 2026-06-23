using System;
using System.Collections.Generic;
using Avalonia.Controls;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.Controls;

public sealed class PacHostedDialogAction
{
    public required string Text { get; init; }

    public DialogButtonStyle Style { get; init; } = DialogButtonStyle.Secondary;

    public bool DismissOnClick { get; init; } = true;

    public Action? Click { get; init; }
}

public sealed class PacHostedDialogContext
{
    public required string Title { get; init; }

    public required Control Body { get; init; }

    public required IReadOnlyList<PacHostedDialogAction> Actions { get; init; }

    internal DialogManager? Manager { get; set; }

    internal Action? RequestClose { get; set; }

    internal void InvokeAction(PacHostedDialogAction action)
    {
        action.Click?.Invoke();
        if (action.DismissOnClick)
        {
            RequestClose?.Invoke();
        }
    }
}
