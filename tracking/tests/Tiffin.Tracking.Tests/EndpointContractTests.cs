using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Tiffin.Tracking.Api.Rest.Endpoints;
using Tiffin.Tracking.Application.Views;

namespace Tiffin.Tracking.Tests;

public sealed class EndpointContractTests
{
    [Theory]
    [InlineData("ReportPosition", typeof(void), 204)]
    [InlineData("GetTracking", typeof(TrackingView), 200)]
    public async Task Real_endpoint_metadata_describes_its_existing_success_body_and_status(string operation, Type body, int status)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IMessageBus>(_ => throw new InvalidOperationException("Contract tests never invoke business operations."));
        await using var app = builder.Build();
        app.MapTrackingEndpoints();
        var endpoint = Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints),
            e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == operation);
        var produces = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
        Assert.Contains(produces, metadata => metadata.StatusCode == status && metadata.Type == body &&
            (body == typeof(void) ? !metadata.ContentTypes.Any() : metadata.ContentTypes.Contains("application/json")));
    }
}
