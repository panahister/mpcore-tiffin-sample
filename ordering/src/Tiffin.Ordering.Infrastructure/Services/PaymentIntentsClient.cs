using System.Globalization;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using MPCore.Application.Results;
using MPCore.Tenancy;
using Tiffin.Ordering.Application;
using Tiffin.Ordering.Application.Ports;
using Tiffin.Ordering.Infrastructure.Payments;

namespace Tiffin.Ordering.Infrastructure.Services;

/// <summary>The adapter for the Payments service: one call, over gRPC, as this service itself.</summary>
/// <remarks>
/// <para>
/// The client is generated from a copy of Payments' contract (<c>Protos/tiffin_payments.proto</c>) and
/// carries this service's own token, like the REST client beside it: a gRPC client is built by the same
/// factory and takes the same handlers.
/// </para>
/// <para>
/// A service's token names no city, so the city travels in the request. Payments believes it because it
/// believes the caller: only a service of the platform may call.
/// </para>
/// </remarks>
/// <remarks>
/// The generated client is built here, on a channel over the named HTTP client, and is not a constructor
/// parameter. Wolverine writes the code that builds a handler's dependencies and refuses a registration it
/// cannot read: the gRPC client factory registers a generated client with a lambda, and its own
/// implementation is not public. <see cref="IHttpClientFactory"/> is, and it is the same factory that gives
/// the REST client its resilience and its identity.
/// </remarks>
public sealed class PaymentIntentsClient(IHttpClientFactory clients, ITenantContext tenant, ILogger<PaymentIntentsClient> logger)
    : IPaymentIntents
{
    public const string ClientName = "payments";

    /// <summary>How long the customer waits for Payments before being told to try again.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    public async Task<Result<Guid>> CreateAsync(
        Guid orderId, string customerId, string paymentToken, decimal amount, string currency, CancellationToken cancellationToken)
    {
        try
        {
            // A channel is a thin thing over the client it is given: the connections belong to the factory's
            // handler, which outlives the channel.
            var http = clients.CreateClient(ClientName);
            using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new GrpcChannelOptions { HttpClient = http });
            var payments = new PaymentIntents.PaymentIntentsClient(channel);
            var reply = await payments.CreateIntentAsync(
                new CreateIntentRequest
                {
                    OrderId = orderId.ToString(),
                    CustomerId = customerId,
                    City = tenant.TenantId ?? string.Empty,
                    PaymentToken = paymentToken,
                    Amount = amount.ToString(CultureInfo.InvariantCulture),
                    Currency = currency
                },
                deadline: DateTime.UtcNow.Add(Deadline), cancellationToken: cancellationToken).ConfigureAwait(false);
            return Guid.TryParse(reply.PaymentIntentId, out var intent)
                ? Result<Guid>.Success(intent)
                : Unavailable(orderId, "an answer without a reference");
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.InvalidArgument)
        {
            return Result<Guid>.FromFailure(OrderingFailures.PaymentTokenRefused());
        }
        catch (RpcException exception)
        {
            return Unavailable(orderId, exception.StatusCode.ToString());
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException
                                              or Polly.CircuitBreaker.BrokenCircuitException
                                              or Polly.Timeout.TimeoutRejectedException)
        {
            return Unavailable(orderId, exception.GetType().Name);
        }
    }

    private Result<Guid> Unavailable(Guid orderId, string what)
    {
        // Never the request: it holds the card's token.
        logger.LogWarning("Payments did not give a reference for order {OrderId}: {What}", orderId, what);
        return Result<Guid>.FromFailure(OrderingFailures.PaymentsUnavailable());
    }
}
