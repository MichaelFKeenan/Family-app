using FamilyApp.SharedKernel;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyApp.Modules.Tasks;

/// <summary>Tasks, categories and the board. Built in step 4.</summary>
public sealed class TasksModule : IModule
{
    public string Name => "tasks";

    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder group) { }
}
