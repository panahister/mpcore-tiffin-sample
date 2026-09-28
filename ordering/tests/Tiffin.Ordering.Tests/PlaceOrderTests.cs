using MPCore.Application.Results;
using Tiffin.Ordering.Application;
using Tiffin.Ordering.Application.Commands;
using Tiffin.Ordering.Application.Contracts;
using Tiffin.Ordering.Application.Ports;
using Tiffin.Ordering.Application.Validators;
using Tiffin.Ordering.Application.Views;
using Tiffin.Ordering.Domain;
using Tiffin.Ordering.Tests.Support;

namespace Tiffin.Ordering.Tests;

public sealed class FakeQuotes : IRestaurantQuotes
{
    public static readonly Guid DiziSara = Guid.Parse("01a0e4e9-bdc9-7aaa-9e9f-4d0e5de82c16");

    public Result<RestaurantQuote>? Answer { get; set; }

    public int Asked { get; private set; }

    public Task<Result<RestaurantQuote>> QuoteAsync(Guid restaurantId, IReadOnlyList<(string Code, int Quantity)> lines, CancellationToken cancellationToken)
    {
        Asked++;
        return Task.FromResult(Answer ?? Result<RestaurantQuote>.Success(new RestaurantQuote(
            restaurantId, "Dizi Sara", "tehran", "IRR",
            [.. lines.Select(static l => new RestaurantQuoteLine(l.Code, l.Code, 450_000m, l.Quantity))], lines.Sum(static l => 450_000m * l.Quantity))));
    }
}

public sealed class FakePaymentIntents : IPaymentIntents
{
    public Result<Guid>? Answer { get; set; }

    public List<(Guid OrderId, string Token, decimal Amount)> Asked { get; } = [];

    public Task<Result<Guid>> CreateAsync(
        Guid orderId, string customerId, string paymentToken, decimal amount, string currency, CancellationToken cancellationToken)
    {
        Asked.Add((orderId, paymentToken, amount));
        return Task.FromResult(Answer ?? Result<Guid>.Success(Guid.NewGuid()));
    }
}

/// <summary>Placing an order: two questions to other services, then one transaction of this service's own.</summary>
public sealed class PlaceOrderTests
{
    private readonly FakeQuotes quotes = new();
    private readonly FakePaymentIntents intents = new();
    private readonly FakeOrders orders = new();
    private readonly RecordingPublisher publisher = new();
    private readonly FakeAudit audit = new();

    private static PlaceOrder Two(decimal expectedTotal = 900_000m, string token = "tok_ok") => new(
        FakeQuotes.DiziSara, [new PlaceOrderLine("DIZI", 2)], new PlaceOrderAddress("Sara Ahmadi", "+989121234567", "Vanak", "12 Gandhi St"),
        token, expectedTotal);

    private Task<Result<OrderAccepted>> Place(PlaceOrder command, FakeActor? actor = null, FakeTenant? tenant = null) => PlaceOrderHandler.Handle(
        command, actor ?? FakeActor.User("sara", "customer"), tenant ?? FakeTenant.Tehran(), quotes, intents, orders, publisher, audit,
        new FakeUnitOfWork(), FakeClock.At2026(), default);

    [Fact]
    public async Task An_order_is_stored_with_the_request_for_its_charge_and_the_customer_is_told_its_identity()
    {
        var accepted = (await Place(Two())).Value;

        var order = await orders.GetAsync(accepted.OrderId, "tehran", default);
        Assert.NotNull(order);
        Assert.Equal((OrderStatus.Placed, 900_000m, "sara", "tehran"), (order.Status, order.Total, order.CustomerId, order.City));
        var asked = publisher.Single<PaymentRequested>();
        Assert.Equal((order.Id, order.PaymentIntentId, 900_000m), (asked.OrderId, asked.PaymentIntentId, asked.Amount));
        Assert.Equal("order-placed", Assert.Single(audit.Records).Action);
    }

    [Fact]
    public async Task The_cards_token_goes_to_payments_and_nowhere_else()
    {
        var accepted = (await Place(Two(token: "tok_secret_of_sara"))).Value;

        Assert.Equal("tok_secret_of_sara", Assert.Single(intents.Asked).Token);
        var order = await orders.GetAsync(accepted.OrderId, "tehran", default);
        Assert.DoesNotContain("tok_secret_of_sara", System.Text.Json.JsonSerializer.Serialize(publisher.Messages.Cast<object>()), StringComparison.Ordinal);
        Assert.DoesNotContain("tok_secret_of_sara", Two(token: "tok_secret_of_sara").ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(Order).GetProperties(), static p => p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(order);
    }

    [Fact]
    public async Task A_total_that_changed_is_refused_with_the_total_that_is_true_and_nothing_is_asked_of_payments()
    {
        var result = await Place(Two(expectedTotal: 800_000m));

        Assert.Equal("ORDER_TOTAL_CHANGED", result.FailureDescriptor!.Identity.Code);
        Assert.Equal("900000", result.FailureDescriptor.Message.Arguments["total"]);
        Assert.Empty(intents.Asked);
        Assert.Empty(publisher.Messages);
    }

    [Fact]
    public async Task A_restaurant_of_another_city_does_not_exist_for_the_customer()
    {
        var result = await Place(Two(), FakeActor.User("elif", "customer"), FakeTenant.Istanbul());

        Assert.Equal("RESTAURANT_NOT_FOUND", result.FailureDescriptor!.Identity.Code);
        Assert.Empty(intents.Asked);
    }

    [Fact]
    public async Task What_the_restaurant_refuses_is_passed_on_under_the_restaurants_own_code()
    {
        quotes.Answer = Result<RestaurantQuote>.FromFailure(OrderingFailures.RefusedByRestaurant("RESTAURANT_CLOSED", "Dizi Sara is closed."));

        var failure = (await Place(Two())).FailureDescriptor!;

        Assert.Equal(("tiffin.restaurants", "RESTAURANT_CLOSED"), (failure.Identity.Domain, failure.Identity.Code));
        Assert.Equal(ErrorCategory.BusinessRule, failure.Category);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_service_that_does_not_answer_is_told_as_something_to_try_again_and_nothing_is_stored(bool restaurantsIsDown)
    {
        if (restaurantsIsDown)
        {
            quotes.Answer = Result<RestaurantQuote>.FromFailure(OrderingFailures.RestaurantsUnavailable());
        }
        else
        {
            intents.Answer = Result<Guid>.FromFailure(OrderingFailures.PaymentsUnavailable());
        }

        var failure = (await Place(Two())).FailureDescriptor!;

        Assert.Equal(ErrorCategory.DependencyUnavailable, failure.Category);
        Assert.True(failure.Retry.IsRetryable);
        Assert.Empty(publisher.Messages);
        Assert.Empty(audit.Records);
    }

    [Fact]
    public async Task Who_orders_and_where_comes_from_the_token()
    {
        Assert.Equal("CUSTOMER_REQUIRED", (await Place(Two(), FakeActor.Anonymous())).FailureDescriptor!.Identity.Code);
        Assert.Equal("CUSTOMER_REQUIRED", (await Place(Two(), tenant: FakeTenant.None())).FailureDescriptor!.Identity.Code);
        Assert.Equal(0, quotes.Asked);
    }

    [Theory]
    [InlineData("09121234567", false)]
    [InlineData("+989121234567", true)]
    [InlineData("+90 532 123 45 67", false)]
    public void A_phone_number_is_written_as_the_world_writes_it(string phone, bool accepted)
    {
        var command = Two() with { DeliverTo = new PlaceOrderAddress("Sara", phone, "Vanak", "12 Gandhi St") };

        Assert.Equal(accepted, new PlaceOrderValidator().Validate(command).IsValid);
    }

    [Fact]
    public void An_order_without_lines_or_with_a_hundred_of_one_item_is_refused_before_anybody_is_asked()
    {
        var validator = new PlaceOrderValidator();

        Assert.False(validator.Validate(Two() with { Lines = [] }).IsValid);
        Assert.False(validator.Validate(Two() with { Lines = [new PlaceOrderLine("DIZI", 100)] }).IsValid);
        Assert.False(validator.Validate(Two() with { PaymentToken = "" }).IsValid);
        Assert.True(validator.Validate(Two()).IsValid);
    }
}

public sealed class SettingsTests
{
    [Fact]
    public void The_settings_of_the_calls_to_other_services_print_no_secret()
    {
        var settings = new Tiffin.Ordering.Infrastructure.OtherServices
        {
            Restaurants = new Uri("http://localhost:6300/"), Payments = new Uri("http://localhost:6501"),
            Authority = new Uri("http://localhost:38180/realms/tiffin"), ClientId = "tiffin-ordering-service", ClientSecret = "a-secret-nobody-may-see"
        };

        Assert.DoesNotContain("a-secret-nobody-may-see", settings.ToString(), StringComparison.Ordinal);
        Assert.Contains("tiffin-ordering-service", settings.ToString(), StringComparison.Ordinal);
    }
}
