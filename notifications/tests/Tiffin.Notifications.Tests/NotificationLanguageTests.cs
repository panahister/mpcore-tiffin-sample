using Microsoft.AspNetCore.Http;
using Tiffin.Notifications.Api.Rest.Endpoints;

namespace Tiffin.Notifications.Tests;

public sealed class NotificationLanguageTests
{
    [Theory]
    [InlineData("ar", "ar")]
    [InlineData("ar-SA", "ar")]
    [InlineData("ar-EG", "ar")]
    [InlineData("en;q=0.2,ar-SA;q=0.9", "ar")]
    [InlineData("tr-TR", "tr")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("en;q=0.2,tr;q=0.9", "tr")]
    [InlineData("tr;q=0", "en")]
    [InlineData("ar;q=0", "en")]
    [InlineData("de-DE", "en")]
    [InlineData("*", "en")]
    [InlineData("", "en")]
    public void The_real_endpoint_negotiates_supported_parent_quality_and_fallback(string header, string expected)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.AcceptLanguage = header;
        Assert.Equal(expected, NotificationEndpoints.LanguageOf(http).Name);
    }
}
