using NetArchTest.Rules;
using Spotlys.Domain.Pricing;

namespace Spotlys.Domain.Tests;

/// <summary>
/// Enforces the dependency rule from docs/ARCHITECTURE.md §2: Domain references nothing;
/// Application references only Domain; Infrastructure/Api/Ingestion reference Application.
/// Fails the build on violation, per docs/ENGINEERING.md §2 -- not left to code review.
/// </summary>
public class ArchitectureTests
{
    private static readonly string DomainAssembly = typeof(PriceArea).Assembly.GetName().Name!;

    [Fact]
    public void Domain_should_not_depend_on_any_other_layer()
    {
        var result = Types.InAssembly(typeof(PriceArea).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Spotlys.Application", "Spotlys.Infrastructure", "Spotlys.Api", "Spotlys.Ingestion")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_should_not_depend_on_infrastructure_or_hosts()
    {
        var applicationAssembly = System.Reflection.Assembly.Load("Spotlys.Application");

        var result = Types.InAssembly(applicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("Spotlys.Infrastructure", "Spotlys.Api", "Spotlys.Ingestion")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(TestResult result) =>
        $"Violations in {DomainAssembly} dependency rules: "
        + string.Join(", ", result.FailingTypeNames ?? []);
}
