using System.Collections.Generic;
using global::Avalonia.Controls;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.Views.Dialogs;

public sealed record MsfxStateDetailDialogModel(
    string Header,
    string SubHeader,
    TraceEntryState State,
    string HighlightTitle,
    string HighlightMessage,
    IReadOnlyList<InfoDetailItem> Items);

public partial class MsfxStateDetailDialogView : UserControl
{
    public MsfxStateDetailDialogView()
    {
        InitializeComponent();
    }
}
