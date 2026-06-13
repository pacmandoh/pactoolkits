using System;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia.Services.Integration;

public interface IMsfxApiClient
{
    Task<MsfxApiCallResult> ExecuteRawAsync(
        MsfxApiOptions options,
        string method,
        IReadOnlyDictionary<string, string?> bizParams,
        CancellationToken ct);

    Task<MsfxListUpoutResult> GetYljgListUpoutAsync(
        MsfxApiOptions options,
        MsfxListUpoutRequest request,
        CancellationToken ct);

    Task<MsfxListUpoutDetailResult> GetYljgListUpoutDetailAsync(
        MsfxApiOptions options,
        MsfxListUpoutDetailRequest request,
        CancellationToken ct);
}

public sealed class MsfxApiClient : IMsfxApiClient
{
    private static readonly HttpClient Http = new();
    private sealed record RelationBatchResult(
        Dictionary<string, HashSet<string>> ChildrenMap,
        Dictionary<string, int> CodeLevels,
        HashSet<string> LevelOneCodes);

    private const string ApiYljgListUpout = "alibaba.alihealth.drugtrace.top.yljg.listupout";
    private const string ApiYljgListUpoutDetail = "alibaba.alihealth.drugtrace.top.yljg.listupout.detail";
    private const string ApiYljgQueryRelation = "alibaba.alihealth.drugtrace.top.yljg.query.relation";

    public async Task<MsfxApiCallResult> ExecuteRawAsync(
        MsfxApiOptions options,
        string method,
        IReadOnlyDictionary<string, string?> bizParams,
        CancellationToken ct)
    {
        var gateway = (options.GatewayUrl ?? string.Empty).Trim();
        var appKey = (options.AppKey ?? string.Empty).Trim();
        var appSecret = (options.AppSecret ?? string.Empty).Trim();
        var session = (options.SessionToken ?? string.Empty).Trim();
        var methodName = (method ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(gateway))
            return Fail("网关地址不能为空");
        if (string.IsNullOrWhiteSpace(appKey))
            return Fail("AppKey 不能为空");
        if (string.IsNullOrWhiteSpace(appSecret))
            return Fail("AppSecret 不能为空");
        if (string.IsNullOrWhiteSpace(methodName))
            return Fail("接口方法名不能为空");

        var parameters = BuildSignedParameters(
            appKey: appKey,
            appSecret: appSecret,
            session: session,
            methodName: methodName,
            bizParams: bizParams);

        using var req = new HttpRequestMessage(HttpMethod.Post, gateway)
        {
            Content = new FormUrlEncodedContent(parameters)
        };

        try
        {
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var (bizCode, bizMsg, bizOk) = ParseBizStatus(body);
            var summary = resp.IsSuccessStatusCode
                ? (bizOk ? "请求成功" : $"业务失败: {bizCode} {bizMsg}".Trim())
                : $"HTTP 失败: {(int)resp.StatusCode}";
            return new MsfxApiCallResult(
                Ok: resp.IsSuccessStatusCode && bizOk,
                HttpStatusCode: (int)resp.StatusCode,
                Summary: summary,
                BizCode: bizCode,
                BizMessage: bizMsg,
                RequestId: ParseRequestId(body),
                ResponseText: body,
                RequestTrace: BuildRequestTrace(gateway, parameters));
        }
        catch (Exception ex)
        {
            return Fail($"请求异常: {ex.Message}", BuildRequestTrace(gateway, parameters));
        }
    }

    public async Task<MsfxListUpoutResult> GetYljgListUpoutAsync(
        MsfxApiOptions options,
        MsfxListUpoutRequest request,
        CancellationToken ct)
    {
        var call = await ExecuteRawAsync(options, ApiYljgListUpout, new Dictionary<string, string?>
        {
            ["ref_ent_id"] = request.RefEntId,
            ["begin_date"] = request.BeginDate,
            ["end_date"] = request.EndDate,
            ["page"] = request.Page.ToString(CultureInfo.InvariantCulture),
            ["page_size"] = request.PageSize.ToString(CultureInfo.InvariantCulture)
        }, ct).ConfigureAwait(false);

        if (!call.Ok)
            return new MsfxListUpoutResult(call, 0, Array.Empty<MsfxListUpoutItem>());

        var items = new List<MsfxListUpoutItem>();
        var total = 0L;
        try
        {
            using var doc = JsonDocument.Parse(call.ResponseText);
            var response = GetTopResponseNode(doc.RootElement);
            if (response.ValueKind == JsonValueKind.Undefined)
                return new MsfxListUpoutResult(call, 0, Array.Empty<MsfxListUpoutItem>());

            var result = GetPropertyOrDefault(response, "result");
            var model = GetPropertyOrDefault(result, "model");
            total = GetInt64(model, "total_num");
            var resultList = GetPropertyOrDefault(model, "result_list");
            if (resultList.ValueKind == JsonValueKind.Undefined)
                resultList = GetPropertyOrDefault(model, "resultList");
            if (resultList.ValueKind == JsonValueKind.Undefined)
                resultList = GetPropertyOrDefault(model, "bill_up_out_detail_do_list");
            if (resultList.ValueKind == JsonValueKind.Undefined)
                resultList = GetPropertyOrDefault(model, "rows");
            // Some responses wrap rows as: result_list.bill_up_out_detail_do[]
            if (resultList.ValueKind == JsonValueKind.Object)
            {
                var wrapped = GetPropertyOrDefault(resultList, "bill_up_out_detail_do");
                if (wrapped.ValueKind == JsonValueKind.Array)
                    resultList = wrapped;
            }
            if (resultList.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in resultList.EnumerateArray())
                {
                    items.Add(new MsfxListUpoutItem(
                        BillCode: GetString(x, "bill_code"),
                        BillType: GetString(x, "bill_type"),
                        BillTime: GetString(x, "bill_time_format"),
                        BillUploadTime: GetString(x, "bill_upload_time"),
                        PhysicName: GetString(x, "physic_name"),
                        PkgSpec: GetString(x, "pkg_spec"),
                        PrepnSpec: GetString(x, "prepn_spec"),
                        PrepnCount: GetInt64(x, "prepn_count"),
                        CodeCount: GetInt64(x, "code_count"),
                        ProduceBatchNo: GetString(x, "produce_batch_no"),
                        ExpireDate: GetString(x, "exprie_date_format"),
                        FromEntName: GetString(x, "from_ent_name"),
                        ProduceEntName: GetString(x, "produce_ent_name"),
                        FromRefUserId: GetString(x, "from_ref_user_id"),
                        ToRefUserId: GetString(x, "to_ref_user_id"),
                        ConfirmStatus: GetStringAny(x, "confirm_status", "confirm_status_desc", "confirm_status_name", "confirm_state"),
                        DrugTag: GetStringAny(x, "drug_tag", "drug_tag_desc", "drug_tag_name", "drug_type_desc"),
                        IsCollectDrugBill: GetStringAny(x, "is_collect_drug_bill", "contain_collect_drug", "contains_collect_drug"),
                        IsSpecialDrugBill: GetStringAny(x, "is_special_drug_bill", "contain_special_drug", "contains_special_drug"),
                        IsBloodProductBill: GetStringAny(x, "is_blood_product_bill", "contain_blood_product", "contains_blood_product"),
                        IsBiologicalProductBill: GetStringAny(x, "is_biological_product_bill", "contain_biological_product", "contains_biological_product"),
                        IsBotulinumBill: GetStringAny(x, "is_botulinum_toxin_bill", "contain_botulinum_toxin", "contains_botulinum_toxin"),
                        VerifyStatus: GetStringAny(x, "verify_status", "verify_status_desc", "verify_status_name", "check_status"),
                        RegulatedFlag: GetStringAny(x, "is_regulated", "is_regulated_drug", "is_china_regulated", "is_national_regulated"),
                        LogisticsStatus: GetStringAny(x, "logistics_status", "logistics_status_desc", "logistics_status_name", "status_desc"),
                        Status: GetString(x, "status")
                    ));
                }
            }
        }
        catch
        {
            // Keep call result as-is. UI still can inspect raw response text.
        }

        return new MsfxListUpoutResult(call, total, items);
    }

    public async Task<MsfxListUpoutDetailResult> GetYljgListUpoutDetailAsync(
        MsfxApiOptions options,
        MsfxListUpoutDetailRequest request,
        CancellationToken ct)
    {
        var call = await ExecuteRawAsync(options, ApiYljgListUpoutDetail, new Dictionary<string, string?>
        {
            ["ref_ent_id"] = request.RefEntId,
            ["bill_code"] = request.BillCode,
            ["to_ref_user_id"] = request.ToRefUserId,
            ["from_ref_user_id"] = request.FromRefUserId
        }, ct).ConfigureAwait(false);

        if (!call.Ok)
            return new MsfxListUpoutDetailResult(call, request.BillCode, Array.Empty<MsfxDrugDetailItem>(), Array.Empty<string>(), string.Empty);

        var drugs = new List<MsfxDrugDetailItem>();
        try
        {
            using var doc = JsonDocument.Parse(call.ResponseText);
            var response = GetTopResponseNode(doc.RootElement);
            if (response.ValueKind == JsonValueKind.Undefined)
                return new MsfxListUpoutDetailResult(call, request.BillCode, Array.Empty<MsfxDrugDetailItem>(), Array.Empty<string>(), string.Empty);

            var result = GetPropertyOrDefault(response, "result");
            var model = GetPropertyOrDefault(result, "model");
            var drugList = GetPropertyOrDefault(model, "drug_infos_dto_list");
            if (drugList.ValueKind == JsonValueKind.Object)
            {
                var wrapped = GetPropertyOrDefault(drugList, "drug_infos_dto");
                if (wrapped.ValueKind == JsonValueKind.Array)
                    drugList = wrapped;
            }
            if (drugList.ValueKind == JsonValueKind.Array)
            {
                foreach (var drug in drugList.EnumerateArray())
                {
                    var traceCodes = new List<MsfxTraceCodeItem>();
                    var codeList = GetPropertyOrDefault(drug, "code_info_list_dto_list");
                    if (codeList.ValueKind == JsonValueKind.Object)
                    {
                        var wrapped = GetPropertyOrDefault(codeList, "code_info_list_dto");
                        if (wrapped.ValueKind == JsonValueKind.Array)
                            codeList = wrapped;
                    }
                    if (codeList.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var code in codeList.EnumerateArray())
                        {
                            var codeValue = GetString(code, "code");
                            if (string.IsNullOrWhiteSpace(codeValue))
                                continue;

                            traceCodes.Add(new MsfxTraceCodeItem(
                                Code: codeValue,
                                CodeLevel: GetString(code, "code_level")));
                        }
                    }

                    drugs.Add(new MsfxDrugDetailItem(
                        PhysicName: GetString(drug, "physic_name"),
                        PackageSpec: GetString(drug, "package_spec"),
                        PrepnSpec: GetString(drug, "prepn_spec"),
                        ProduceBatchNo: GetString(drug, "produce_batch_no"),
                        TraceCodes: traceCodes));
                }
            }
        }
        catch
        {
            // Keep call result as-is. UI still can inspect raw response text.
        }

        var minimalCodeSet = new HashSet<string>(StringComparer.Ordinal);
        var normalizedDrugs = new List<MsfxDrugDetailItem>(drugs.Count);
        var relationRequestTraces = new List<string>();
        var relationErrors = new List<string>();
        foreach (var drug in drugs)
        {
            var minimalCodes = await ResolveMinimalCodesForDrugAsync(
                options,
                request.RefEntId,
                request.ToRefUserId,
                request.FromRefUserId,
                drug.TraceCodes,
                relationRequestTraces,
                relationErrors,
                ct).ConfigureAwait(false);

            foreach (var code in minimalCodes)
                minimalCodeSet.Add(code.Code);

            normalizedDrugs.Add(drug with { TraceCodes = minimalCodes });
        }

        var mergedMinimalCodes = minimalCodeSet.ToList();
        mergedMinimalCodes.Sort(StringComparer.Ordinal);
        var mergedTrace = call.RequestTrace;
        if (relationRequestTraces.Count > 0)
        {
            var sb = new StringBuilder(mergedTrace);
            sb.AppendLine();
            sb.AppendLine("---- relation drill requests ----");
            for (var i = 0; i < relationRequestTraces.Count; i++)
            {
                sb.AppendLine($"[{i + 1}]");
                sb.AppendLine(relationRequestTraces[i]);
            }

            mergedTrace = sb.ToString();
        }

        var relationErrorHint = relationErrors.Count > 0
            ? relationErrors[0]
            : string.Empty;
        var callWithTrace = call with { RequestTrace = mergedTrace };
        return new MsfxListUpoutDetailResult(callWithTrace, request.BillCode, normalizedDrugs, mergedMinimalCodes, relationErrorHint);
    }

    private async Task<IReadOnlyList<MsfxTraceCodeItem>> ResolveMinimalCodesForDrugAsync(
        MsfxApiOptions options,
        string refEntId,
        string? toRefUserId,
        string? fromRefUserId,
        IReadOnlyList<MsfxTraceCodeItem> seedCodes,
        List<string> relationRequestTraces,
        List<string> relationErrors,
        CancellationToken ct)
    {
        const string errEmptySeed = "MSFX_RELATION_EMPTY_SEED";
        const string errNoMinimal = "MSFX_RELATION_NO_MINIMAL_CODE";

        var levelOneCodes = new HashSet<string>(StringComparer.Ordinal);
        var seedSet = new HashSet<string>(StringComparer.Ordinal);
        var seedLevel = new Dictionary<string, int?>(StringComparer.Ordinal);
        var parentMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var minimalCodes = new HashSet<string>(StringComparer.Ordinal);
        var resolvedLevel = new Dictionary<string, int?>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var c in seedCodes)
        {
            if (!IsCandidateTraceCode(c.Code))
                continue;

            seedSet.Add(c.Code);
            var level = ParseLevel(c.CodeLevel);
            seedLevel[c.Code] = level;
            resolvedLevel[c.Code] = level;
            seen.Add(c.Code);
            if (level == 1)
            {
                // item 直接返回 1 级码时，直接作为最小包装码使用，不依赖 relation 再下钻
                minimalCodes.Add(c.Code);
                levelOneCodes.Add(c.Code);
            }
        }

        if (seedSet.Count == 0)
            throw new InvalidOperationException($"[{errEmptySeed}] 未找到可下钻追溯码种子");

        var frontier = seedSet
            .Where(code => !seedLevel.TryGetValue(code, out var level) || level is null || level > 1)
            .ToList();
        var maxSeedLevel = seedLevel.Values
            .Where(v => v.HasValue && v.Value > 0)
            .Select(v => v!.Value)
            .DefaultIfEmpty(0)
            .Max();
        var expectedDepth = Math.Max(1, maxSeedLevel - 1);
        // 防止无意义深层迭代导致巡检耗时放大
        var maxDepth = Math.Clamp(expectedDepth + 2, 1, 6);
        for (var depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
        {
            var next = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < frontier.Count; i += 10)
            {
                ct.ThrowIfCancellationRequested();
                var batch = frontier.Skip(i).Take(10).ToList();
                var rel = await QueryRelationChildrenBatchCompatAsync(
                    options,
                    refEntId,
                    toRefUserId,
                    fromRefUserId,
                    batch,
                    relationRequestTraces,
                    relationErrors,
                    ct).ConfigureAwait(false);

                foreach (var code in rel.LevelOneCodes)
                {
                    if (IsCandidateTraceCode(code))
                        levelOneCodes.Add(code);
                }

                foreach (var parent in batch)
                {
                    if (!rel.ChildrenMap.TryGetValue(parent, out var children) || children.Count == 0)
                        continue;

                    foreach (var child in children)
                    {
                        if (!IsCandidateTraceCode(child) || string.Equals(child, parent, StringComparison.Ordinal))
                            continue;

                        if (!parentMap.ContainsKey(child))
                            parentMap[child] = parent;

                        var parentLevel = resolvedLevel.TryGetValue(parent, out var pLv)
                            ? pLv
                            : (rel.CodeLevels.TryGetValue(parent, out var parsedParent) ? parsedParent : null);
                        var parsedChildLevel = rel.CodeLevels.TryGetValue(child, out var parsedChild)
                            ? parsedChild
                            : (int?)null;
                        var derivedChildLevel = parentLevel is > 1
                            ? parentLevel.Value - 1
                            : parsedChildLevel;
                        if (derivedChildLevel is >= 1)
                        {
                            if (!resolvedLevel.TryGetValue(child, out var existed) ||
                                !existed.HasValue ||
                                derivedChildLevel.Value < existed.Value)
                            {
                                resolvedLevel[child] = derivedChildLevel.Value;
                            }
                        }
                        else if (parsedChildLevel is >= 1 && !resolvedLevel.ContainsKey(child))
                        {
                            resolvedLevel[child] = parsedChildLevel.Value;
                        }

                        if (rel.LevelOneCodes.Contains(child) ||
                            (resolvedLevel.TryGetValue(child, out var childLevel) && childLevel == 1))
                            minimalCodes.Add(child);
                        else if (seen.Add(child))
                            next.Add(child);
                    }
                }
            }

            frontier = next.ToList();
        }

        if (minimalCodes.Count > 0)
        {
            var rows = minimalCodes
                .OrderBy(x => x, StringComparer.Ordinal)
                .Select(code =>
                {
                    var codeLevel = resolvedLevel.TryGetValue(code, out var lv) ? lv : null;
                    var (l1, l2, l3, l4, l5) = BuildHierarchy(code, parentMap, codeLevel);
                    return new MsfxTraceCodeItem(
                    Code: code,
                    CodeLevel: (codeLevel ?? 1).ToString(CultureInfo.InvariantCulture),
                    Level1Code: l1,
                    Level2Code: l2,
                    Level3Code: l3,
                    Level4Code: l4,
                    Level5Code: l5);
                })
                .ToArray();
            return rows;
        }

        if (levelOneCodes.Count > 0)
        {
            var direct = levelOneCodes
                .OrderBy(x => x, StringComparer.Ordinal)
                .Select(code => new MsfxTraceCodeItem(
                    Code: code,
                    CodeLevel: "1",
                    Level1Code: code))
                .ToArray();
            return direct;
        }

        var hint = relationErrors.Count > 0 ? relationErrors[0] : "relation 接口未返回可用层级链路";
        throw new InvalidOperationException($"[{errNoMinimal}] 无法解析最小包装码：{hint}");
    }

    private static (string? L1, string? L2, string? L3, string? L4, string? L5) BuildHierarchy(
        string leafCode,
        IReadOnlyDictionary<string, string> parentMap,
        int? leafLevel)
    {
        var chain = new List<string>(8) { leafCode };
        var current = leafCode;
        for (var i = 0; i < 16; i++)
        {
            if (!parentMap.TryGetValue(current, out var p) || string.IsNullOrWhiteSpace(p))
                break;
            chain.Add(p);
            current = p;
        }

        var l1 = (string?)null;
        var l2 = (string?)null;
        var l3 = (string?)null;
        var l4 = (string?)null;
        var l5 = (string?)null;

        var start = leafLevel is >= 1 and <= 5 ? leafLevel.Value : 1;
        for (var i = 0; i < chain.Count; i++)
        {
            var level = start + i;
            if (level > 5)
                break;

            var code = chain[i];
            switch (level)
            {
                case 1: l1 = code; break;
                case 2: l2 = code; break;
                case 3: l3 = code; break;
                case 4: l4 = code; break;
                case 5: l5 = code; break;
            }
        }

        return (l1, l2, l3, l4, l5);
    }

    private async Task<RelationBatchResult> QueryRelationChildrenBatchCompatAsync(
        MsfxApiOptions options,
        string refEntId,
        string? toRefUserId,
        string? fromRefUserId,
        IReadOnlyList<string> codes,
        List<string> relationRequestTraces,
        List<string> relationErrors,
        CancellationToken ct)
    {
        if (codes.Count == 0)
        {
            return new RelationBatchResult(
                new Dictionary<string, HashSet<string>>(StringComparer.Ordinal),
                new Dictionary<string, int>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));
        }

        // query.relation: align with verified API-tool request template.
        var des = refEntId;
        var joined = string.Join(",", codes);
        var bizParams = new Dictionary<string, string?>
        {
            ["ref_ent_id"] = refEntId,
            ["des_ref_ent_id"] = des,
            ["code"] = joined
        };

        var relationCall = await ExecuteRawAsync(
            options,
            ApiYljgQueryRelation,
            bizParams,
            ct).ConfigureAwait(false);
        AppendRelationTrace(relationRequestTraces, relationCall);

        if (!relationCall.Ok)
        {
            var errorHint = string.IsNullOrWhiteSpace(relationCall.BizCode)
                ? relationCall.Summary
                : $"{relationCall.BizCode}: {relationCall.BizMessage}";
            if (!string.IsNullOrWhiteSpace(errorHint))
                relationErrors.Add(errorHint);
            return new RelationBatchResult(
                new Dictionary<string, HashSet<string>>(StringComparer.Ordinal),
                new Dictionary<string, int>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));
        }

        var childMap = ParseRelationChildrenMap(relationCall.ResponseText, codes);
        var codeLevels = ParseRelationCodeLevels(relationCall.ResponseText);
        var levelOneCodes = ParseRelationLevelOneCodes(relationCall.ResponseText);
        return new RelationBatchResult(childMap, codeLevels, levelOneCodes);
    }

    private static void AppendRelationTrace(List<string> traces, MsfxApiCallResult call)
    {
        if (string.IsNullOrWhiteSpace(call.RequestTrace))
            return;
        traces.Add(call.RequestTrace.TrimEnd());
    }

    private static Dictionary<string, HashSet<string>> ParseRelationChildrenMap(
        string responseBody,
        IReadOnlyList<string> parentCodes)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var parentSet = new HashSet<string>(parentCodes, StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            WalkRelationChildren(doc.RootElement, parentSet, map);
        }
        catch
        {
            // ignored
        }
        return map;
    }

    private static Dictionary<string, int> ParseRelationCodeLevels(string responseBody)
    {
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            WalkRelationCodeLevels(doc.RootElement, levels);
        }
        catch
        {
            // ignored
        }

        return levels;
    }

    private static HashSet<string> ParseRelationLevelOneCodes(string responseBody)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            WalkRelationLevelOneCodes(doc.RootElement, set);
        }
        catch
        {
            // ignored
        }

        return set;
    }

    private static void AddRelationChild(Dictionary<string, HashSet<string>> map, string parent, string child)
    {
        if (!map.TryGetValue(parent, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            map[parent] = set;
        }

        set.Add(child);
    }

    private static void WalkRelationChildren(
        JsonElement node,
        HashSet<string> parentSet,
        Dictionary<string, HashSet<string>> map)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var parent = GetStringAny(
                node,
                "parent_code",
                "parentCode",
                "ParentCode",
                "up_code",
                "from_code",
                "src_code",
                "source_code");
            var code = GetStringAny(node, "code", "Code", "trace_code", "traceCode");
            var c1 = GetStringAny(node, "child_code", "childCode", "ChildCode", "sub_code", "to_code", "des_code", "target_code");
            // query.relation commonly returns: code_relation_list.code_info[].{ parent_code, code, code_level }
            if (parentSet.Contains(parent) &&
                IsCandidateTraceCode(code) &&
                !string.Equals(parent, code, StringComparison.Ordinal))
            {
                AddRelationChild(map, parent, code);
            }
            if (parentSet.Contains(parent) && IsCandidateTraceCode(c1) &&
                !string.Equals(parent, c1, StringComparison.Ordinal))
                AddRelationChild(map, parent, c1);

            var self = code;
            if (parentSet.Contains(self))
            {
                var c2 = GetStringAny(node, "child_code", "childCode", "ChildCode", "sub_code", "to_code", "des_code", "target_code");
                if (IsCandidateTraceCode(c2) && !string.Equals(self, c2, StringComparison.Ordinal))
                    AddRelationChild(map, self, c2);
            }

            foreach (var p in node.EnumerateObject())
            {
                WalkRelationChildren(p.Value, parentSet, map);
            }

            return;
        }

        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
                WalkRelationChildren(item, parentSet, map);
        }
    }

    private static void WalkRelationCodeLevels(JsonElement node, Dictionary<string, int> levels)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var code = GetStringAny(node, "code", "Code", "child_code", "childCode", "sub_code", "trace_code", "traceCode", "drug_trace_code");
            var levelText = GetStringAny(node, "code_level", "codeLevel", "CodeLevel", "trace_code_level", "traceCodeLevel", "level", "Level");
            var level = ParseLevel(levelText);
            if (IsCandidateTraceCode(code) && level is >= 1 and <= 5)
            {
                levels[code] = level.Value;
            }

            foreach (var p in node.EnumerateObject())
                WalkRelationCodeLevels(p.Value, levels);
            return;
        }

        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
                WalkRelationCodeLevels(item, levels);
        }
    }

    private static void WalkRelationLevelOneCodes(JsonElement node, HashSet<string> output)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var level = GetStringAny(node, "code_level", "codeLevel", "CodeLevel", "trace_code_level", "traceCodeLevel", "level", "Level");
            if (string.Equals(level, "1", StringComparison.Ordinal))
            {
                var code = GetStringAny(node, "code", "Code", "child_code", "childCode", "sub_code", "trace_code", "traceCode", "drug_trace_code");
                if (IsCandidateTraceCode(code))
                    output.Add(code);
            }

            foreach (var p in node.EnumerateObject())
                WalkRelationLevelOneCodes(p.Value, output);
            return;
        }

        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
                WalkRelationLevelOneCodes(item, output);
        }
    }

    private static SortedDictionary<string, string> BuildSignedParameters(
        string appKey,
        string appSecret,
        string session,
        string methodName,
        IReadOnlyDictionary<string, string?> bizParams)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["app_key"] = appKey,
            ["format"] = "json",
            ["method"] = methodName,
            ["sign_method"] = "md5",
            ["timestamp"] = now,
            ["v"] = "2.0"
        };

        if (!string.IsNullOrWhiteSpace(session))
            parameters["session"] = session;

        foreach (var kv in bizParams)
        {
            var key = (kv.Key ?? string.Empty).Trim();
            var value = (kv.Value ?? string.Empty).Trim();
            if (key.Length == 0 || value.Length == 0)
                continue;
            parameters[key] = value;
        }

        parameters["sign"] = BuildTaobaoMd5Sign(parameters, appSecret);
        return parameters;
    }

    private static (string BizCode, string BizMessage, bool Ok) ParseBizStatus(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            var error = GetPropertyOrDefault(root, "error_response");
            if (error.ValueKind != JsonValueKind.Undefined)
            {
                return (
                    BizCode: GetString(error, "code"),
                    BizMessage: GetString(error, "msg"),
                    Ok: false);
            }

            var response = GetTopResponseNode(root);
            if (response.ValueKind == JsonValueKind.Undefined)
                return ("", "", false);

            var result = GetPropertyOrDefault(response, "result");
            if (result.ValueKind == JsonValueKind.Undefined)
                return ("", "", true);

            var msgCode = GetString(result, "msg_code");
            var msgInfo = GetString(result, "msg_info");

            var ok = TryGetBool(result, "response_success", out var responseSuccess)
                ? responseSuccess
                : TryGetBool(result, "success", out var success) && success;

            if (!ok && string.IsNullOrWhiteSpace(msgCode) && string.IsNullOrWhiteSpace(msgInfo))
                return ("", "", false);

            return (msgCode, msgInfo, ok || string.IsNullOrWhiteSpace(msgCode));
        }
        catch
        {
            return ("", "", false);
        }
    }

    private static string BuildTaobaoMd5Sign(
        SortedDictionary<string, string> parameters,
        string secret)
    {
        var sb = new StringBuilder();
        sb.Append(secret);
        foreach (var kv in parameters)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) ||
                string.IsNullOrWhiteSpace(kv.Value) ||
                string.Equals(kv.Key, "sign", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sb.Append(kv.Key);
            sb.Append(kv.Value);
        }

        sb.Append(secret);
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = MD5.HashData(bytes);

        var hex = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            hex.Append(b.ToString("X2", CultureInfo.InvariantCulture));

        return hex.ToString();
    }

    private static MsfxApiCallResult Fail(string message, string requestTrace = "")
        => new(false, null, message, "", "", "", "", requestTrace);

    private static string ParseRequestId(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var response = GetTopResponseNode(doc.RootElement);
            if (response.ValueKind == JsonValueKind.Undefined)
                return string.Empty;
            var rid = GetString(response, "request_id");
            if (!string.IsNullOrWhiteSpace(rid))
                return rid.Trim();
            rid = GetString(response, "requestId");
            return rid?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildRequestTrace(string gateway, SortedDictionary<string, string> parameters)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"POST {gateway}");
        foreach (var kv in parameters)
        {
            var value = string.Equals(kv.Key, "session", StringComparison.OrdinalIgnoreCase)
                ? "***"
                : kv.Value;
            sb.Append(kv.Key).Append('=').AppendLine(value);
        }

        return sb.ToString();
    }

    private static JsonElement GetTopResponseNode(JsonElement root)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name.EndsWith("_response", StringComparison.Ordinal) ||
                prop.Name.EndsWith(".response", StringComparison.Ordinal))
            {
                return prop.Value;
            }
        }

        return default;
    }

    private static JsonElement GetPropertyOrDefault(JsonElement node, string name)
    {
        if (node.ValueKind == JsonValueKind.Object &&
            node.TryGetProperty(name, out var value))
        {
            return value;
        }

        return default;
    }

    private static string GetString(JsonElement node, string name)
    {
        var value = GetPropertyOrDefault(node, name);
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static string GetStringAny(JsonElement node, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetString(node, name);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static long GetInt64(JsonElement node, string name)
    {
        var value = GetPropertyOrDefault(node, name);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var num))
            return num;
        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static bool TryGetBool(JsonElement node, string name, out bool value)
    {
        var raw = GetPropertyOrDefault(node, name);
        if (raw.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        if (raw.ValueKind == JsonValueKind.False)
        {
            value = false;
            return true;
        }

        if (raw.ValueKind == JsonValueKind.String &&
            bool.TryParse(raw.GetString(), out var parsed))
        {
            value = parsed;
            return true;
        }

        value = false;
        return false;
    }

    private static int? ParseLevel(string raw)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
            return level;
        return null;
    }

    private static bool IsCandidateTraceCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 20)
            return false;

        for (var i = 0; i < code.Length; i++)
        {
            if (!char.IsDigit(code[i]))
                return false;
        }

        return code[0] == '8';
    }
}
