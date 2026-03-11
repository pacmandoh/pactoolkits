using System.Collections.Generic;
using Avalonia.Controls;
using pactoolkits_ui.Contracts;

namespace pactoolkits_ui.Views.Dialogs;

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
