using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Tests;

public sealed class SensitiveUnlockSessionTests
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(1);

    [Fact]
    public void Successful_grant_unlocks_and_access_extends_idle()
    {
        var session = CreateSession();
        var now = DateTimeOffset.Parse("2026-07-12T00:00:00Z");

        var validation = session.Grant("inventory", now);
        var access = session.CheckAccess("inventory", now.AddMinutes(5));

        Assert.True(validation.IsSuccess);
        Assert.True(access.IsGranted);
        Assert.Equal(now.AddMinutes(20), session.GetSnapshot("inventory").ExpiresAtUtc);
    }

    [Fact]
    public void Note_activity_extends_all_unlocked_scopes()
    {
        var session = CreateSession();
        var now = DateTimeOffset.Parse("2026-07-12T00:00:00Z");
        session.Grant("inventory", now);
        session.Grant("msfx", now);

        session.NoteActivity(now.AddMinutes(10));

        Assert.Equal(now.AddMinutes(25), session.GetSnapshot("inventory").ExpiresAtUtc);
        Assert.Equal(now.AddMinutes(25), session.GetSnapshot("msfx").ExpiresAtUtc);
    }

    [Fact]
    public void Idle_expires_only_after_timeout_without_activity()
    {
        var session = CreateSession();
        var now = DateTimeOffset.Parse("2026-07-12T00:00:00Z");
        session.Grant("inventory", now);
        session.NoteActivity(now.AddMinutes(10));

        Assert.False(session.Refresh("inventory", now.AddMinutes(24)));
        Assert.True(session.GetSnapshot("inventory").IsUnlocked);

        Assert.True(session.Refresh("inventory", now.AddMinutes(25)));
        Assert.False(session.GetSnapshot("inventory").IsUnlocked);
    }

    [Fact]
    public void Note_activity_does_not_renew_after_idle_deadline()
    {
        var session = CreateSession();
        var now = DateTimeOffset.Parse("2026-07-12T00:00:00Z");
        session.Grant("inventory", now);

        session.NoteActivity(now + IdleTimeout);

        var snapshot = session.GetSnapshot("inventory");
        Assert.True(snapshot.IsUnlocked);
        Assert.Equal(now + IdleTimeout, snapshot.ExpiresAtUtc);
    }

    [Fact]
    public void Repeated_failures_start_cooldown_and_reset_attempt_count()
    {
        var session = CreateSession();
        var now = DateTimeOffset.Parse("2026-07-12T00:00:00Z");

        SensitiveUnlockSession.Validation result = default;
        for (var i = 0; i < 5; i++)
        {
            result = session.RecordFailure("inventory", now);
        }

        var snapshot = session.GetSnapshot("inventory");
        Assert.False(result.IsSuccess);
        Assert.Contains("已锁定 60 秒", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, snapshot.FailedAttempts);
        Assert.Equal(now.AddMinutes(1), snapshot.CooldownUntilUtc);
        Assert.Equal("验证冷却中，请在 60 秒后重试", session.GetCooldownError("inventory", now));
    }

    [Fact]
    public void Begin_prompt_rechecks_active_gate_for_racing_callers()
    {
        var session = CreateSession();
        var now = DateTimeOffset.Parse("2026-07-12T00:00:00Z");

        Assert.False(session.CheckAccess("inventory", now).IsPromptActive);
        Assert.False(session.CheckAccess("inventory", now).IsPromptActive);

        var first = session.BeginPrompt("inventory", now);
        var second = session.BeginPrompt("inventory", now);

        Assert.Equal(SensitiveUnlockSession.PromptStatus.Started, first.Status);
        Assert.Equal(SensitiveUnlockSession.PromptStatus.Active, second.Status);
    }

    [Fact]
    public void Begin_prompt_rechecks_unlock_completed_by_racing_caller()
    {
        var session = CreateSession();
        var now = DateTimeOffset.Parse("2026-07-12T00:00:00Z");
        Assert.False(session.CheckAccess("inventory", now).IsGranted);

        session.Grant("inventory", now);
        var prompt = session.BeginPrompt("inventory", now);

        Assert.Equal(SensitiveUnlockSession.PromptStatus.Granted, prompt.Status);
        Assert.True(session.GetSnapshot("inventory").IsUnlocked);
    }

    [Fact]
    public void Refresh_expires_unlock_at_idle_deadline()
    {
        var session = CreateSession();
        var now = DateTimeOffset.Parse("2026-07-12T00:00:00Z");
        session.Grant("inventory", now);

        var changed = session.Refresh("inventory", now + IdleTimeout);

        Assert.True(changed);
        Assert.False(session.GetSnapshot("inventory").IsUnlocked);
    }

    [Fact]
    public void Grant_during_cooldown_is_rejected()
    {
        var session = CreateSession();
        var now = DateTimeOffset.Parse("2026-07-12T00:00:00Z");
        for (var i = 0; i < 5; i++)
        {
            session.RecordFailure("inventory", now);
        }

        var granted = session.Grant("inventory", now);

        Assert.False(granted.IsSuccess);
        Assert.False(session.GetSnapshot("inventory").IsUnlocked);
        Assert.Contains("验证冷却中", granted.Error, StringComparison.Ordinal);
    }

    private static SensitiveUnlockSession CreateSession()
        => new(IdleTimeout, CooldownDuration, failedAttemptThreshold: 5);
}
