using System.Reflection;
using System.Text.Json;
using MPCore.Domain.Events;
using FromDispatch = Tiffin.Dispatch.Domain.Events;
using FromKitchen = Tiffin.Kitchen.Domain.Events;
using FromOrdering = Tiffin.Ordering.Application.Contracts;
using FromPayments = Tiffin.Payments.Domain.Events;
using OrderEvents = Tiffin.Ordering.Domain.Events;
using ToDispatch = Tiffin.Dispatch.Application.Contracts;
using ToKitchen = Tiffin.Kitchen.Application.Contracts;
using ToNotifications = Tiffin.Notifications.Application.Contracts;
using ToOrdering = Tiffin.Ordering.Application.Contracts;
using ToPayments = Tiffin.Payments.Application.Contracts;
using ToTracking = Tiffin.Tracking.Application.Contracts;

namespace Tiffin.Contracts.Tests;

/// <summary>
/// The nine services share no assembly. Each declares the messages it reads in its own code, and what holds
/// a writer and its readers together is a name, a version and the JSON between them. These tests write each
/// message as its writer does and read it as each reader does.
/// </summary>
/// <remarks>
/// Ian Robinson described the idea as <i>consumer-driven contracts</i> (2006): a reader states what it
/// needs, and the writer is tested against that. Between services in separate repositories the same test is
/// a Pact; here, where the services live side by side, it is a unit test, and it runs with every build.
/// </remarks>
[Trait("Category", "Contract")]
public sealed class ContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Order = Guid.Parse("01a0e50d-ced7-770e-af44-6a6049b96b34");
    private static readonly Guid Restaurant = Guid.Parse("01a0e4e9-bdc9-7aaa-9e9f-4d0e5de82c16");
    private const string Number = "TFN-260928-96B34A";

    /// <summary>Every message that crosses from one service to another: as it is written, and the type each reader reads it as.</summary>
    public static TheoryData<string, object, Type> Messages() => new()
    {
        { "Ordering asks Payments to charge", new FromOrdering.PaymentRequested(Order, Number, Guid.NewGuid(), 900_000m, "IRR", Now), typeof(ToPayments.PaymentRequested) },
        { "Ordering asks Payments to refund", new FromOrdering.RefundRequested(Order, Number, "no-courier", Now), typeof(ToPayments.RefundRequested) },
        {
            "Ordering asks the Kitchen to cook",
            new FromOrdering.PreparationRequested(Order, Number, Restaurant, "Dizi Sara", [new FromOrdering.PreparationLine("DIZI", "Dizi", 2)], Now),
            typeof(ToKitchen.PreparationRequested)
        },
        { "Ordering tells the Kitchen to stop", new FromOrdering.PreparationCancelled(Order, Number, "no-courier", Now), typeof(ToKitchen.PreparationCancelled) },
        {
            "Ordering asks Dispatch for a courier",
            new FromOrdering.CourierRequested(Order, Number, "sara", "Dizi Sara", new FromOrdering.CourierDestination("Sara Ahmadi", "+989121234567", "Vanak", "12 Gandhi St"), Now),
            typeof(ToDispatch.CourierRequested)
        },
        { "Payments answers: charged", new FromPayments.PaymentAuthorized(Order, "PLA-1", Now), typeof(ToOrdering.PaymentAuthorized) },
        { "Payments answers: declined", new FromPayments.PaymentDeclined(Order, "INSUFFICIENT_FUNDS", Now), typeof(ToOrdering.PaymentDeclined) },
        { "Payments answers: refunded", new FromPayments.PaymentRefunded(Order, "PLR-1", Now), typeof(ToOrdering.PaymentRefunded) },
        { "The Kitchen answers: accepted", new FromKitchen.OrderAccepted(Order, 25, Now), typeof(ToOrdering.KitchenAccepted) },
        { "The Kitchen answers: rejected", new FromKitchen.OrderRejected(Order, "out of lamb", Now), typeof(ToOrdering.KitchenRejected) },
        { "Dispatch answers: a courier", new FromDispatch.CourierAssigned(Order, "omid", "Omid Sadeghi", Now), typeof(ToOrdering.CourierAssigned) },
        { "Dispatch answers: nobody", new FromDispatch.CourierUnavailable(Order, Now), typeof(ToOrdering.CourierUnavailable) },
        { "Dispatch says a delivery began, to Tracking", new FromDispatch.DeliveryAssigned(Order, Number, "tehran", "sara", "omid", Now), typeof(ToTracking.DeliveryAssigned) },
        { "Dispatch says a delivery was completed, to Tracking", new FromDispatch.DeliveryCompleted(Order, Number, "tehran", "sara", "omid", Now), typeof(ToTracking.DeliveryCompleted) },
        { "Dispatch says a delivery was completed, to Ordering", new FromDispatch.DeliveryCompleted(Order, Number, "tehran", "sara", "omid", Now), typeof(ToOrdering.DeliveryCompleted) },
        {
            "Ordering says an order was placed, to Notifications",
            new OrderEvents.OrderPlaced(Order, Number, "tehran", "sara", Restaurant, "Dizi Sara", 900_000m, "IRR", 2, Now), typeof(ToNotifications.OrderPlaced)
        },
        { "Ordering says an order was cancelled, to Notifications", new OrderEvents.OrderCancelled(Order, Number, "tehran", "sara", "no-courier", Now), typeof(ToNotifications.OrderCancelled) },
        {
            "Ordering says an order is on its way, to Notifications",
            new OrderEvents.OrderOutForDelivery(Order, Number, "tehran", "sara", "omid", "Omid Sadeghi", Now), typeof(ToNotifications.OrderOutForDelivery)
        },
        { "Ordering says an order was delivered, to Notifications", new OrderEvents.OrderDelivered(Order, Number, "tehran", "sara", Now), typeof(ToNotifications.OrderDelivered) },
        {
            "Restaurants says a restaurant exists, to the Kitchen",
            new Tiffin.Restaurants.Domain.Events.RestaurantRegistered(Restaurant, "tehran", "Dizi Sara", "mina", Now), typeof(ToKitchen.RestaurantRegistered)
        },
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public void A_reader_reads_what_its_writer_wrote(string what, object written, Type reader)
    {
        foreach (var options in new[] { new JsonSerializerOptions(), new JsonSerializerOptions(JsonSerializerDefaults.Web) })
        {
            var json = JsonSerializer.Serialize(written, written.GetType(), options);
            var read = JsonSerializer.Deserialize(json, reader, options)!;

            // The same contract: its name, its version, and the identity of the message.
            var (w, r) = ((IIntegrationEvent)written, (IIntegrationEvent)read);
            Assert.Equal((w.EventName, w.EventVersion, w.EventId, w.OccurredOnUtc), (r.EventName, r.EventVersion, r.EventId, r.OccurredOnUtc));

            // A reader declares only what its writer sends, and reads what was sent.
            foreach (var property in reader.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var source = written.GetType().GetProperty(property.Name);
                Assert.True(source is not null, $"{what}: the reader expects {property.Name}, which the writer does not send.");
                Assert.Equal(
                    JsonSerializer.Serialize(source.GetValue(written), options),
                    JsonSerializer.Serialize(property.GetValue(read), options));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Messages))]
    public void A_message_that_holds_an_address_prints_none_of_it(string what, object written, Type reader)
    {
        var read = JsonSerializer.Deserialize(JsonSerializer.Serialize(written, written.GetType()), reader)!;

        foreach (var printed in new[] { written.ToString()!, read.ToString()! })
        {
            Assert.False(printed.Contains("Sara Ahmadi", StringComparison.Ordinal), $"{what} prints the recipient.");
            Assert.False(printed.Contains("Gandhi", StringComparison.Ordinal), $"{what} prints the door.");
            Assert.False(printed.Contains("+98912", StringComparison.Ordinal), $"{what} prints the phone number.");
        }
    }

    [Fact]
    public void A_writer_and_its_reader_name_the_same_channel()
    {
        (string Writer, string Reader)[] channels =
        [
            (Tiffin.Ordering.Api.Hosting.OrderingQueues.PaymentRequested, Tiffin.Payments.Api.Hosting.PaymentsQueues.PaymentRequested),
            (Tiffin.Ordering.Api.Hosting.OrderingQueues.RefundRequested, Tiffin.Payments.Api.Hosting.PaymentsQueues.RefundRequested),
            (Tiffin.Ordering.Api.Hosting.OrderingQueues.PreparationRequested, Tiffin.Kitchen.Api.Hosting.KitchenQueues.PreparationRequested),
            (Tiffin.Ordering.Api.Hosting.OrderingQueues.PreparationCancelled, Tiffin.Kitchen.Api.Hosting.KitchenQueues.PreparationCancelled),
            (Tiffin.Ordering.Api.Hosting.OrderingQueues.CourierRequested, Tiffin.Dispatch.Api.Hosting.DispatchQueues.CourierRequested),
            (Tiffin.Payments.Api.Hosting.PaymentsQueues.PaymentAuthorized, Tiffin.Ordering.Api.Hosting.OrderingQueues.PaymentAuthorized),
            (Tiffin.Payments.Api.Hosting.PaymentsQueues.PaymentDeclined, Tiffin.Ordering.Api.Hosting.OrderingQueues.PaymentDeclined),
            (Tiffin.Payments.Api.Hosting.PaymentsQueues.PaymentRefunded, Tiffin.Ordering.Api.Hosting.OrderingQueues.PaymentRefunded),
            (Tiffin.Kitchen.Api.Hosting.KitchenQueues.OrderAccepted, Tiffin.Ordering.Api.Hosting.OrderingQueues.KitchenAccepted),
            (Tiffin.Kitchen.Api.Hosting.KitchenQueues.OrderRejected, Tiffin.Ordering.Api.Hosting.OrderingQueues.KitchenRejected),
            (Tiffin.Dispatch.Api.Hosting.DispatchQueues.CourierAssigned, Tiffin.Ordering.Api.Hosting.OrderingQueues.CourierAssigned),
            (Tiffin.Dispatch.Api.Hosting.DispatchQueues.CourierUnavailable, Tiffin.Ordering.Api.Hosting.OrderingQueues.CourierUnavailable),
            (Tiffin.Dispatch.Api.Hosting.DispatchTopics.DeliveryCompleted, Tiffin.Ordering.Api.Hosting.OrderingTopics.DeliveryCompleted),
            (Tiffin.Dispatch.Api.Hosting.DispatchTopics.DeliveryCompleted, Tiffin.Tracking.Api.Hosting.TrackingTopics.DeliveryCompleted),
            (Tiffin.Dispatch.Api.Hosting.DispatchTopics.DeliveryAssigned, Tiffin.Tracking.Api.Hosting.TrackingTopics.DeliveryAssigned),
            (Tiffin.Ordering.Api.Hosting.OrderingTopics.OrderPlaced, Tiffin.Notifications.Api.Hosting.NotificationsTopics.OrderPlaced),
            (Tiffin.Ordering.Api.Hosting.OrderingTopics.OrderCancelled, Tiffin.Notifications.Api.Hosting.NotificationsTopics.OrderCancelled),
            (Tiffin.Ordering.Api.Hosting.OrderingTopics.OrderOutForDelivery, Tiffin.Notifications.Api.Hosting.NotificationsTopics.OrderOutForDelivery),
            (Tiffin.Ordering.Api.Hosting.OrderingTopics.OrderDelivered, Tiffin.Notifications.Api.Hosting.NotificationsTopics.OrderDelivered),
            (Tiffin.Restaurants.Api.Hosting.RestaurantsTopics.RestaurantRegistered, Tiffin.Kitchen.Api.Hosting.KitchenTopics.RestaurantRegistered),
        ];

        Assert.All(channels, static channel => Assert.Equal(channel.Writer, channel.Reader));
    }

    [Fact]
    public void A_topic_carries_the_contract_its_name_says_and_every_channel_names_a_version()
    {
        Assert.Equal(OrderEvents.OrderPlaced.Name + ".v1", Tiffin.Ordering.Api.Hosting.OrderingTopics.OrderPlaced);
        Assert.Equal(OrderEvents.OrderCancelled.Name + ".v1", Tiffin.Ordering.Api.Hosting.OrderingTopics.OrderCancelled);
        Assert.Equal(OrderEvents.OrderOutForDelivery.Name + ".v1", Tiffin.Ordering.Api.Hosting.OrderingTopics.OrderOutForDelivery);
        Assert.Equal(OrderEvents.OrderDelivered.Name + ".v1", Tiffin.Ordering.Api.Hosting.OrderingTopics.OrderDelivered);
        Assert.Equal(FromDispatch.DeliveryAssigned.Name + ".v1", Tiffin.Dispatch.Api.Hosting.DispatchTopics.DeliveryAssigned);
        Assert.Equal(FromDispatch.DeliveryCompleted.Name + ".v1", Tiffin.Dispatch.Api.Hosting.DispatchTopics.DeliveryCompleted);
        Assert.Equal(Tiffin.Restaurants.Domain.Events.RestaurantRegistered.Name + ".v1", Tiffin.Restaurants.Api.Hosting.RestaurantsTopics.RestaurantRegistered);
        Assert.Equal(Tiffin.Access.Domain.Events.RoleChanged.Name + ".v1", Tiffin.Access.Api.Hosting.AccessTopics.RoleChanged);
        Assert.Equal(Tiffin.Media.Domain.Events.FileAvailable.Name + ".v1", Tiffin.Media.Api.Hosting.MediaTopics.FileAvailable);
        Assert.Equal(Tiffin.Media.Domain.Events.FileDeleted.Name + ".v1", Tiffin.Media.Api.Hosting.MediaTopics.FileDeleted);

        var channels = new[]
            {
                typeof(Tiffin.Ordering.Api.Hosting.OrderingQueues), typeof(Tiffin.Payments.Api.Hosting.PaymentsQueues),
                typeof(Tiffin.Kitchen.Api.Hosting.KitchenQueues), typeof(Tiffin.Dispatch.Api.Hosting.DispatchQueues)
            }
            .SelectMany(static t => t.GetFields(BindingFlags.Public | BindingFlags.Static)).Select(static f => (string)f.GetRawConstantValue()!);
        Assert.All(channels, static channel => Assert.Matches(@"^tiffin\.[a-z]+\.[a-z-]+\.v[0-9]+$", channel));
    }

    [Fact]
    public void A_copy_of_a_contract_file_says_what_the_original_says()
    {
        var root = RepositoryRoot();
        var original = Contract(Path.Combine(root, "payments/src/Tiffin.Payments.Api/Protos/tiffin_payments.proto"));
        var copy = Contract(Path.Combine(root, "ordering/src/Tiffin.Ordering.Infrastructure/Protos/tiffin_payments.proto"));

        Assert.NotEmpty(original);
        Assert.Equal(original, copy);
    }

    /// <summary>What a contract file says: its lines without comments, without the namespace a language generates into, and without empty ones.</summary>
    private static List<string> Contract(string path) =>
        [.. File.ReadAllLines(path)
            .Select(static line => line.Split("//")[0].Trim())
            .Where(static line => line.Length > 0 && !line.StartsWith("option csharp_namespace", StringComparison.Ordinal))];

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "global.json")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The root of the repository was not found above the tests.");
    }
}
