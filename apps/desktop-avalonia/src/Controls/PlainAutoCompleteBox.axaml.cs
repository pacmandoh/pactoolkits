using System;
using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Controls;

public partial class PlainAutoCompleteBox : UserControl
{
    private readonly AutoCompleteBox _innerBox;
    public static readonly StyledProperty<string?> TextProperty =
        AutoCompleteBox.TextProperty.AddOwner<PlainAutoCompleteBox>();

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AutoCompleteBox.ItemsSourceProperty.AddOwner<PlainAutoCompleteBox>();

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AutoCompleteBox.PlaceholderTextProperty.AddOwner<PlainAutoCompleteBox>();

    public static readonly StyledProperty<bool> IsDropDownOpenProperty =
        AutoCompleteBox.IsDropDownOpenProperty.AddOwner<PlainAutoCompleteBox>();

    public static readonly StyledProperty<AutoCompleteFilterMode> FilterModeProperty =
        AutoCompleteBox.FilterModeProperty.AddOwner<PlainAutoCompleteBox>();

    public static readonly StyledProperty<int> MinimumPrefixLengthProperty =
        AutoCompleteBox.MinimumPrefixLengthProperty.AddOwner<PlainAutoCompleteBox>();

    public static readonly StyledProperty<bool> IsTextCompletionEnabledProperty =
        AutoCompleteBox.IsTextCompletionEnabledProperty.AddOwner<PlainAutoCompleteBox>();

    public static readonly StyledProperty<IDataTemplate> ItemTemplateProperty =
        AutoCompleteBox.ItemTemplateProperty.AddOwner<PlainAutoCompleteBox>();

    static PlainAutoCompleteBox()
    {
        FilterModeProperty.OverrideDefaultValue<PlainAutoCompleteBox>(AutoCompleteFilterMode.Contains);
        IsTextCompletionEnabledProperty.OverrideDefaultValue<PlainAutoCompleteBox>(true);
    }

    public PlainAutoCompleteBox()
    {
        AvaloniaXamlLoader.Load(this);
        _innerBox = this.FindControl<AutoCompleteBox>("InnerBox")
                    ?? throw new InvalidOperationException("AutoCompleteBox host is not ready.");
        _innerBox.PropertyChanged += OnInnerBoxPropertyChanged;
        AddHandler(KeyDownEvent, OnInnerKeyDown, RoutingStrategies.Tunnel);
    }

    public AutoCompleteBox Box => _innerBox;

    public TextBox Input => InputFocusHelper.FindDescendant<TextBox>(Box)
                            ?? throw new InvalidOperationException("AutoCompleteBox text input is not ready.");

    public AutoCompleteFilterPredicate<object?>? ItemFilter
    {
        get => Box.ItemFilter;
        set => Box.ItemFilter = value;
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public bool IsDropDownOpen
    {
        get => GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    public AutoCompleteFilterMode FilterMode
    {
        get => GetValue(FilterModeProperty);
        set => SetValue(FilterModeProperty, value);
    }

    public int MinimumPrefixLength
    {
        get => GetValue(MinimumPrefixLengthProperty);
        set => SetValue(MinimumPrefixLengthProperty, value);
    }

    public bool IsTextCompletionEnabled
    {
        get => GetValue(IsTextCompletionEnabledProperty);
        set => SetValue(IsTextCompletionEnabledProperty, value);
    }

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value!);
    }

    public event EventHandler<AvaloniaPropertyChangedEventArgs>? BoxPropertyChanged;

    private void OnInnerBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        BoxPropertyChanged?.Invoke(this, e);
    }

    private void OnInnerKeyDown(object? sender, KeyEventArgs e)
    {
        if (!ReferenceEquals(e.Source, _innerBox))
        {
            return;
        }

        RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = KeyDownEvent,
            Key = e.Key,
            KeyModifiers = e.KeyModifiers,
            Source = this,
        });
    }
}
