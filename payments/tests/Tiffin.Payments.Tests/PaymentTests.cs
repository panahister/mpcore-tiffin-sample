using Microsoft.Extensions.Logging.Abstractions;
using MPCore.Application.Results;
using Tiffin.Payments.Application;
using Tiffin.Payments.Application.Commands;
using Tiffin.Payments.Application.Contracts;
using Tiffin.Payments.Application.Ports;
using Tiffin.Payments.Domain;
using Tiffin.Payments.Domain.Events;
using Tiffin.Payments.Tests.Support;

namespace Tiffin.Payments.Tests;

public sealed class FakePayments : IPaymentRepository
{
    private readonly List<Payment> items = [];

    public Task<Payment?> GetAsync(Guid id, string city, CancellationToken cancellationToken) =>
        Task.FromResult(items.Find(p => p.Id == id && p.City == city));

    public Task<Payment?> OfOrderAsync(Guid orderId, string city, CancellationToken cancellationToken) =>
        Task.FromResult(items.Find(p => p.OrderId == orderId && p.City == city));

    public void Add(Payment payment) => items.Add(payment);

    public IReadOnlyList<Payment> All => items;
}

/// <summary>The provider: answers as it is told, and remembers what it was asked.</summary>
public sealed class FakeGateway : IPaymentGateway
{
    public GatewayResult Answer { get; set; } = GatewayResult.Approved("PLA-TEST");

    public List<(string Key, decimal Amount, string Token)> Charges { get; } = [];

    public List<(string Key, string Reference)> Refunds { get; } = [];

    public Task<GatewayResult> AuthorizeAsync(string idempotencyKey, decimal amount, string currency, string paymentToken, CancellationToken cancellationToken)
    {
        Charges.Add((idempotencyKey, amount, paymentToken));
        return Task.FromResult(Answer);
    }

    public Task<GatewayResult> RefundAsync(string idempotencyKey, string providerReference, decimal amount, CancellationToken cancellationToken)
    {
        Refunds.Add((idempotencyKey, providerReference));
        return Task.FromResult(Answer.Outcome == GatewayOutcome.Approved ? GatewayResult.Approved("PLR-TEST") : Answer);
    }
}

/// <summary>The money of an order: a reference, a charge, and perhaps a refund; and a token that does not outlive its use.</summary>
public sealed class PaymentTests
{
    private static readonly Guid Event = Guid.NewGuid();
    private readonly FakePayments payments = new();
    private readonly FakeGateway gateway = new();
    private readonly RecordingPublisher publisher = new();
    private readonly FakeAudit audit = new();
    private readonly FakeClock clock = FakeClock.At2026();
    private readonly FakeTenant tehran = FakeTenant.Tehran();

    private async Task<Payment> Opened(decimal amount = 900_000m)
    {
        var reference = (await OpenPaymentHandler.Handle(
            new OpenPayment(Guid.NewGuid(), "tehran", "sara", "tok_of_sara", amount, "irr"), payments, new FakeUnitOfWork(), clock, default)).Value;
        return payments.All.Single(p => p.Id == reference.PaymentIntentId);
    }

    private Task Requested(Payment payment, decimal? amount = null, FakeTenant? tenant = null, Guid? intent = null) => PaymentProcessHandler.Handle(
        new PaymentRequested(Event, payment.OrderId, intent ?? payment.Id, amount ?? payment.Amount, "IRR", clock.UtcNow), payments, tenant ?? tehran,
        gateway, publisher, audit, new FakeUnitOfWork(), clock, NullLogger<PaymentRequested>.Instance, default);

    private Task RefundAsked(Payment payment) => PaymentProcessHandler.Handle(
        new RefundRequested(Event, payment.OrderId, "restaurant-refused", clock.UtcNow), payments, tehran, gateway, audit, new FakeUnitOfWork(), clock,
        NullLogger<RefundRequested>.Instance, default);

    [Fact]
    public async Task A_reference_is_given_for_a_token_and_asking_again_for_the_same_order_gives_the_same()
    {
        var command = new OpenPayment(Guid.NewGuid(), "tehran", "sara", "tok_of_sara", 900_000m, "irr");

        var first = (await OpenPaymentHandler.Handle(command, payments, new FakeUnitOfWork(), clock, default)).Value;
        var second = (await OpenPaymentHandler.Handle(command, payments, new FakeUnitOfWork(), clock, default)).Value;

        Assert.Equal(first.PaymentIntentId, second.PaymentIntentId);
        var payment = Assert.Single(payments.All);
        Assert.Equal((PaymentStatus.Pending, "IRR", clock.UtcNow + Payment.Lifetime), (payment.Status, payment.Currency, payment.ExpiresOnUtc));
    }

    [Fact]
    public async Task A_charge_erases_the_token_and_answers_the_order()
    {
        var payment = await Opened();

        await Requested(payment);

        Assert.Equal((PaymentStatus.Authorized, "PLA-TEST"), (payment.Status, payment.ProviderReference));
        Assert.Null(payment.PaymentToken);
        Assert.Equal(payment.OrderId, Assert.IsType<PaymentAuthorized>(Assert.Single(payment.IntegrationEvents)).OrderId);
        Assert.Equal((payment.OrderId.ToString(), 900_000m, "tok_of_sara"), Assert.Single(gateway.Charges));
        Assert.DoesNotContain("tok_of_sara", string.Join(' ', audit.Records.SelectMany(static r => r.Metadata!.Values)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_that_arrives_twice_asks_the_provider_once_and_repeats_the_answer()
    {
        var payment = await Opened();

        await Requested(payment);
        await Requested(payment);

        Assert.Single(gateway.Charges);
        Assert.Equal("PLA-TEST", publisher.Single<PaymentAuthorized>().ProviderReference);
    }

    [Fact]
    public async Task A_refusal_of_the_bank_is_answered_with_the_banks_code_and_erases_the_token()
    {
        var payment = await Opened();
        gateway.Answer = GatewayResult.Declined("INSUFFICIENT_FUNDS");

        await Requested(payment);

        Assert.Equal((PaymentStatus.Declined, "INSUFFICIENT_FUNDS"), (payment.Status, payment.DeclineCode));
        Assert.Null(payment.PaymentToken);
        Assert.IsType<PaymentDeclined>(Assert.Single(payment.IntegrationEvents));
    }

    [Fact]
    public async Task A_provider_that_does_not_answer_is_a_failure_to_try_again_and_the_token_is_kept_for_the_next_attempt()
    {
        var payment = await Opened();
        gateway.Answer = GatewayResult.Unavailable;

        var exception = await Assert.ThrowsAsync<ResultFailureException>(() => Requested(payment));

        Assert.Equal("PROVIDER_UNAVAILABLE", exception.Failure.Identity.Code);
        Assert.True(exception.Failure.Retry.IsRetryable);
        Assert.Equal((PaymentStatus.Pending, "tok_of_sara"), (payment.Status, payment.PaymentToken));
    }

    [Fact]
    public async Task An_amount_other_than_the_one_the_reference_was_given_for_is_declined_and_the_provider_is_not_asked()
    {
        var payment = await Opened(amount: 900_000m);

        await Requested(payment, amount: 9_000_000m);

        Assert.Equal((PaymentStatus.Declined, DeclineCodes.AmountMismatch), (payment.Status, payment.DeclineCode));
        Assert.Empty(gateway.Charges);
    }

    [Fact]
    public async Task A_reference_that_waited_longer_than_half_an_hour_is_declined()
    {
        var payment = await Opened();
        clock.UtcNow += Payment.Lifetime;

        await Requested(payment);

        Assert.Equal((PaymentStatus.Declined, DeclineCodes.ReferenceExpired), (payment.Status, payment.DeclineCode));
        Assert.Empty(gateway.Charges);
    }

    [Fact]
    public async Task A_reference_of_another_city_or_one_that_was_never_given_is_declined_as_unknown()
    {
        var payment = await Opened();

        await Requested(payment, tenant: FakeTenant.Istanbul());
        await Requested(payment, intent: Guid.NewGuid());

        Assert.Equal(2, publisher.Messages.OfType<PaymentDeclined>().Count(static d => d.DeclineCode == DeclineCodes.UnknownReference));
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Empty(gateway.Charges);
    }

    [Fact]
    public async Task What_was_charged_is_given_back_once()
    {
        var payment = await Opened();
        await Requested(payment);
        payment.ClearEvents();

        await RefundAsked(payment);
        await RefundAsked(payment);

        Assert.Equal((PaymentStatus.Refunded, "PLR-TEST"), (payment.Status, payment.RefundReference));
        Assert.Equal(($"refund-{payment.OrderId}", "PLA-TEST"), Assert.Single(gateway.Refunds));
        Assert.IsType<PaymentRefunded>(Assert.Single(payment.IntegrationEvents));
    }

    [Fact]
    public async Task A_refund_asked_before_the_charge_makes_sure_that_the_card_is_never_charged()
    {
        var payment = await Opened();

        await RefundAsked(payment);
        await Requested(payment);

        Assert.Equal(PaymentStatus.Voided, payment.Status);
        Assert.Null(payment.PaymentToken);
        Assert.Empty(gateway.Charges);
        Assert.Empty(gateway.Refunds);
        Assert.Empty(publisher.Messages);
    }

    [Fact]
    public async Task A_payment_prints_no_token()
    {
        var payment = await Opened();

        Assert.DoesNotContain("tok_of_sara", payment.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("tok_of_sara", new OpenPayment(payment.OrderId, "tehran", "sara", "tok_of_sara", 1, "IRR").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Rules_of_the_payment()
    {
        var now = clock.UtcNow;
        Assert.Equal("AMOUNT_NOT_POSITIVE", Rules.Broken(() => Payment.Open(Guid.NewGuid(), "tehran", "sara", "tok", 0m, "IRR", now)));

        var payment = Payment.Open(Guid.NewGuid(), "tehran", "sara", "tok", 10m, "IRR", now);
        Assert.Equal("PAYMENT_NOT_CHARGED", Rules.Broken(() => payment.Refund("PLR-1", now)));
        payment.Authorize("PLA-1", now);
        Assert.Equal("PAYMENT_NOT_PENDING", Rules.Broken(() => payment.Authorize("PLA-2", now)));
        Assert.Equal("PAYMENT_NOT_PENDING", Rules.Broken(() => payment.Void()));
    }
}
