using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MPCore.Application.Querying;
using Wolverine;
using Tiffin.Restaurants.Api.Rest.Endpoints;
using Tiffin.Restaurants.Application.Views;

namespace Tiffin.Restaurants.Tests;

public sealed class EndpointContractTests
{
    [Theory]
    [InlineData("ListRestaurants", typeof(Page<RestaurantSummary>), 200)]
    [InlineData("GetMenu", typeof(MenuView), 200)]
    [InlineData("RegisterRestaurant", typeof(MenuView), 201)]
    [InlineData("SetMenuItem", typeof(MenuView), 200)]
    [InlineData("OpenRestaurant", typeof(MenuView), 200)]
    [InlineData("CloseRestaurant", typeof(MenuView), 200)]
    [InlineData("ShowRestaurantPicture", typeof(MenuView), 200)]
    [InlineData("QuoteOrder", typeof(QuoteView), 200)]
    public async Task Real_endpoint_metadata_describes_its_existing_success_body_and_status(string operation, Type body, int status)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IMessageBus>(_ => throw new InvalidOperationException("Contract tests never invoke business operations."));
        await using var app = builder.Build();
        app.MapRestaurantEndpoints();
        var endpoint = Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints),
            e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == operation);
        var produces = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
        Assert.Contains(produces, metadata => metadata.StatusCode == status && metadata.Type == body &&
            (body == typeof(void) ? !metadata.ContentTypes.Any() : metadata.ContentTypes.Contains("application/json")));
    }
}
