using FamilyApp.SharedKernel;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyApp.Modules.Households;

/// <summary>Households, memberships and invites; the tenant boundary (ADR-009). Built in step 3.</summary>
public sealed class HouseholdsModule : IModule
{
    public string Name => "households";

    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder group) { }
}
