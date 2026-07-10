using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Controls;

public partial class TraceCodeEditor : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<TraceCodeEditor, string?>(
            nameof(Text),
            defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<IReadOnlyList<TraceCodeLineKind>?> LineKindsProperty =
        AvaloniaProperty.Register<TraceCodeEditor, IReadOnlyList<TraceCodeLineKind>?>(nameof(LineKinds));

    public static readonly StyledProperty<TraceCodeHighlightFilter> HighlightFilterProperty =
        AvaloniaProperty.Register<TraceCodeEditor, TraceCodeHighlightFilter>(nameof(HighlightFilter));

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<TraceCodeEditor, string?>(nameof(PlaceholderText));

    private bool _syncingText;
    private Control? _textPresenter;
    private bool _layoutQueued;

    public TraceCodeEditor()
    {
        InitializeComponent();
        Editor.TextChanged += OnEditorTextChanged;
        Editor.GotFocus += OnEditorGotFocus;
        Editor.LayoutUpdated += OnEditorLayoutUpdated;
        Editor.TemplateApplied += OnEditorTemplateApplied;
        ActualThemeVariantChanged += (_, _) => QueueHighlightRebuild();
    }

    public event EventHandler<RoutedEventArgs>? EditorGotFocus;

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IReadOnlyList<TraceCodeLineKind>? LineKinds
    {
        get => GetValue(LineKindsProperty);
        set => SetValue(LineKindsProperty, value);
    }

    public TraceCodeHighlightFilter HighlightFilter
    {
        get => GetValue(HighlightFilterProperty);
        set => SetValue(HighlightFilterProperty, value);
    }

    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            SyncEditorText(change.GetNewValue<string?>());
            QueueHighlightRebuild();
            return;
        }

        if (change.Property == LineKindsProperty || change.Property == HighlightFilterProperty)
        {
            UpdateHighlightChrome();
            QueueHighlightRebuild();
            return;
        }

        if (change.Property == IsEnabledProperty)
        {
            Editor.IsEnabled = IsEnabled;
            return;
        }

        if (change.Property == PlaceholderTextProperty)
        {
            Editor.PlaceholderText = change.GetNewValue<string?>() ?? string.Empty;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SyncEditorText(Text);
        Editor.IsEnabled = IsEnabled;
        Editor.PlaceholderText = PlaceholderText ?? string.Empty;
        UpdateHighlightChrome();
        QueueHighlightRebuild();
    }

    private void OnEditorTemplateApplied(object? sender, TemplateAppliedEventArgs e)
        => _textPresenter = e.NameScope.Find<Control>("PART_TextPresenter");

    private void OnEditorLayoutUpdated(object? sender, EventArgs e)
        => QueueHighlightRebuild();

    private void SyncEditorText(string? value)
    {
        if (_syncingText || string.Equals(Editor.Text, value, StringComparison.Ordinal))
        {
            return;
        }

        _syncingText = true;
        try
        {
            Editor.Text = value ?? string.Empty;
        }
        finally
        {
            _syncingText = false;
        }
    }

    private void OnEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_syncingText)
        {
            return;
        }

        _syncingText = true;
        try
        {
            Text = Editor.Text;
        }
        finally
        {
            _syncingText = false;
        }

        QueueHighlightRebuild();
    }

    private void OnEditorGotFocus(object? sender, RoutedEventArgs e)
        => EditorGotFocus?.Invoke(this, e);

    private void UpdateHighlightChrome()
    {
        if (HighlightFilter == TraceCodeHighlightFilter.None)
        {
            Editor.ClearValue(BackgroundProperty);
            HighlightCanvas.Children.Clear();
            return;
        }

        Editor.Background = Brushes.Transparent;
    }

    private void QueueHighlightRebuild()
    {
        if (_layoutQueued)
        {
            return;
        }

        _layoutQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _layoutQueued = false;
            RebuildHighlights();
        }, DispatcherPriority.Background);
    }

    private void RebuildHighlights()
    {
        HighlightCanvas.Children.Clear();
        if (HighlightFilter == TraceCodeHighlightFilter.None)
        {
            return;
        }

        var kinds = LineKinds;
        if (kinds is null || kinds.Count == 0 || string.IsNullOrEmpty(Editor.Text))
        {
            return;
        }

        if (TryGetPresenterLayout(out var textLines) is not { } presenter)
        {
            return;
        }

        var matrix = presenter.TransformToVisual(HighlightCanvas);
        if (matrix is not { } toCanvas)
        {
            return;
        }

        var presenterWidth = presenter.Bounds.Width;
        if (presenterWidth <= 0)
        {
            return;
        }

        var lineTop = 0d;
        var lineCount = Math.Min(textLines.Count, kinds.Count);
        for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
        {
            var textLine = textLines[lineIndex];
            var kind = kinds[lineIndex];

            if (ShouldTint(kind))
            {
                AddLineHighlight(kind, lineTop, textLine.Height, presenterWidth, toCanvas);
            }

            lineTop += textLine.Height;
        }
    }

    private Control? TryGetPresenterLayout(out IReadOnlyList<TextLine> textLines)
    {
        textLines = Array.Empty<TextLine>();
        if (_textPresenter is null)
        {
            return null;
        }

        if (_textPresenter.GetType().GetProperty(
                "TextLayout",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(_textPresenter) is not TextLayout layout
            || layout.TextLines.Count == 0)
        {
            return null;
        }

        textLines = layout.TextLines;
        return _textPresenter;
    }

    private void AddLineHighlight(
        TraceCodeLineKind kind,
        double lineTop,
        double slotHeight,
        double presenterWidth,
        Matrix toCanvas)
    {
        var bgOrigin = toCanvas.Transform(new Point(0, lineTop));

        var background = new Border
        {
            Width = presenterWidth,
            Height = slotHeight,
            Background = ThemeBrushResolver.GetBrush(ResolveHighlightBrush(kind), Brushes.Transparent)
        };
        Canvas.SetLeft(background, bgOrigin.X);
        Canvas.SetTop(background, bgOrigin.Y);
        HighlightCanvas.Children.Add(background);
    }

    private static string ResolveHighlightBrush(TraceCodeLineKind kind)
        => kind switch
        {
            TraceCodeLineKind.Valid => "SuccessColor10",
            TraceCodeLineKind.ScanDuplicate => "WarningColor10",
            TraceCodeLineKind.PoolDuplicate => "WarningColor10",
            TraceCodeLineKind.Invalid => "ErrorColor10",
            _ => "GhostColor"
        };

    private bool ShouldTint(TraceCodeLineKind kind)
    {
        if (kind == TraceCodeLineKind.Empty)
        {
            return false;
        }

        return HighlightFilter switch
        {
            TraceCodeHighlightFilter.All => true,
            TraceCodeHighlightFilter.Valid => kind == TraceCodeLineKind.Valid,
            TraceCodeHighlightFilter.Duplicate => kind == TraceCodeLineKind.ScanDuplicate,
            TraceCodeHighlightFilter.PoolSkip => kind == TraceCodeLineKind.PoolDuplicate,
            TraceCodeHighlightFilter.Invalid => kind == TraceCodeLineKind.Invalid,
            _ => false
        };
    }
}
