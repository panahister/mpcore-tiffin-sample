using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MPCore.Application.Querying;
using MPCore.Localization;
using Tiffin.Notifications.Application.Commands;
using Tiffin.Notifications.Application.Contracts;
using Tiffin.Notifications.Application.Events;
using Tiffin.Notifications.Application.Ports;
using Tiffin.Notifications.Application.Queries;
using Tiffin.Notifications.Application.Resources;
using Tiffin.Notifications.Application.Views;
using Tiffin.Notifications.Domain;
using Tiffin.Notifications.Tests.Support;

namespace Tiffin.Notifications.Tests;

public sealed class FakeNotifications : INotificationRepository, INotificationReadModel
{
    private readonly List<Notification> items = [];

    public IReadOnlyList<Notification> All => items;

    public Task<Notification?> GetAsync(Guid id, string city, CancellationToken cancellationToken) => Task.FromResult(items.Find(n => n.Id == id && n.City == city));

    public void Add(Notification notification) => items.Add(notification);

    public Task<Page<NotificationView>> ListForAsync(string recipientId, string city, bool unreadOnly, PageRequest page, CancellationToken cancellationToken)
    {
        var found = items.Where(n => n.City == city && n.RecipientId == recipientId && (!unreadOnly || n.ReadOnUtc is null))
            .OrderByDescending(static n => n.OccurredOnUtc).Select(NotificationViews.Of).ToList();
        return Task.FromResult(new Page<NotificationView>(found, page.Number, page.Size, found.Count));
    }
}

/// <summary>What happened to an order becomes something its customer is told, in the language they read in.</summary>
public sealed class NotificationTests
{
    private static readonly Guid Order = Guid.NewGuid();
    private readonly FakeNotifications notifications = new();
    private readonly FakeClock clock = FakeClock.At2026();

    private static IMessageCatalog Catalog()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMPCoreMessageCatalog(static catalog => catalog.AddResources<NotificationsMessages>());
        return services.BuildServiceProvider().GetRequiredService<IMessageCatalog>();
    }

    private Task Placed(Guid? eventId = null) => OrderEventsHandler.Handle(
        new OrderPlaced(eventId ?? Guid.NewGuid(), Order, "TFN-260928-ABC123", "tehran", "sara", "Dizi Sara", 900_000m, "IRR", clock.UtcNow), notifications,
        new FakeUnitOfWork(), default);

    private Task Cancelled(string reason) => OrderEventsHandler.Handle(
        new OrderCancelled(Guid.NewGuid(), Order, "TFN-260928-ABC123", "tehran", "sara", reason, clock.UtcNow), notifications, new FakeUnitOfWork(), default);

    [Fact]
    public async Task An_event_becomes_one_notification_for_the_customer_of_the_order_kept_as_a_key_and_its_values()
    {
        var eventId = Guid.NewGuid();

        await Placed(eventId);
        await Placed(eventId);

        var notification = Assert.Single(notifications.All);
        Assert.Equal((eventId, "tehran", "sara", "notifications.order_placed"), (notification.Id, notification.City, notification.RecipientId, notification.MessageKey));
        Assert.Equal("900000", notification.Arguments["total"]);
        Assert.Equal("Dizi Sara", notification.Arguments["restaurant"]);
    }

    [Theory]
    [InlineData("en", "Your order TFN-260928-ABC123 at Dizi Sara was received: 900000 IRR.")]
    [InlineData("zh-Hans", "您在 Dizi Sara 的订单 TFN-260928-ABC123 已收到：900000 IRR。")]
    [InlineData("tr", "Dizi Sara restoranındaki TFN-260928-ABC123 numaralı siparişiniz alındı: 900000 IRR.")]
    [InlineData("zh-CN", "您在 Dizi Sara 的订单 TFN-260928-ABC123 已收到：900000 IRR。")]
    [InlineData("de", "Your order TFN-260928-ABC123 at Dizi Sara was received: 900000 IRR.")]
    public async Task The_sentence_is_made_when_it_is_read_in_the_language_of_who_reads(string language, string sentence)
    {
        await Placed();
        var notification = Assert.Single(notifications.All);

        var told = Catalog().Render(notification.MessageKey, notification.Arguments, CultureInfo.GetCultureInfo(language));

        Assert.Equal(sentence, told);
    }

    [Theory]
    [InlineData("payment-declined", "notifications.order_cancelled.payment-declined", "Nothing was charged")]
    [InlineData("restaurant-refused", "notifications.order_cancelled.restaurant-refused", "goes back to your card")]
    [InlineData("no-courier", "notifications.order_cancelled.no-courier", "no courier was free")]
    [InlineData("cancelled-by-customer", "notifications.order_cancelled.cancelled-by-customer", "as you asked")]
    [InlineData("a-reason-of-next-year", "notifications.order_cancelled", "was cancelled.")]
    public async Task A_cancellation_says_why_and_a_reason_nobody_has_heard_of_yet_is_told_without_one(string reason, string key, string says)
    {
        await Cancelled(reason);

        var notification = Assert.Single(notifications.All);
        Assert.Equal(key, notification.MessageKey);
        Assert.Contains(says, Catalog().Render(notification.MessageKey, notification.Arguments, CultureInfo.GetCultureInfo("en")), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_text_exists_in_every_language_and_names_the_same_values()
    {
        var catalog = Catalog();
        var values = new Dictionary<string, string>
        {
            ["order_number"] = "N", ["restaurant"] = "R", ["total"] = "T", ["currency"] = "C", ["courier"] = "K"
        };
        string[] keys =
        [
            "notifications.order_placed", "notifications.order_out_for_delivery", "notifications.order_delivered", "notifications.order_cancelled",
            "notifications.order_cancelled.payment-declined", "notifications.order_cancelled.restaurant-refused",
            "notifications.order_cancelled.no-courier", "notifications.order_cancelled.cancelled-by-customer",
            "notifications.order_cancelled.restaurant-did-not-answer"
        ];

        foreach (var key in keys)
        {
            var texts = new[] { "en", "zh-Hans", "tr" }.Select(l => catalog.Render(key, values, CultureInfo.GetCultureInfo(l))).ToList();
            Assert.All(texts, text => Assert.DoesNotContain("{", text, StringComparison.Ordinal));
            Assert.Equal(3, texts.Distinct().Count());
        }
    }

    [Fact]
    public async Task Somebody_is_shown_their_own_of_their_city_and_marks_them_read_once()
    {
        await Placed();
        var id = notifications.All[0].Id;
        Task<MPCore.Application.Results.Result<NotificationView>> Read(string who, FakeTenant? tenant = null) => MarkReadHandler.Handle(
            new MarkRead(id), FakeActor.User(who), tenant ?? FakeTenant.Tehran(), notifications, new FakeUnitOfWork(), clock, default);

        Assert.Equal("NOTIFICATION_NOT_FOUND", (await Read("reza")).FailureDescriptor!.Identity.Code);
        Assert.Equal("NOTIFICATION_NOT_FOUND", (await Read("sara", FakeTenant.Istanbul())).FailureDescriptor!.Identity.Code);
        var first = (await Read("sara")).Value.ReadOnUtc;
        clock.UtcNow += TimeSpan.FromHours(1);
        var second = (await Read("sara")).Value.ReadOnUtc;

        Assert.Equal(first, second);
        var unread = await ListMyNotificationsHandler.Handle(
            new ListMyNotifications(UnreadOnly: true), FakeActor.User("sara"), FakeTenant.Tehran(), notifications, default);
        Assert.Empty(unread.Value.Items);
        var ofReza = await ListMyNotificationsHandler.Handle(new ListMyNotifications(), FakeActor.User("reza"), FakeTenant.Tehran(), notifications, default);
        Assert.Empty(ofReza.Value.Items);
    }
}
