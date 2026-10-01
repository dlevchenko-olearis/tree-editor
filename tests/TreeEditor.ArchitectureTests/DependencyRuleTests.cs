using System.Reflection;
using NetArchTest.Rules;

namespace TreeEditor.ArchitectureTests;

/// <summary>Enforces the dependency rule between the backend layers.</summary>
public sealed class DependencyRuleTests
{
    private static readonly Assembly Domain = Assembly.Load("TreeEditor.Domain");
    private static readonly Assembly Application = Assembly.Load("TreeEditor.Application");
    private static readonly Assembly Infrastructure = Assembly.Load("TreeEditor.Infrastructure");

    [Fact]
    public void Domain_references_only_the_base_class_library()
    {
        var foreign = Domain.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !IsBaseClassLibrary(name))
            .ToList();

        Assert.Empty(foreign);
    }

    [Fact]
    public void Domain_types_depend_on_no_other_layer_or_framework()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(
                "TreeEditor.Application",
                "TreeEditor.Infrastructure",
                "TreeEditor.Api",
                "Microsoft")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_depends_neither_on_infrastructure_nor_on_aspnet()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny(
                "TreeEditor.Infrastructure",
                "TreeEditor.Api",
                "Microsoft.AspNetCore",
                "Microsoft.EntityFrameworkCore",
                "Npgsql")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_assembly_does_not_reference_outer_layers()
    {
        var forbidden = Application.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("TreeEditor.Infrastructure", StringComparison.Ordinal)
                || name.StartsWith("TreeEditor.Api", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || name.StartsWith("Npgsql", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_the_api()
    {
        var result = Types.InAssembly(Infrastructure)
            .ShouldNot()
            .HaveDependencyOnAny("TreeEditor.Api", "Microsoft.AspNetCore")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static bool IsBaseClassLibrary(string assemblyName) =>
        assemblyName == "netstandard"
        || assemblyName == "mscorlib"
        || assemblyName.StartsWith("System", StringComparison.Ordinal);

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
