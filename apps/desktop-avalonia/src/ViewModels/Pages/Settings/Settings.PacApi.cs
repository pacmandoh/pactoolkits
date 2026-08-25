using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Application.Serialization;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;
using PacToolkits.Desktop.Avalonia.Services.Presentation.Connectivity;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public partial class Settings
{
    private void SyncPacApi()
    {
        var options = _pacApi.CaptureOptions();
        PacApiUrl = options.BaseUrl;
        PacApiKey = options.ApiKey;
        PacApiAgentsKey = options.AgentsApiKey;
        _pacApiBaseline = ClonePacApi(options);
    }

    private void OnPacApiAvailabilityChanged()
        => PostUi(SyncPacApiInfo, "pacapi.info.ui_fail");

    private void SyncPacApiInfo()
    {
        var configured = _apiAvailability.IsConfigured;
        var snap = _apiAvailability.Current;
        if (!configured)
        {
            PacApiInfoLabel = "未配置";
            IsPacApiInfoUnknown = true;
            IsPacApiInfoReady = false;
            IsPacApiInfoFail = false;
            PacApiVersion = "--";
            PacApiContract = "--";
            PacApiContractRange = FormatContractRange();
            PacApiDatabase = "--";
            PacApiSchema = "--";
            PacApiSchemaVersion = "--";
            PacApiCheckedAt = "--";
            PacApiDetail = "--";
            return;
        }

        if (!snap.FirstCheckCompleted)
        {
            PacApiInfoLabel = "探测中";
            IsPacApiInfoUnknown = true;
            IsPacApiInfoReady = false;
            IsPacApiInfoFail = false;
        }
        else if (snap.State is ApiAvailabilityState.Ready)
        {
            PacApiInfoLabel = "已连接";
            IsPacApiInfoUnknown = false;
            IsPacApiInfoReady = true;
            IsPacApiInfoFail = false;
        }
        else
        {
            PacApiInfoLabel = snap.State switch
            {
                ApiAvailabilityState.ContractBlocked => "协议不兼容",
                ApiAvailabilityState.SchemaBlocked => "结构不兼容",
                ApiAvailabilityState.ServerDatabaseBlocked => "数据库不可用",
                _ => "不可用"
            };
            IsPacApiInfoUnknown = false;
            IsPacApiInfoReady = false;
            IsPacApiInfoFail = true;
        }

        PacApiVersion = Dash(_apiAvailability.LastApiVersion);
        PacApiContract = Dash(_apiAvailability.LastContractVersion);
        PacApiContractRange = FormatContractRange();
        PacApiDatabase = FormatDatabase(_apiAvailability.LastDatabase);
        PacApiSchema = FormatSchema(_apiAvailability.LastSchema);
        PacApiSchemaVersion = Dash(_apiAvailability.LastSchemaVersion);
        PacApiCheckedAt = snap.CheckedAt == DateTimeOffset.MinValue
            ? "--"
            : snap.CheckedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        PacApiDetail = string.IsNullOrWhiteSpace(snap.Detail) ? "--" : snap.Detail.Trim();
    }

    private string FormatContractRange()
    {
        var ver = _releaseVersion.Current;
        var min = ver.MinApiContract;
        var max = ver.MaxApiContract;
        if (string.IsNullOrWhiteSpace(min)
            || string.IsNullOrWhiteSpace(max)
            || string.Equals(min, "unknown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(max, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "--";
        }

        return $"{min.Trim()} – {max.Trim()}";
    }

    private static string FormatDatabase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "--";
        }

        return string.Equals(value.Trim(), "ok", StringComparison.OrdinalIgnoreCase)
            ? "可用"
            : "不可用";
    }

    private static string FormatSchema(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "--";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "ok" => "兼容",
            "incompatible" => "不兼容",
            "metadata_missing" => "缺少元数据",
            "unavailable" => "无法读取",
            "skipped" => "未检查",
            var raw => raw
        };
    }

    private static string Dash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();

    [RelayCommand]
    private Task SavePacApiConfigAsync() => ApplyPacApiConfigAsync();

    [RelayCommand]
    private async Task TestPacApiAsync()
    {
        if (SkipTrigger() || IsPacApiBusy)
        {
            return;
        }

        var draft = BuildPacApiFromUi();
        var validated = PacApiOptions.Validate(draft);
        if (validated.Failed)
        {
            _toast.Error("PacAPI 服务", string.Join("；", validated.Failures ?? ["配置无效"]));
            return;
        }

        if (!draft.IsConfigured)
        {
            _toast.Warn("PacAPI 服务", "请先填写地址与密钥");
            return;
        }

        await RunOnUiAsync(() => IsPacApiBusy = true);
        try
        {
            using var client = new PacApiClient(draft.BaseUrl, draft.ApiKey, _logger, draft.HeaderName);
            using var cts = CreatePageOperationCts(TimeSpan.FromSeconds(8));

            // 与 Shell 探测同口径：info 协议 + status（200/503 诊断）
            var info = await client.GetAvailabilityJsonAsync(
                    () => new HttpRequestMessage(HttpMethod.Get, client.Resolve("/v1/system/info")),
                    PacJsonContext.Default.PacApiSystemInfo,
                    cts.Token)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("连接失败：空协议响应");

            var ver = _releaseVersion.Current;
            var min = ver.MinApiContract;
            var max = ver.MaxApiContract;
            if (string.IsNullOrWhiteSpace(min)
                || string.IsNullOrWhiteSpace(max)
                || string.Equals(min, "unknown", StringComparison.OrdinalIgnoreCase)
                || string.Equals(max, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                await RunOnUiAsync(() =>
                    _toast.Warn("PacAPI 服务", "客户端缺少 PacAPI 服务协议版本范围，请更新客户端"));
                return;
            }

            var contractBlock = PacApiContractGate.ClassifyBlockReason(info.ContractVersion, min, max);
            if (contractBlock is not null)
            {
                await RunOnUiAsync(() => _toast.Warn("PacAPI 服务", contractBlock));
                return;
            }

            using var response = await client.SendAvailabilityAsync(
                    () => new HttpRequestMessage(HttpMethod.Get, client.Resolve("/v1/system/status")),
                    cts.Token)
                .ConfigureAwait(false);

            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable))
            {
                await PacApiClient.EnsureSuccessAsync(response, TimeProvider.System, _logger, cts.Token)
                    .ConfigureAwait(false);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            var status = await System.Text.Json.JsonSerializer.DeserializeAsync(
                    stream,
                    PacJsonContext.Default.PacApiSystemStatus,
                    cts.Token)
                .ConfigureAwait(false);

            if (status is null)
            {
                await RunOnUiAsync(() => _toast.Error("PacAPI 服务", "连接失败：空响应"));
                return;
            }

            var state = ApiAvailabilityService.ClassifyStatus(status);
            if (state is ApiAvailabilityState.Ready)
            {
                await RunOnUiAsync(() => _toast.Success("PacAPI 服务", "连接成功"));
                return;
            }

            var detail = ApiAvailabilityService.DescribeServerDatabase(status);
            await RunOnUiAsync(() => _toast.Warn("PacAPI 服务", detail));
        }
        catch (OperationCanceledException) when (IsPageWorkCancellation())
        {
        }
        catch (Exception ex)
        {
            _logger.Warn("SettingsVM", "pacapi.test.fail", "PacApi connection test failed", ex);
            var detail = ApiAvailabilityService.DescribeUserFacing(ex);
            await RunOnUiAsync(() => _toast.Error("PacAPI 服务", detail));
        }
        finally
        {
            await RunOnUiAsync(() => IsPacApiBusy = false);
        }
    }

    private async Task<bool> ApplyPacApiConfigAsync()
    {
        if (SkipTrigger())
        {
            return false;
        }

        var draft = BuildPacApiFromUi();
        var validated = PacApiOptions.Validate(draft);
        if (validated.Failed)
        {
            _toast.Error("PacAPI 服务", string.Join("；", validated.Failures ?? ["配置无效"]));
            return false;
        }

        try
        {
            var beforeKind = ConnectionView.From(_apiAvailability.Current, _apiAvailability.IsConfigured).Kind;

            await _appConfigStore.UpdateAsync(cfg =>
            {
                cfg.PacApi = ClonePacApi(draft);
            });

            _pacApi.Apply(draft);
            _pacApiContractGate.Reset();
            _changeWatermark.Reset();
            _apiAvailability.Reset();

            await _apiAvailability.ProbeAsync(_pageWorkCts.Token).ConfigureAwait(false);

            if (!draft.IsConfigured)
            {
                await RunOnUiAsync(() =>
                {
                    SyncPacApi();
                    SyncPacApiInfo();
                });
                RefreshUnsaved();
                return true;
            }

            var snap = _apiAvailability.Current;
            var ready = ConnectionView.IsReady(snap, isConfigured: true);

            await RunOnUiAsync(() =>
            {
                SyncPacApi();
                SyncPacApiInfo();
                if (ready)
                {
                    if (beforeKind is not (ConnectionKind.Down or ConnectionKind.Blocked))
                    {
                        _toast.Success("PacAPI 服务", "配置已保存并应用");
                    }
                }
                else
                {
                    var detail = string.IsNullOrWhiteSpace(snap.Detail)
                        ? "配置已写入，但连接失败"
                        : snap.Detail;
                    _toast.Error("PacAPI 服务", detail);
                }
            });

            RefreshUnsaved();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "pacapi.settings.save.fail", "Failed to save PacApi settings", ex);
            _toast.Error(
                "PacAPI 服务",
                $"保存失败：{ApiAvailabilityService.DescribeUserFacing(ex, ex.Message)}");
            return false;
        }
    }

    private PacApiOptions BuildPacApiFromUi()
        => new()
        {
            BaseUrl = (PacApiUrl ?? string.Empty).Trim().TrimEnd('/'),
            ApiKey = (PacApiKey ?? string.Empty).Trim(),
            AgentsApiKey = (PacApiAgentsKey ?? string.Empty).Trim(),
            HeaderName = "X-Api-Key",
        };

    private static PacApiOptions ClonePacApi(PacApiOptions source)
        => new()
        {
            BaseUrl = source.BaseUrl ?? string.Empty,
            ApiKey = source.ApiKey ?? string.Empty,
            AgentsApiKey = source.AgentsApiKey ?? string.Empty,
            HeaderName = string.IsNullOrWhiteSpace(source.HeaderName) ? "X-Api-Key" : source.HeaderName,
        };

    private bool IsPacApiDirty()
        => !PacApiEqual(BuildPacApiFromUi(), _pacApiBaseline);

    private static bool PacApiEqual(PacApiOptions left, PacApiOptions right)
        => string.Equals(left.BaseUrl, right.BaseUrl, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.ApiKey, right.ApiKey, StringComparison.Ordinal)
           && string.Equals(left.AgentsApiKey, right.AgentsApiKey, StringComparison.Ordinal)
           && string.Equals(
               string.IsNullOrWhiteSpace(left.HeaderName) ? "X-Api-Key" : left.HeaderName,
               string.IsNullOrWhiteSpace(right.HeaderName) ? "X-Api-Key" : right.HeaderName,
               StringComparison.OrdinalIgnoreCase);
}
