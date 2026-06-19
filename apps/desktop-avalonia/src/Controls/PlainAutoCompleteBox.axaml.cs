using System;
using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Controls;

public partial class PlainAutoCompleteBox : UserControl
{
    private readonly AutoCompleteBox _innerBox;
    private bool _pendingCandidateCommit;
    private TopLevel? _dropDownPointerTopLevel;
    private EventHandler<PointerPressedEventArgs>? _dropDownPointerPressedHandler;
    private EventHandler<PointerReleasedEventArgs>? _dropDownPointerReleasedHandler;

    public static readonly RoutedEvent<RoutedEventArgs> CandidateCommittedEvent =
        RoutedEvent.Register<PlainAutoCompleteBox, RoutedEventArgs>(
            nameof(CandidateCommitted),
            RoutingStrategies.Bubble);

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
        _innerBox.DropDownOpened += OnInnerDropDownOpened;
        _innerBox.DropDownClosed += OnInnerDropDownClosed;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
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

    public event EventHandler<RoutedEventArgs>? CandidateCommitted
    {
        add => AddHandler(CandidateCommittedEvent, value);
        remove => RemoveHandler(CandidateCommittedEvent, value);
    }

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

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _innerBox.IsDropDownOpen)
        {
            _pendingCandidateCommit = true;
        }
    }

    private void OnInnerDropDownOpened(object? sender, EventArgs e)
    {
        HookDropDownPointerHandlers();
    }

    private void HookDropDownPointerHandlers()
    {
        UnhookDropDownPointerHandlers();

        _dropDownPointerTopLevel = TopLevel.GetTopLevel(this);
        if (_dropDownPointerTopLevel is null)
        {
            return;
        }

        _dropDownPointerPressedHandler ??= OnTopLevelPointerPressed;
        _dropDownPointerReleasedHandler ??= OnTopLevelPointerReleased;
        _dropDownPointerTopLevel.AddHandler(
            PointerPressedEvent,
            _dropDownPointerPressedHandler,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        _dropDownPointerTopLevel.AddHandler(
            PointerReleasedEvent,
            _dropDownPointerReleasedHandler,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private void OnInnerDropDownClosed(object? sender, EventArgs e)
    {
        UnhookDropDownPointerHandlers();

        if (!_pendingCandidateCommit)
        {
            return;
        }

        _pendingCandidateCommit = false;
        Dispatcher.UIThread.Post(
            () => RaiseEvent(new RoutedEventArgs(CandidateCommittedEvent, this)),
            DispatcherPriority.Loaded);
    }

    private void UnhookDropDownPointerHandlers()
    {
        if (_dropDownPointerTopLevel is null)
        {
            return;
        }

        if (_dropDownPointerPressedHandler is not null)
        {
            _dropDownPointerTopLevel.RemoveHandler(PointerPressedEvent, _dropDownPointerPressedHandler);
        }

        if (_dropDownPointerReleasedHandler is not null)
        {
            _dropDownPointerTopLevel.RemoveHandler(PointerReleasedEvent, _dropDownPointerReleasedHandler);
        }

        _dropDownPointerTopLevel = null;
    }

    private void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_innerBox.IsDropDownOpen)
        {
            return;
        }

        if (IsInsideOpenDropDownSurface(e.Source))
        {
            _pendingCandidateCommit = true;
        }
    }

    private void OnTopLevelPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        if (IsInsideOpenDropDownSurface(e.Source))
        {
            _pendingCandidateCommit = true;
        }
    }

    private static bool IsInsideOpenDropDownSurface(object? source)
    {
        if (source is not Visual visual)
        {
            return false;
        }

        foreach (var ancestor in visual.GetVisualAncestors())
        {
            if (ancestor is PopupRoot or OverlayPopupHost)
            {
                return true;
            }

            if (ancestor is ListBoxItem or ComboBoxItem or TreeViewItem)
            {
                return true;
            }
        }

        return false;
    }
}
