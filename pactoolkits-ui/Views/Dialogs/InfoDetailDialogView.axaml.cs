using System.Collections.Generic;
using Avalonia.Controls;

namespace pactoolkits_ui.Views.Dialogs;

public sealed record InfoDetailItem(string Label, string Value);

public sealed record InfoDetailDialogModel(
    string Header,
    string SubHeader,
    IReadOnlyList<InfoDetailItem> Items);

public partial class InfoDetailDialogView : UserControl
{
    public InfoDetailDialogView()
    {
        InitializeComponent();
    }
}

