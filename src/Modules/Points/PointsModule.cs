using FamilyApp.SharedKernel;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyApp.Modules.Points;

/// <summary>Points ledger and leaderboard (ADR-006). Built in step 6.</summary>
public sealed class PointsModule : IModule
{
    public string Name => "points";

    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder group) { }
}
