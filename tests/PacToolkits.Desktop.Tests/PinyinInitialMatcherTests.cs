using PacToolkits.Application.TextSearch;

namespace PacToolkits.Desktop.Tests;

public sealed class PinyinInitialMatcherTests
{
    [Theory]
    [InlineData("xjx", "盐酸溴己新注射液")]
    [InlineData("tbk", "复方托吡卡胺滴眼液")]
    [InlineData("tpk", "复方托吡卡胺滴眼液")]
    [InlineData("ysxjx", "盐酸溴己新注射液")]
    [InlineData("溴己", "盐酸溴己新注射液")]
    [InlineData("q", "卡介苗")]
    [InlineData("k", "卡介苗")]
    public void IsMatch_finds_common_drug_prefixes(string query, string candidate)
        => Assert.True(PinyinInitialMatcher.IsMatch(query, candidate));

    [Fact]
    public void GetInitials_uses_primary_reading_for_display()
    {
        Assert.Equal("ysxjxzsy", PinyinInitialMatcher.GetInitials("盐酸溴己新注射液"));
        Assert.Equal("fftpkadyy", PinyinInitialMatcher.GetInitials("复方托吡卡胺滴眼液"));
    }

    [Theory]
    [InlineData("abc", "盐酸溴己新注射液")]
    [InlineData("zzz", "复方托吡卡胺滴眼液")]
    public void IsMatch_rejects_unrelated_queries(string query, string candidate)
        => Assert.False(PinyinInitialMatcher.IsMatch(query, candidate));

    [Fact]
    public void GetInitialOptions_includes_all_readings_for_polyphonic_chars()
    {
        Assert.Equal(['b', 'p'], PinyinInitialMatcher.GetInitialOptions("吡")[0]);
        Assert.Equal(['k', 'q'], PinyinInitialMatcher.GetInitialOptions("卡")[0]);
    }

    [Fact]
    public void IsMatch_accepts_every_initial_option_for_dictionary_han_chars()
    {
        for (var code = 0x4E00; code <= 0x9FFF; code++)
        {
            var ch = (char)code;
            var options = PinyinInitialMatcher.GetInitialOptions(ch.ToString());
            if (options.Length == 0 || options[0].Length == 0)
            {
                continue;
            }

            foreach (var initial in options[0])
            {
                Assert.True(
                    PinyinInitialMatcher.IsMatch(initial.ToString(), ch.ToString()),
                    $"char {ch} initial {initial} should match");
            }
        }
    }

    [Fact]
    public void IsMatch_accepts_subsequence_of_primary_initials()
    {
        const string candidate = "盐酸溴己新注射液";
        var initials = PinyinInitialMatcher.GetInitials(candidate);
        for (var start = 0; start < initials.Length; start++)
        {
            for (var len = 1; len <= initials.Length - start; len++)
            {
                var query = initials.Substring(start, len);
                Assert.True(
                    PinyinInitialMatcher.IsMatch(query, candidate),
                    $"query {query} should match via primary initials {initials}");
            }
        }
    }
}
