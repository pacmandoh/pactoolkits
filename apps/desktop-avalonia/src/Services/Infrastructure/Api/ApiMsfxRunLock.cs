using System;
using System.Net.Http;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>当前 AutoRun 跑锁；请求带锁头，顺带续期</summary>
public sealed class ApiMsfxRunLock
{
    private readonly object _gate = new();
    private Guid? _lockId;

    public Guid? Current
    {
        get
        {
            lock (_gate)
            {
                return _lockId;
            }
        }
        set
        {
            lock (_gate)
            {
                _lockId = value;
            }
        }
    }

    public void Apply(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Current is not { } lockId)
        {
            return;
        }

        request.Headers.TryAddWithoutValidation(PacApiHeaders.MsfxRunLock, lockId.ToString("D"));
    }
}
