using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MPCore.Application.Results;
using Tiffin.Ordering.Application;
using Tiffin.Ordering.Application.Ports;

namespace Tiffin.Ordering.Infrastructure.Services;

/// <summary>The adapter for the Restaurants service: one question, asked over REST, as this service itself.</summary>
/// <remarks>
/// <para>
/// The client is a named one (<see cref="ClientName"/>), built in <c>DependencyInjection</c> with MP Core's
/// resilience pipeline and with this service's own identity: every request carries a token the identity
/// provider issued to the Ordering service, not the customer's.
/// </para>
/// <para>
/// Restaurants answers a failure as Problem Details (RFC 9457). A rule it refused the order for (closed,
/// an item not on sale) is passed on to the customer under Restaurants' own code. Anything that is not an
/// answer, a timeout, an open circuit, a refused token, is "unavailable", which the customer may retry.
/// </para>
/// </remarks>
public sealed class RestaurantQuotesClient(IHttpClientFactory clients, ILogger<RestaurantQuotesClient> logger) : IRestaurantQuotes
{
    public const string ClientName = "restaurants";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<RestaurantQuote>> QuoteAsync(
        Guid restaurantId, IReadOnlyList<(string Code, int Quantity)> lines, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var client = clients.CreateClient(ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"v1/restaurants/{restaurantId}/quotes")
        {
            Content = JsonContent.Create(new QuoteRequest([.. lines.Select(static l => new QuoteRequestLine(l.Code, l.Quantity))]), options: Json)
        };

        // The answer is for the customer, so it is asked for in the customer's language.
        if (System.Globalization.CultureInfo.CurrentUICulture.Name is { Length: > 0 } language)
        {
            request.Headers.AcceptLanguage.ParseAdd(language);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var quote = await response.Content.ReadFromJsonAsync<QuoteAnswer>(Json, cancellationToken).ConfigureAwait(false);
                    return quote is null
                        ? Unavailable("an empty answer")
                        : Result<RestaurantQuote>.Success(new RestaurantQuote(
                            quote.RestaurantId, quote.RestaurantName, quote.City, quote.Currency,
                            [.. quote.Lines.Select(static l => new RestaurantQuoteLine(l.Code, l.Name, l.UnitPrice, l.Quantity))], quote.Total));
                case HttpStatusCode.NotFound:
                    return Result<RestaurantQuote>.FromFailure(OrderingFailures.RestaurantNotFound());
                case HttpStatusCode.UnprocessableEntity:
                    var problem = await response.Content.ReadFromJsonAsync<Problem>(Json, cancellationToken).ConfigureAwait(false);
                    return Result<RestaurantQuote>.FromFailure(OrderingFailures.RefusedByRestaurant(problem?.ErrorCode ?? "REFUSED", problem?.Detail));
                default:
                    return Unavailable($"HTTP {(int)response.StatusCode}");
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or JsonException
                                              or Polly.CircuitBreaker.BrokenCircuitException
                                              or Polly.Timeout.TimeoutRejectedException
                                              or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            logger.LogWarning(exception, "Restaurants did not answer the quote for {RestaurantId}", restaurantId);
            return Result<RestaurantQuote>.FromFailure(OrderingFailures.RestaurantsUnavailable());
        }
    }

    private Result<RestaurantQuote> Unavailable(string what)
    {
        logger.LogWarning("Restaurants answered a quote with {What}", what);
        return Result<RestaurantQuote>.FromFailure(OrderingFailures.RestaurantsUnavailable());
    }

    private sealed record QuoteRequest(IReadOnlyList<QuoteRequestLine> Lines);

    private sealed record QuoteRequestLine(string Code, int Quantity);

    private sealed record QuoteAnswer(
        Guid RestaurantId, string RestaurantName, string City, string Currency, IReadOnlyList<QuoteAnswerLine> Lines, decimal Total);

    private sealed record QuoteAnswerLine(string Code, string Name, decimal UnitPrice, int Quantity);

    private sealed record Problem(string? ErrorCode, string? Detail);
}
