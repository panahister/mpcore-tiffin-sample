using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Tiffin.Ordering.Application.Resources;

namespace Tiffin.Ordering.Tests;

public sealed partial class MessageCatalogTests
{
    [Fact]
    public void Arabic_has_a_real_satellite_with_every_default_key_and_argument()
    {
        var manager = new ResourceManager(typeof(OrderingMessages));
        using var defaults = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false);
        using var arabic = manager.GetResourceSet(CultureInfo.GetCultureInfo("ar"), true, false);
        Assert.NotNull(defaults);
        Assert.NotNull(arabic);
        var original = Values(defaults);
        var translated = Values(arabic);
        Assert.Equal(15, original.Count);
        Assert.Equal(original.Keys.Order(StringComparer.Ordinal), translated.Keys.Order(StringComparer.Ordinal));
        foreach (var (key, text) in translated)
        {
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.NotEqual(original[key], text);
            Assert.Equal(Arguments(original[key]), Arguments(text));
            Assert.Equal(text, manager.GetString(key, CultureInfo.GetCultureInfo("ar-SA")));
        }
    }

    [Theory]
    [InlineData("zh-Hans")]
    [InlineData("zh-CN")]
    [InlineData("tr")]
    public void Published_languages_keep_their_own_text_and_arguments(string culture)
    {
        var manager = new ResourceManager(typeof(OrderingMessages));
        using var defaults = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false);
        Assert.NotNull(defaults);
        foreach (var (key, text) in Values(defaults))
        {
            var translated = manager.GetString(key, CultureInfo.GetCultureInfo(culture));
            Assert.False(string.IsNullOrWhiteSpace(translated));
            Assert.NotEqual(text, translated);
            Assert.Equal(Arguments(text), Arguments(translated!));
        }
    }

    private static Dictionary<string, string> Values(ResourceSet set) => set.Cast<DictionaryEntry>()
        .ToDictionary(static entry => (string)entry.Key, static entry => (string)entry.Value!, StringComparer.Ordinal);

    private static string[] Arguments(string text) => Placeholder().Matches(text)
        .Select(static match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    [GeneratedRegex("\\{(\\w+)\\}")]
    private static partial Regex Placeholder();
}
