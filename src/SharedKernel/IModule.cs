using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyApp.SharedKernel;

/// <summary>
/// The contract every module implements so the host can wire it up
/// without knowing anything about its internals (ADR-001).
/// </summary>
public interface IModule
{
    /// <summary>Short name, also used as the module's URL segment and database schema.</summary>
    string Name { get; }

    /// <summary>Register the module's services.</summary>
    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Map the module's endpoints under <c>/api/{Name}</c>.</summary>
    void MapEndpoints(IEndpointRouteBuilder group);
}
