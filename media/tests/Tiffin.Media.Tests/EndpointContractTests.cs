using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tiffin.Media.Api.Rest.Endpoints;
using Tiffin.Media.Application.Views;
using Wolverine;

namespace Tiffin.Media.Tests;

public sealed class EndpointContractTests
{
    [Theory]
    [InlineData("ReserveUpload", typeof(UploadTicket), 201)]
    [InlineData("ConfirmUpload", typeof(MediaView), 200)]
    [InlineData("GetMedia", typeof(MediaView), 200)]
    [InlineData("DeleteMedia", typeof(MediaView), 200)]
    public async Task Real_endpoint_metadata_describes_its_existing_success_body_and_status(string operation, Type body, int status)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IMessageBus>(_ => throw new InvalidOperationException("Contract tests never invoke business operations."));
        await using var app = builder.Build();
        app.MapMediaEndpoints();
        var endpoint = Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints),
            e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == operation);
        var produces = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
        Assert.Contains(produces, metadata => metadata.StatusCode == status && metadata.Type == body &&
            metadata.ContentTypes.Contains("application/json"));
    }
}
