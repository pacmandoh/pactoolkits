using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class DrugIndexView : UserControl
{
    private static readonly string[] CopyFields = { "DrugId", "Spec" };
    private static readonly string[] ContextGridNames = { "DrugGrid" };

    public DrugIndexView()
        : this(((global::Avalonia.Application.Current as App)?.Services.GetRequiredService<IClipboardService>())
               ?? throw new InvalidOperationException("IClipboardService not available. Ensure it is registered in App.Services."))
    {
    }

    private readonly IClipboardService _clipboard;

    public DrugIndexView(IClipboardService clipboard)
    {
        _clipboard = clipboard;
        InitializeComponent();
        AddHandler(KeyDownEvent, OnEditorAreaKeyDown, RoutingStrategies.Tunnel);
    }

    private void DrugIndexSearchBox_OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        InputFocusHelper.FocusControlByName(this, "DrugIndexSearchButton", DispatcherPriority.Background);
    }

    private void OnEditorAreaKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab && e.Key != Key.Enter)
        {
            return;
        }

        var editorCard = this.FindControl<Control>("EditorCard");
        if (editorCard is null)
        {
            return;
        }

        var inputs = EnumerateEditorInputs(editorCard).ToList();
        InputFocusHelper.TryHandleTabCycle(this, e, inputs);
    }

    private static IEnumerable<Control> EnumerateEditorInputs(Control root) =>
        InputFocusHelper.EnumerateInputs(root, typeof(TextBox), typeof(NumericUpDown));

    public async void OnGridRowCopy(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi)
        {
            return;
        }

        await GridContextMenuActions.CopySafeAsync(
            _clipboard,
            this,
            mi,
            mi.CommandParameter,
            ContextGridNames,
            CopyFields,
            "DrugIndexView",
            "drug_index.context_copy.fail",
            "Failed copying drug row");
    }

    public void OnGridSelectAll(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi)
        {
            return;
        }

        GridContextMenuActions.SelectAllSafe(
            this,
            mi,
            mi.CommandParameter,
            ContextGridNames,
            "DrugIndexView",
            "drug_index.context_select_all.fail",
            "Failed selecting all rows");
    }

    public void OnDrugGridContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        if (DataContext is not DrugIndexViewModel vm)
        {
            return;
        }

        vm.SyncEditorUnlockStateForUi();
        var canToggle = vm.IsEditorInputEnabled;
        foreach (var item in menu.Items)
        {
            switch (item)
            {
                case MenuItem menuItem when menuItem.Classes.Contains("DrugIndexSensitiveMenuItem"):
                    menuItem.IsVisible = canToggle;
                    menuItem.IsEnabled = canToggle;
                    break;
                case Separator separator when separator.Classes.Contains("DrugIndexSensitiveMenuSeparator"):
                    separator.IsVisible = canToggle;
                    break;
            }
        }
    }

    public void OnGridToggleDeprecated(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi)
        {
            return;
        }

        var rowItem = mi.CommandParameter;
        if (rowItem is null)
        {
            return;
        }

        var grid = this.FindControl<DataGrid>("DrugGrid");
        grid?.SelectedItem = rowItem;

        if (DataContext is DrugIndexViewModel vm
            && vm.ToggleDeprecatedCommand.CanExecute(rowItem))
        {
            vm.ToggleDeprecatedCommand.Execute(rowItem);
        }
    }

    public void OnGridToggleNoSplit(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi)
        {
            return;
        }

        var rowItem = mi.CommandParameter;
        if (rowItem is null)
        {
            return;
        }

        var grid = this.FindControl<DataGrid>("DrugGrid");
        grid?.SelectedItem = rowItem;

        if (DataContext is DrugIndexViewModel vm
            && vm.ToggleNoSplitCommand.CanExecute(rowItem))
        {
            vm.ToggleNoSplitCommand.Execute(rowItem);
        }
    }

}
