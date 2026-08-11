using System;
using global::Avalonia.Threading;

namespace PacToolkits.Desktop.Avalonia.Ui.State;

/// <summary>滚动日期范围（近 N 天等）控制器</summary>
public sealed class RollingDateRangeController : IDisposable
{
    private readonly DispatcherTimer _timer = new();
    private readonly Action _onDayChanged;
    private DateOnly _lastDate = DateOnly.FromDateTime(DateTime.Today);

    public RollingDateRangeController(Action onDayChanged)
    {
        _onDayChanged = onDayChanged ?? throw new ArgumentNullException(nameof(onDayChanged));
        _timer.Tick += OnTick;
        ScheduleNextTick();
        _timer.Start();
    }

    public static DateTime DefaultFromDate => DateTime.Today.AddDays(-6).Date;
    public static DateTime DefaultToDate => DateTime.Today.Date;

    public static (DateTime From, DateTime To) Normalize(DateTime? fromDate, DateTime? toDate)
    {
        var today = DateTime.Today;
        var from = (fromDate ?? DefaultFromDate).Date;
        var to = (toDate ?? DefaultToDate).Date;

        if (from > today)
        {
            from = today;
        }

        if (to > today)
        {
            to = today;
        }

        if (from > to)
        {
            from = to;
        }

        return (from, to);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (today != _lastDate)
        {
            _lastDate = today;
            _onDayChanged();
        }

        ScheduleNextTick();
    }

    private void ScheduleNextTick()
    {
        var now = DateTime.Now;
        var nextMidnight = DateTime.Today.AddDays(1).AddSeconds(1);
        var interval = nextMidnight - now;
        if (interval < TimeSpan.FromSeconds(1))
        {
            interval = TimeSpan.FromSeconds(1);
        }

        _timer.Interval = interval;
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
