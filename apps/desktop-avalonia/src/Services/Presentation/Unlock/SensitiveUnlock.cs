using System;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Connectivity;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Notifications;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation.Unlock;

/// <summary>
/// 敏感操作解锁服务
///
/// 口令由 PacAPI 校验；本机只管会话（空闲超时、失败冷却、提示互斥）
/// </summary>
public sealed class SensitiveUnlockService : ISensitiveUnlockService
{
    private const string UnlockMismatch = "unlock_mismatch";
    private const string UnlockNotConfigured = "unlock_not_configured";

    private readonly SensitiveUnlockSession _session;
    private readonly PacApiClient _api;
    private readonly IDialogService _dialog;
    private readonly IToastService _toast;

    public SensitiveUnlockService(
        SensitiveUnlockSession session,
        PacApiClient api,
        IDialogService dialog,
        IToastService toast)
    {
        _session = session;
        _api = api;
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
                password => VerifyPasswordAsync(prompt.ScopeKey, password, ct)).ConfigureAwait(true);
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

    private async Task<string?> VerifyPasswordAsync(string key, string password, CancellationToken ct)
    {
        var cooldown = _session.GetCooldownError(key, DateTimeOffset.UtcNow);
        if (cooldown is not null)
        {
            return cooldown;
        }

        try
        {
            await _api.VerifyUnlockAsync(password, ct).ConfigureAwait(true);
        }
        catch (PacApiException ex) when (IsCode(ex, UnlockMismatch))
        {
            var result = _session.RecordFailure(key, DateTimeOffset.UtcNow);
            if (result.StateChanged)
            {
                RaiseStateChanged(result.ScopeKey);
            }

            return result.Error;
        }
        catch (PacApiException ex) when (IsCode(ex, UnlockNotConfigured))
        {
            return "未配置敏感操作密码";
        }
        catch (Exception ex) when (TransportErrors.IsTransport(ex))
        {
            return "无法连接 PacAPI，无法验证";
        }
        catch (PacApiException ex)
        {
            return string.IsNullOrWhiteSpace(ex.Problem.Title)
                ? "验证失败"
                : ex.Problem.Title;
        }

        var granted = _session.Grant(key, DateTimeOffset.UtcNow);
        if (!granted.IsSuccess)
        {
            return granted.Error;
        }

        if (granted.StateChanged)
        {
            RaiseStateChanged(granted.ScopeKey);
        }

        return null;
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

    private static bool IsCode(PacApiException ex, string code)
        => string.Equals(ex.Code, code, StringComparison.OrdinalIgnoreCase);
}
