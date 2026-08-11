using Microsoft.Extensions.Options;
using PacToolkits.Core;

namespace PacToolkits.Api.Hosting;

/// <summary>
/// SchemaBounds 启动校验：正式发布 SemVer，且 min ≤ max
/// </summary>
public sealed class SchemaBoundsOptionsValidator : IValidateOptions<SchemaBoundsOptions>
{
    public const string InvalidSemVerMessage =
        "SchemaBounds:MinDbSchema and MaxDbSchema must be valid release SemVer (X.Y.Z)";

    public const string OutOfOrderMessage =
        "SchemaBounds:MinDbSchema must be less than or equal to MaxDbSchema";

    public ValidateOptionsResult Validate(string? name, SchemaBoundsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!IsReleaseSemVer(options.MinDbSchema) || !IsReleaseSemVer(options.MaxDbSchema))
        {
            return ValidateOptionsResult.Fail(InvalidSemVerMessage);
        }

        if (!IsOrderedRange(options.MinDbSchema, options.MaxDbSchema))
        {
            return ValidateOptionsResult.Fail(OutOfOrderMessage);
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsReleaseSemVer(string? text)
        => SemVer.TryParse(text, out var value) && value.PreRelease is null;

    private static bool IsOrderedRange(string? minText, string? maxText)
    {
        if (!SemVer.TryParse(minText, out var min) || !SemVer.TryParse(maxText, out var max))
        {
            return false;
        }

        return SemVer.Compare(min, max) <= 0;
    }
}
