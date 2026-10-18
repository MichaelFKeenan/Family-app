using System.Reflection;
using FamilyApp.Mcp;
using FamilyApp.Modules.Households;
using FamilyApp.Modules.Identity;
using FamilyApp.Modules.Points;
using FamilyApp.Modules.Shopping;
using FamilyApp.Modules.Tasks;
using FamilyApp.SharedKernel;

namespace FamilyApp.Architecture.Tests;

/// <summary>
/// Module boundaries are enforced by these tests, not by the network (ADR-001).
/// Modules talk to each other only through domain events defined in SharedKernel (ADR-004).
/// </summary>
public class ModuleBoundaryTests
{
    private const string ModulePrefix = "FamilyApp.Modules.";

    private static readonly Assembly[] Modules =
    [
        typeof(IdentityModule).Assembly,
        typeof(HouseholdsModule).Assembly,
        typeof(TasksModule).Assembly,
        typeof(ShoppingModule).Assembly,
        typeof(PointsModule).Assembly,
    ];

    public static TheoryData<string> ModuleNames()
    {
        var data = new TheoryData<string>();
        foreach (var module in Modules)
        {
            data.Add(module.GetName().Name!);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_does_not_reference_another_module(string moduleName)
    {
        var module = Modules.Single(m => m.GetName().Name == moduleName);

        var offending = module.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith(ModulePrefix, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            offending.Count == 0,
            $"{moduleName} references {string.Join(", ", offending)}. Use a domain event instead.");
    }

    [Fact]
    public void SharedKernel_does_not_reference_any_module()
    {
        var offending = typeof(IModule).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith(ModulePrefix, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offending);
    }

    [Fact]
    public void Mcp_server_uses_only_the_http_api()
    {
        // The MCP server is just another API client (ADR-040): going through HTTP gives agents the same
        // checks as people. Referencing the modules, the host or SharedKernel would bypass them.
        var offending = typeof(FamilyApi).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("FamilyApp.", StringComparison.Ordinal))
            .ToList();

        Assert.True(offending.Count == 0, $"FamilyApp.Mcp references {string.Join(", ", offending)}. Call the API instead.");
    }

    [Fact]
    public void Every_module_exposes_exactly_one_IModule()
    {
        foreach (var module in Modules)
        {
            var entryPoints = module.GetTypes()
                .Where(type => typeof(IModule).IsAssignableFrom(type) && type is { IsClass: true, IsAbstract: false })
                .ToList();

            Assert.Single(entryPoints);
        }
    }
}
