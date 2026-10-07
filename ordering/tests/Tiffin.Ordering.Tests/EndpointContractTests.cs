using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MPCore.Application.Querying;
using Wolverine;
using Tiffin.Ordering.Api.Rest.Endpoints;
using Tiffin.Ordering.Application.Views;

namespace Tiffin.Ordering.Tests;

public sealed class EndpointContractTests
{
    [Theory]
    [InlineData("PlaceOrder", typeof(OrderAccepted), 202)]
    [InlineData("ListMyOrders", typeof(Page<OrderSummary>), 200)]
    [InlineData("GetOrder", typeof(OrderView), 200)]
    [InlineData("CancelOrder", typeof(OrderView), 200)]
    public async Task Real_endpoint_metadata_describes_its_existing_success_body_and_status(string operation, Type body, int status)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IMessageBus>(_ => throw new InvalidOperationException("Contract tests never invoke business operations."));
        builder.Services.AddSingleton<MPCore.Application.Idempotency.IIdempotentExecutor>(_ => throw new InvalidOperationException("Contract tests never invoke business operations."));
        await using var app = builder.Build();
        app.MapOrderEndpoints();
        var endpoint = Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints),
            e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == operation);
        var produces = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
        Assert.Contains(produces, metadata => metadata.StatusCode == status && metadata.Type == body && metadata.ContentTypes.Contains("application/json"));
    }
}
