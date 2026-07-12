using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Tests;

public sealed class SensitiveUnlockSessionTests
{
    private static readonly TimeSpan SessionDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(1);

    [Fact]
    public void Successful_validation_unlocks_and_access_extends_session()
    {
        var session = CreateSession();
        var now = DateTimeOffset.Parse("2026-07-12T00:00:00Z");

        var validation = session.Validate("inventory", " secret ", "secret", now);
        var access = session.CheckAccess("inventory", now.AddMinutes(5));

        Assert.True(validation.IsSuccess);
        Assert.True(access.IsGranted);
        Assert.Equal(now.AddMinutes(20), session.GetSnapshot("inventory").ExpiresAtUtc);
    }

    [Fact]
    public void Repeated_failures_start_cooldown_and_reset_attempt_count()
    {
        var session = CreateSession();
        var now = DateTimeOffset.Parse("2026-07-12T00:00:00Z");

        SensitiveUnlockSession.Validation result = default;
        for (var i = 0; i < 5; i++)
        {
            result = session.Validate("inventory", "wrong", "secret", now);
        }

        var snapshot = session.GetSnapshot("inventory");
        Assert.False(result.IsSuccess);
        Assert.Contains("已锁定 60 秒", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, snapshot.FailedAttempts);
        Assert.Equal(now.AddMinutes(1), snapshot.CooldownUntilUtc);
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

        session.Validate("inventory", "secret", "secret", now);
        var prompt = session.BeginPrompt("inventory", now);

        Assert.Equal(SensitiveUnlockSession.PromptStatus.Granted, prompt.Status);
        Assert.True(session.GetSnapshot("inventory").IsUnlocked);
    }

    [Fact]
    public void Refresh_expires_unlock_at_session_deadline()
    {
        var session = CreateSession();
        var now = DateTimeOffset.Parse("2026-07-12T00:00:00Z");
        session.Validate("inventory", "secret", "secret", now);

        var changed = session.Refresh("inventory", now + SessionDuration);

        Assert.True(changed);
        Assert.False(session.GetSnapshot("inventory").IsUnlocked);
    }

    private static SensitiveUnlockSession CreateSession()
        => new(SessionDuration, CooldownDuration, failedAttemptThreshold: 5);
}
