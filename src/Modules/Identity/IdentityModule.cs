using FamilyApp.SharedKernel;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyApp.Modules.Identity;

/// <summary>Users, login identities and sign-in (ADR-007, ADR-008). Built in step 2.</summary>
public sealed class IdentityModule : IModule
{
    public string Name => "identity";

    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder group) { }
}
