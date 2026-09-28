using System.Globalization;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using MPCore.Application.Results;
using Tiffin.Payments.Api.Hosting;
using Tiffin.Payments.Application;
using Tiffin.Payments.Application.Commands;
using Wolverine;
using ResultFailureException = MPCore.Application.Results.ResultFailureException;

namespace Tiffin.Payments.Api.Grpc.Services;

/// <summary>References for cards, for the services of the platform.</summary>
/// <remarks>
/// The method reads the request, invokes one command through Wolverine, and maps the answer. A failure is
/// thrown as <see cref="ResultFailureException"/>, and MP Core's gRPC failure handling turns it into the
/// native status with rich error details: an amount that is not a number is <c>InvalidArgument</c>.
/// </remarks>
[Authorize(Policy = PaymentsPolicies.Service)]
public sealed class PaymentIntentsService(IMessageBus bus) : PaymentIntents.PaymentIntentsBase
{
    /// <inheritdoc />
    public override async Task<CreateIntentReply> CreateIntent(CreateIntentRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (!Guid.TryParse(request.OrderId, out var orderId))
        {
            throw new ResultFailureException(PaymentFailures.Invalid("order_id", "NOT_A_UUID", "payments.order_id_invalid"));
        }

        if (!decimal.TryParse(request.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            throw new ResultFailureException(PaymentFailures.Invalid("amount", "NOT_A_NUMBER", "payments.amount_invalid"));
        }

        var reference = (await bus.InvokeAsync<Result<PaymentReference>>(
            new OpenPayment(orderId, request.City, request.CustomerId, request.PaymentToken, amount, request.Currency),
            context.CancellationToken).ConfigureAwait(false)).ValueOrThrow();
        return new CreateIntentReply
        {
            PaymentIntentId = reference.PaymentIntentId.ToString(),
            ExpiresOn = Timestamp.FromDateTimeOffset(reference.ExpiresOnUtc)
        };
    }
}
