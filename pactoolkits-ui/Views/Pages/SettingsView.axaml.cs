using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using pactoolkits_ui.ViewModels.Pages;
using pactoolkits_ui.Common;

namespace pactoolkits_ui.Views.Pages;

public partial class SettingsView : UserControl
{
    public static readonly StyledProperty<bool> IsClientAliasEditableProperty =
        AvaloniaProperty.Register<SettingsView, bool>(nameof(IsClientAliasEditable), false);

    public static readonly StyledProperty<bool> IsClientAliasReadOnlyProperty =
        AvaloniaProperty.Register<SettingsView, bool>(nameof(IsClientAliasReadOnly), true);

    public bool IsClientAliasEditable
    {
        get => GetValue(IsClientAliasEditableProperty);
        set => SetValue(IsClientAliasEditableProperty, value);
    }

    public bool IsClientAliasReadOnly
    {
        get => GetValue(IsClientAliasReadOnlyProperty);
        set => SetValue(IsClientAliasReadOnlyProperty, value);
    }

    private SettingsViewModel? _vm;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(KeyDownEvent, OnSettingsKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        TryAttach(DataContext as SettingsViewModel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        TryAttach(DataContext as SettingsViewModel);
    }

    private void TryAttach(SettingsViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm))
            return;

        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = vm;

        if (_vm is not null)
        {
            IsClientAliasReadOnly = _vm.IsClientAliasReadOnly;
            IsClientAliasEditable = !_vm.IsClientAliasReadOnly;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
        else
        {
            IsClientAliasReadOnly = true;
            IsClientAliasEditable = false;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsClientAliasReadOnly))
        {
            IsClientAliasReadOnly = _vm?.IsClientAliasReadOnly ?? true;
            IsClientAliasEditable = !IsClientAliasReadOnly;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TryAttach(DataContext as SettingsViewModel);
    }

    private void OnSettingsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab && e.Key != Key.Enter)
            return;

        var inputs = EnumerateTabInputs().ToList();
        InputFocusHelper.TryHandleTabCycle(this, e, inputs);
    }

    private IEnumerable<Control> EnumerateTabInputs() =>
        InputFocusHelper.EnumerateInputs(this, typeof(TextBox), typeof(NumericUpDown), typeof(ComboBox));

}
