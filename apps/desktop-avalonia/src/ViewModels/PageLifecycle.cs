using System;
using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

public interface IPageLifecycleAware
{
    Task OnPageActivatedAsync(CancellationToken ct = default);

    Task OnPageDeactivatedAsync(CancellationToken ct = default);

    ValueTask DisposePageAsync();
}

[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class StateRetainedAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class LongLivedAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class OwnsSubscriptionsAttribute : Attribute;
