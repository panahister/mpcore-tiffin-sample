using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MPCore.Application.Querying;
using Wolverine;
using Tiffin.Kitchen.Api.Rest.Endpoints;
using Tiffin.Kitchen.Application.Views;

namespace Tiffin.Kitchen.Tests;

public sealed class EndpointContractTests
{
    [Theory]
    [InlineData("ListTickets", typeof(Page<TicketView>), 200)]
    [InlineData("AcceptTicket", typeof(TicketView), 200)]
    [InlineData("RejectTicket", typeof(TicketView), 200)]
    public async Task Real_endpoint_metadata_describes_its_existing_success_body_and_status(string operation, Type body, int status)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IMessageBus>(_ => throw new InvalidOperationException("Contract tests never invoke business operations."));
        await using var app = builder.Build();
        app.MapKitchenEndpoints();
        var endpoint = Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints),
            e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == operation);
        var produces = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
        Assert.Contains(produces, metadata => metadata.StatusCode == status && metadata.Type == body &&
            (body == typeof(void) ? !metadata.ContentTypes.Any() : metadata.ContentTypes.Contains("application/json")));
    }
}
