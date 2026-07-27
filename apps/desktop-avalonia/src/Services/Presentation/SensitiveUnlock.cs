using System;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation;

/// <summary>
/// 敏感操作解锁服务
///
/// 协调解锁会话与视图状态通知，不定义权限策略
/// </summary>
public sealed class SensitiveUnlockService : ISensitiveUnlockService
{
    private readonly SensitiveUnlockSession _session;
    private readonly IDbConfigService _dbConfig;
    private readonly IDialogService _dialog;
    private readonly IToastService _toast;

    public SensitiveUnlockService(
        SensitiveUnlockSession session,
        IDbConfigService dbConfig,
        IDialogService dialog,
        IToastService toast)
    {
        _session = session;
        _dbConfig = dbConfig;
        _dialog = dialog;
        _toast = toast;
    }

    public event Action<string>? StateChanged;

    public UnlockScopeSnapshot GetSnapshot(string scopeKey)
        => _session.GetSnapshot(scopeKey);

    public void Refresh(string scopeKey)
    {
        var key = SensitiveUnlockSession.NormalizeScope(scopeKey);
        if (_session.Refresh(key, DateTimeOffset.UtcNow))
        {
            RaiseStateChanged(key);
        }
    }

    public void Lock(string scopeKey)
    {
        var key = SensitiveUnlockSession.NormalizeScope(scopeKey);
        if (_session.Lock(key))
        {
            RaiseStateChanged(key);
        }
    }

    public async Task<bool> RequireUnlockAsync(
        string scopeKey,
        string scene,
        string promptTitle,
        string promptHint,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var access = _session.CheckAccess(scopeKey, now);
        if (access.StateChanged)
        {
            RaiseStateChanged(access.ScopeKey);
        }

        if (access.IsGranted)
        {
            return true;
        }

        if (access.IsPromptActive)
        {
            return false;
        }

        var expectedPassword = NormalizeInput(_dbConfig.Current.Password);
        if (string.IsNullOrWhiteSpace(expectedPassword))
        {
            _toast.Error(scene, "当前未配置数据库密码，无法执行该操作");
            return false;
        }

        var prompt = _session.BeginPrompt(access.ScopeKey, DateTimeOffset.UtcNow);
        if (prompt.StateChanged)
        {
            RaiseStateChanged(prompt.ScopeKey);
        }

        if (prompt.Status is SensitiveUnlockSession.PromptStatus.Granted)
        {
            return true;
        }

        if (prompt.Status is SensitiveUnlockSession.PromptStatus.Active)
        {
            return false;
        }

        if (prompt.Status is SensitiveUnlockSession.PromptStatus.CoolingDown)
        {
            var left = prompt.CooldownUntilUtc - DateTimeOffset.UtcNow;
            _toast.Warn(scene, $"验证冷却中，请在 {Math.Max(1, (int)Math.Ceiling(left.TotalSeconds))} 秒后重试");
            return false;
        }

        string? input;
        try
        {
            var suffix = prompt.FailedAttempts <= 0
                ? promptHint
                : $"{promptHint}\n（已失败 {prompt.FailedAttempts} 次）";
            input = await _dialog.PromptUnlockPassword(
                promptTitle,
                suffix,
                password => VerifyPassword(prompt.ScopeKey, password, expectedPassword)).ConfigureAwait(true);
        }
        finally
        {
            _session.EndPrompt(prompt.ScopeKey);
        }

        if (ct.IsCancellationRequested || string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        Refresh(prompt.ScopeKey);
        if (!GetSnapshot(prompt.ScopeKey).IsUnlocked)
        {
            return false;
        }

        RaiseStateChanged(prompt.ScopeKey);
        return true;
    }

    public Task<bool> RequestUnlockAsync(SensitiveOpRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RequireUnlockAsync(
            request.ScopeKey,
            request.Scene,
            request.PromptTitle,
            request.PromptHint,
            ct);
    }

    private string? VerifyPassword(string key, string password, string expectedPassword)
    {
        var result = _session.Validate(key, password, expectedPassword, DateTimeOffset.UtcNow);
        if (result.StateChanged && !result.IsSuccess)
        {
            RaiseStateChanged(result.ScopeKey);
        }

        return result.Error;
    }

    private void RaiseStateChanged(string scopeKey)
    {
        var handler = StateChanged;
        if (handler is null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action<string>)subscriber).Invoke(scopeKey);
            }
            catch
            {
                // 单个 View 订阅失败不得中断其余监听方的解锁流程
            }
        }
    }

    private static string? NormalizeInput(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
