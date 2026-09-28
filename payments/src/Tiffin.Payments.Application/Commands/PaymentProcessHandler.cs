using System.Globalization;
using Microsoft.Extensions.Logging;
using MPCore.Application.Results;
using MPCore.Application.Time;
using MPCore.Audit;
using MPCore.Messaging.Abstractions;
using MPCore.Persistence.Abstractions;
using MPCore.Tenancy;
using Tiffin.Payments.Application.Contracts;
using Tiffin.Payments.Application.Ports;
using Tiffin.Payments.Domain;
using Tiffin.Payments.Domain.Events;

namespace Tiffin.Payments.Application.Commands;

/// <summary>What Payments does when the order asks: charge the card, or give the money back.</summary>
/// <remarks>
/// <para>
/// <b>The provider is called once per order, however often the request arrives.</b> The payment remembers
/// its outcome, and a repeated request is answered from memory. The call itself carries an
/// <c>Idempotency-Key</c>, the order's identity: MP Core's resilient client retries a call that timed out,
/// and the provider must recognise the retry as the same charge.
/// </para>
/// <para>
/// <b>When the provider cannot be reached</b> even after the client's retries, the handler throws a
/// retryable failure. The host's error policy obeys its directive: redelivery with a cooldown, then the
/// error queue, and then the order is told that the card was not charged
/// (<c>Hosting/GivenUpMessages.cs</c>).
/// </para>
/// <para>
/// These handlers run from a queue. MP Core opens the city of the message that arrived, so a payment is
/// only found by a request from its own city, and the audit trail names the city.
/// </para>
/// </remarks>
public static class PaymentProcessHandler
{
    public static async Task Handle(
        PaymentRequested message, IPaymentRepository payments, ITenantContext tenant, IPaymentGateway gateway, IMessagePublisher publisher,
        IBusinessAuditRecorder audit, IUnitOfWork unitOfWork, IClock clock, ILogger<PaymentRequested> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(audit);

        var now = clock.UtcNow;
        var payment = await payments.GetAsync(message.PaymentIntentId, CityOf(tenant, message.OrderId), cancellationToken).ConfigureAwait(false);
        if (payment is null || payment.OrderId != message.OrderId)
        {
            // A reference this service never gave, or gave for another order or another city.
            await publisher.PublishAsync(new PaymentDeclined(message.OrderId, DeclineCodes.UnknownReference, now), cancellationToken).ConfigureAwait(false);
            return;
        }

        // A repeated request repeats the answer; a payment that will never be charged says nothing.
        switch (payment.Status)
        {
            case PaymentStatus.Authorized:
                await publisher.PublishAsync(new PaymentAuthorized(payment.OrderId, payment.ProviderReference!, now), cancellationToken).ConfigureAwait(false);
                return;
            case PaymentStatus.Declined:
                await publisher.PublishAsync(new PaymentDeclined(payment.OrderId, payment.DeclineCode!, now), cancellationToken).ConfigureAwait(false);
                return;
            case PaymentStatus.Voided or PaymentStatus.Refunded:
                logger.LogInformation("Payment for order {OrderId} is {Status}; not charging", payment.OrderId, payment.Status);
                return;
        }

        if (payment.HasExpired(now) || payment.PaymentToken is null)
        {
            payment.Decline(DeclineCodes.ReferenceExpired, now);
            return;
        }

        if (payment.Amount != message.Amount || !string.Equals(payment.Currency, message.Currency, StringComparison.OrdinalIgnoreCase))
        {
            // The card was shown to the customer for one amount, and the order asks for another.
            payment.Decline(DeclineCodes.AmountMismatch, now);
            return;
        }

        var result = await gateway.AuthorizeAsync(payment.OrderId.ToString(), payment.Amount, payment.Currency, payment.PaymentToken, cancellationToken)
            .ConfigureAwait(false);
        switch (result.Outcome)
        {
            case GatewayOutcome.Approved:
                payment.Authorize(result.Reference!, now);
                await audit.RecordAsync(
                    "payments", "payment-authorized", nameof(Payment), payment.OrderId.ToString(),
                    new Dictionary<string, string>
                    {
                        ["amount"] = payment.Amount.ToString(CultureInfo.InvariantCulture),
                        ["currency"] = payment.Currency,
                        ["provider_reference"] = result.Reference!
                    }, cancellationToken).ConfigureAwait(false);
                break;
            case GatewayOutcome.Declined:
                payment.Decline(result.Reference ?? "DECLINED", now);
                await audit.RecordAsync(
                    "payments", "payment-declined", nameof(Payment), payment.OrderId.ToString(),
                    new Dictionary<string, string> { ["decline_code"] = payment.DeclineCode! }, cancellationToken).ConfigureAwait(false);
                break;
            default:
                logger.LogWarning("The payment provider did not answer for order {OrderId}; the request will be retried", payment.OrderId);
                throw new ResultFailureException(PaymentFailures.ProviderUnavailable());
        }

        logger.LogInformation("Payment for order {OrderId}: {Outcome}", payment.OrderId, result.Outcome);
    }

    public static async Task Handle(
        RefundRequested message, IPaymentRepository payments, ITenantContext tenant, IPaymentGateway gateway, IBusinessAuditRecorder audit,
        IUnitOfWork unitOfWork, IClock clock, ILogger<RefundRequested> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(audit);

        var payment = await payments.OfOrderAsync(message.OrderId, CityOf(tenant, message.OrderId), cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            logger.LogInformation("No payment exists for order {OrderId}; nothing to give back", message.OrderId);
            return;
        }

        switch (payment.Status)
        {
            case PaymentStatus.Pending:
                // Cancelled before the charge. A request to charge that arrives after this finds the
                // payment voided and does nothing.
                payment.Void();
                logger.LogInformation("Payment for order {OrderId} voided: {Reason}", payment.OrderId, message.Reason);
                return;
            case PaymentStatus.Authorized:
                break;
            default:
                logger.LogInformation("Payment for order {OrderId} is {Status}; nothing to give back", payment.OrderId, payment.Status);
                return;
        }

        var result = await gateway.RefundAsync($"refund-{payment.OrderId}", payment.ProviderReference!, payment.Amount, cancellationToken).ConfigureAwait(false);
        if (result.Outcome != GatewayOutcome.Approved)
        {
            logger.LogWarning("The payment provider did not refund order {OrderId}; the request will be retried", payment.OrderId);
            throw new ResultFailureException(PaymentFailures.ProviderUnavailable());
        }

        payment.Refund(result.Reference!, clock.UtcNow);
        await audit.RecordAsync(
            "payments", "payment-refunded", nameof(Payment), payment.OrderId.ToString(),
            new Dictionary<string, string>
            {
                ["amount"] = payment.Amount.ToString(CultureInfo.InvariantCulture),
                ["reason"] = message.Reason,
                ["refund_reference"] = result.Reference!
            }, cancellationToken).ConfigureAwait(false);
    }

    private static string CityOf(ITenantContext tenant, Guid orderId)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return tenant.TenantId
            ?? throw new InvalidOperationException($"The request for order {orderId} names no city; every message of the platform carries its tenant.");
    }
}
