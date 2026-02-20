using System;
using Avalonia;
using Avalonia.Controls;

namespace pactoolkits_ui.Controls;

public partial class UpdateStatusBadge : UserControl
{
    public static readonly StyledProperty<bool?> StatusProperty =
        AvaloniaProperty.Register<UpdateStatusBadge, bool?>(nameof(Status), defaultValue: null);

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<UpdateStatusBadge, string>(nameof(Label), "未知");

    public static readonly StyledProperty<string> LatestVersionProperty =
        AvaloniaProperty.Register<UpdateStatusBadge, string>(nameof(LatestVersion), string.Empty);

    public static readonly DirectProperty<UpdateStatusBadge, bool> IsUnknownProperty =
        AvaloniaProperty.RegisterDirect<UpdateStatusBadge, bool>(nameof(IsUnknown), o => o.IsUnknown);

    public static readonly DirectProperty<UpdateStatusBadge, bool> IsUpToDateProperty =
        AvaloniaProperty.RegisterDirect<UpdateStatusBadge, bool>(nameof(IsUpToDate), o => o.IsUpToDate);

    public static readonly DirectProperty<UpdateStatusBadge, bool> IsUpdateAvailableProperty =
        AvaloniaProperty.RegisterDirect<UpdateStatusBadge, bool>(nameof(IsUpdateAvailable), o => o.IsUpdateAvailable);

    public static readonly DirectProperty<UpdateStatusBadge, bool> ShowLatestVersionProperty =
        AvaloniaProperty.RegisterDirect<UpdateStatusBadge, bool>(nameof(ShowLatestVersion), o => o.ShowLatestVersion);

    private bool _isUnknown = true;
    private bool _isUpToDate;
    private bool _isUpdateAvailable;
    private bool _showLatestVersion;

    public bool? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string LatestVersion
    {
        get => GetValue(LatestVersionProperty);
        set => SetValue(LatestVersionProperty, value);
    }

    public bool IsUnknown => _isUnknown;
    public bool IsUpToDate => _isUpToDate;
    public bool IsUpdateAvailable => _isUpdateAvailable;
    public bool ShowLatestVersion => _showLatestVersion;

    public UpdateStatusBadge()
    {
        InitializeComponent();
        RefreshVisualState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StatusProperty || change.Property == LatestVersionProperty)
            RefreshVisualState();
    }

    private void RefreshVisualState()
    {
        var status = Status;
        var latest = (LatestVersion ?? string.Empty).Trim();
        var showVersion = status == true
                          && latest.Length > 0
                          && !string.Equals(latest, "unknown", StringComparison.OrdinalIgnoreCase);

        SetAndRaise(IsUnknownProperty, ref _isUnknown, status is null);
        SetAndRaise(IsUpToDateProperty, ref _isUpToDate, status == false);
        SetAndRaise(IsUpdateAvailableProperty, ref _isUpdateAvailable, status == true);
        SetAndRaise(ShowLatestVersionProperty, ref _showLatestVersion, showVersion);
    }
}
