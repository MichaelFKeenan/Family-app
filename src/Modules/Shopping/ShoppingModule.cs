using FamilyApp.SharedKernel;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyApp.Modules.Shopping;

/// <summary>The permanent shopping list (ADR-010). Built in step 7.</summary>
public sealed class ShoppingModule : IModule
{
    public string Name => "shopping";

    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder group) { }
}
