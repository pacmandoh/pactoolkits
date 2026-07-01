using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public partial class Settings : AppPageBase, ISettingsPage
{
    [RelayCommand]
    private Task SaveMsfxApiConfigAsync() => ApplyMsfxApiConfigAsync();

    private async Task<bool> ApplyMsfxApiConfigAsync()
    {
        if (SkipTrigger())
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(MsfxAppKey))
        {
            _toast.Error("码上放心 API", "AppKey 不能为空");
            return false;
        }

        if (string.IsNullOrWhiteSpace(MsfxAppSecret))
        {
            _toast.Error("码上放心 API", "AppSecret 不能为空");
            return false;
        }

        try
        {
            var gateway = string.IsNullOrWhiteSpace(MsfxGatewayUrl)
                ? MsfxDefaultGatewayUrl
                : MsfxGatewayUrl.Trim();
            var appKey = MsfxAppKey.Trim();
            var appSecret = MsfxAppSecret.Trim();
            var sessionToken = (MsfxSessionToken ?? string.Empty).Trim();
            var refEntId = (MsfxRefEntId ?? string.Empty).Trim();
            var timeoutSeconds = Math.Clamp(MsfxTimeoutSeconds, 3, 120);

            await _appConfigStore.UpdateAsync(cfg =>
            {
                cfg.MsfxApi = new MsfxApiOptions
                {
                    GatewayUrl = gateway,
                    AppKey = appKey,
                    AppSecret = appSecret,
                    SessionToken = sessionToken,
                    RefEntId = refEntId,
                    DefaultMethod = "alibaba.alihealth.drugtrace.top.yljg.listupout",
                    TimeoutSeconds = timeoutSeconds
                };
            });

            var saved = _appConfigStore.Load().MsfxApi ?? new MsfxApiOptions();
            await RunOnUiAsync(() =>
            {
                MsfxGatewayUrl = string.Equals(saved.GatewayUrl, MsfxDefaultGatewayUrl, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : saved.GatewayUrl;
                MsfxAppKey = saved.AppKey;
                MsfxAppSecret = saved.AppSecret;
                MsfxSessionToken = saved.SessionToken;
                MsfxRefEntId = saved.RefEntId;
                MsfxTimeoutSeconds = saved.TimeoutSeconds;
                RefreshMsfxBadge(saved);
            });

            _toast.Success("码上放心 API", "配置已保存");
            RefreshUnsaved();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "msfx.settings.save.fail", "Failed to save msfx api settings", ex);
            _toast.Error("码上放心 API", $"保存失败：{ex.Message}");
            return false;
        }
    }

    private void RefreshMsfxBadge(MsfxApiOptions options)
    {
        var hasKey = !string.IsNullOrWhiteSpace(options.AppKey);
        var hasSecret = !string.IsNullOrWhiteSpace(options.AppSecret);
        var hasEnt = !string.IsNullOrWhiteSpace(options.RefEntId);
        var hasToken = !string.IsNullOrWhiteSpace(options.SessionToken);
        var hasRequired = hasKey || hasSecret || hasEnt;

        if (!hasRequired)
        {
            MsfxApiBadgeStatus = null;
            MsfxApiBadgeLabel = "未配置";
            return;
        }

        if (hasKey && hasSecret && hasEnt && hasToken)
        {
            MsfxApiBadgeStatus = false;
            MsfxApiBadgeLabel = "已就绪";
            return;
        }

        if (hasKey && hasSecret && hasEnt)
        {
            MsfxApiBadgeStatus = false;
            MsfxApiBadgeLabel = "已就绪";
            return;
        }

        MsfxApiBadgeStatus = true;
        MsfxApiBadgeLabel = "待完善";
    }

    private MsfxApiOptions BuildMsfxOptionsFromUi()
    {
        var gateway = string.IsNullOrWhiteSpace(MsfxGatewayUrl)
            ? MsfxDefaultGatewayUrl
            : MsfxGatewayUrl.Trim();
        return new MsfxApiOptions
        {
            GatewayUrl = gateway,
            AppKey = (MsfxAppKey ?? string.Empty).Trim(),
            AppSecret = (MsfxAppSecret ?? string.Empty).Trim(),
            SessionToken = (MsfxSessionToken ?? string.Empty).Trim(),
            RefEntId = (MsfxRefEntId ?? string.Empty).Trim(),
            DefaultMethod = "alibaba.alihealth.drugtrace.top.yljg.listupout",
            TimeoutSeconds = Math.Clamp(MsfxTimeoutSeconds, 3, 120)
        };
    }


    private async Task SaveUiBehaviorImmediateAsync(bool value, CancellationToken ct)
    {
        try
        {
            await _uiBehavior.SaveAsync(new UiBehaviorOptions
            {
                MinimizeToTrayOnClose = value
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "desktop_behavior.save.fail", "Failed to save desktop behavior", ex);
            await RunOnUiAsync(() =>
            {
                _syncingUiBehavior = true;
                MinimizeToTrayOnClose = _uiBehavior.Current.MinimizeToTrayOnClose;
                _syncingUiBehavior = false;
                _toast.Error("界面行为保存失败", ex.Message);
            });
        }
    }

}
