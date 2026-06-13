using System.Collections.Generic;
using global::Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Views.Dialogs;

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

