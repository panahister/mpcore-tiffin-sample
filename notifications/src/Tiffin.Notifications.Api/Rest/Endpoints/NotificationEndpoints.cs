using System.Globalization;
using Microsoft.Net.Http.Headers;
using MPCore.Application.Querying;
using MPCore.Application.Results;
using MPCore.Localization;
using MPCore.Transport.Http;
using Tiffin.Notifications.Application.Commands;
using Tiffin.Notifications.Application.Queries;
using Tiffin.Notifications.Application.Views;
using Wolverine;

namespace Tiffin.Notifications.Api.Rest.Endpoints;

/// <summary>The REST surface of the Notifications service.</summary>
/// <remarks>
/// <para>
/// Every endpoint binds the request, invokes one command or query through Wolverine, and renders the
/// <see cref="Result"/> with MP Core's <c>ToHttpResult</c>.
/// </para>
/// <para>
/// A notification leaves the application as a key and its values. Its sentence is made here, from MP Core's
/// message catalog, in the language the caller asks for with <c>Accept-Language</c>: the same catalog, and
/// the same fallback from a culture to its parent to English, that tell a failure in the caller's language.
/// </para>
/// </remarks>
public static class NotificationEndpoints
{
    /// <summary>The languages the service has texts in. The first is what somebody is told who asks for none of them.</summary>
    private static readonly string[] Languages = ["en", "zh-Hans", "tr", "ar"];

    public static RouteGroupBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var notifications = endpoints.MapGroup("/v1/notifications").WithTags("Notifications");

        notifications.MapGet("/", static async (
                bool? unread, int? page, int? size, HttpContext http, IMessageCatalog catalog, IMessageBus bus, CancellationToken ct) =>
            {
                var culture = LanguageOf(http);
                return (await bus.InvokeAsync<Result<Page<NotificationView>>>(
                        new ListMyNotifications(unread ?? false, page ?? 1, size ?? PageRequest.DefaultSize), ct).ConfigureAwait(false))
                    .ToHttpResult(found => Results.Ok(new Page<NotificationView>(
                        [.. found.Items.Select(n => Told(n, catalog, culture))], found.Number, found.Size, found.Total)));
            })
            .Produces<Page<NotificationView>>(200)
            .WithName("ListMyNotifications");

        notifications.MapPost("/{notificationId:guid}/read", static async (
                Guid notificationId, HttpContext http, IMessageCatalog catalog, IMessageBus bus, CancellationToken ct) =>
            {
                var culture = LanguageOf(http);
                return (await bus.InvokeAsync<Result<NotificationView>>(new MarkRead(notificationId), ct).ConfigureAwait(false))
                    .ToHttpResult(n => Results.Ok(Told(n, catalog, culture)));
            })
            .Produces<NotificationView>(200)
            .WithName("MarkNotificationRead");

        return notifications;
    }

    private static NotificationView Told(NotificationView notification, IMessageCatalog catalog, CultureInfo culture) =>
        notification with { Text = catalog.Render(notification.MessageKey, notification.Arguments, culture) };

    /// <summary>The first language the caller asks for that the service has texts in, by the caller's own order of preference.</summary>
    internal static CultureInfo LanguageOf(HttpContext http)
    {
        var asked = http.Request.GetTypedHeaders().AcceptLanguage
            // RFC 9110 section 12.4.2: a zero quality value means not acceptable.
            .Where(static l => (l.Quality ?? 1) > 0)
            .OrderByDescending(static l => l.Quality ?? 1)
            .Select(static l => l.Value.Value)
            .OfType<string>();
        foreach (var language in asked)
        {
            // The culture asked for, then its parents: "zh-CN" is told in "zh-Hans", "tr-TR" in "tr".
            CultureInfo culture;
            try
            {
                culture = CultureInfo.GetCultureInfo(language);
            }
            catch (CultureNotFoundException)
            {
                continue;
            }

            for (; culture.Name.Length > 0; culture = culture.Parent)
            {
                var known = Languages.FirstOrDefault(l => string.Equals(l, culture.Name, StringComparison.OrdinalIgnoreCase));
                if (known is not null)
                {
                    return CultureInfo.GetCultureInfo(known);
                }
            }
        }

        return CultureInfo.GetCultureInfo(Languages[0]);
    }
}
