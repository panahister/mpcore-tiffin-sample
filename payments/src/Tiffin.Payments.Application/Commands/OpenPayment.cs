using MPCore.Application.Messaging;
using MPCore.Application.Results;
using MPCore.Application.Time;
using MPCore.Persistence.Abstractions;
using Tiffin.Payments.Application.Ports;
using Tiffin.Payments.Domain;

namespace Tiffin.Payments.Application.Commands;

/// <summary>A service hands over the token of a customer's card, for one order, and receives a reference.</summary>
/// <remarks>
/// Wolverine logs a message whose handling failed by printing it, and a record prints every property. The
/// token is a credential, so this record says what it prints.
/// </remarks>
public sealed record OpenPayment(Guid OrderId, string City, string CustomerId, string PaymentToken, decimal Amount, string Currency)
    : ICommand<Result<PaymentReference>>
{
    public override string ToString() => $"{nameof(OpenPayment)} {{ OrderId = {OrderId}, City = {City}, Amount = {Amount} {Currency} }}";
}

public sealed record PaymentReference(Guid PaymentIntentId, DateTimeOffset ExpiresOnUtc);

/// <summary>
/// Stores the token and answers with a reference. Asking twice for the same order answers with the same
/// reference: the caller's retry after a lost answer opens nothing new.
/// </summary>
public static class OpenPaymentHandler
{
    public static async Task<Result<PaymentReference>> Handle(
        OpenPayment command, IPaymentRepository payments, IUnitOfWork unitOfWork, IClock clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(payments);

        var existing = await payments.OfOrderAsync(command.OrderId, command.City, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Result<PaymentReference>.Success(new PaymentReference(existing.Id, existing.ExpiresOnUtc));
        }

        var payment = Payment.Open(command.OrderId, command.City, command.CustomerId, command.PaymentToken, command.Amount, command.Currency, clock.UtcNow);
        payments.Add(payment);
        return Result<PaymentReference>.Success(new PaymentReference(payment.Id, payment.ExpiresOnUtc));
    }
}
