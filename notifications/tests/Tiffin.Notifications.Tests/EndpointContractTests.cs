using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MPCore.Application.Querying;
using Wolverine;
using Tiffin.Notifications.Api.Rest.Endpoints;
using Tiffin.Notifications.Application.Views;

namespace Tiffin.Notifications.Tests;

public sealed class EndpointContractTests
{
    [Theory]
    [InlineData("ListMyNotifications", typeof(Page<NotificationView>), 200)]
    [InlineData("MarkNotificationRead", typeof(NotificationView), 200)]
    public async Task Real_endpoint_metadata_describes_its_existing_success_body_and_status(string operation, Type body, int status)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IMessageBus>(_ => throw new InvalidOperationException("Contract tests never invoke business operations."));
        builder.Services.AddSingleton<MPCore.Localization.IMessageCatalog>(_ => throw new InvalidOperationException("Contract tests never invoke business operations."));
        await using var app = builder.Build();
        app.MapNotificationEndpoints();
        var endpoint = Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints),
            e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == operation);
        var produces = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
        Assert.Contains(produces, metadata => metadata.StatusCode == status && metadata.Type == body && metadata.ContentTypes.Contains("application/json"));
    }
}
