using System.Reflection;

using NetArchTest.Rules;

using Shouldly;

namespace Architecture.Tests;

/// <summary>
/// docs/architecture.md "Forbidden dependencies" 1-4. Rule 5 (Angular feature isolation) is
/// enforced by the ESLint boundaries rule in web/, not here.
/// </summary>
public sealed class LayerBoundaryTests
{
    private static readonly Assembly DomainAssembly = Assembly.Load("Domain");
    private static readonly Assembly ApplicationAssembly = Assembly.Load("Application");
    private static readonly Assembly InfrastructureAssembly = Assembly.Load("Infrastructure");
    private static readonly Assembly ApiAssembly = Assembly.Load("Api");

    private static readonly string[] InfrastructureNamespaces =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.DependencyInjection",
        "Npgsql",
        "MediatR",
        "System.Net.Http"
    ];

    [Fact]
    public void Domain_ShouldNotDependOnAnyOtherLayerOrInfrastructure_Rule1()
    {
        string[] forbidden = [.. InfrastructureNamespaces, "Application", "Infrastructure", "Api"];

        AssertNoDependency(DomainAssembly, forbidden);
    }

    [Fact]
    public void Application_ShouldDependOnDomainOnly_Rule2()
    {
        string[] forbidden = [.. InfrastructureNamespaces, "Infrastructure", "Api"];

        AssertNoDependency(ApplicationAssembly, forbidden);
    }

    [Fact]
    public void Infrastructure_ShouldNotDependOnApi_Rule3()
    {
        AssertNoDependency(InfrastructureAssembly, ["Api"]);
    }

    /// <summary>
    /// Rule 4 mechanically: the HTTP boundary never exposes a domain (EF-mapped) entity, so no Api
    /// type may depend on a Domain type at all - it speaks Application contracts only. Api is also
    /// kept free of EF Core itself, which is why database initialisation lives in Infrastructure.
    /// </summary>
    [Fact]
    public void Api_ShouldNotDependOnDomainTypesOrEfCore_Rule4()
    {
        AssertNoDependency(ApiAssembly, ["Domain", "Microsoft.EntityFrameworkCore", "Npgsql"]);
    }

    /// <summary>
    /// Guards against a vacuous pass: an empty assembly satisfies every rule above.
    /// </summary>
    [Fact]
    public void EveryLayerAssembly_ShouldContainTypes_SoTheRulesCannotPassVacuously()
    {
        foreach (Assembly assembly in new[] { DomainAssembly, ApplicationAssembly, InfrastructureAssembly, ApiAssembly })
        {
            // Read through NetArchTest (metadata only) rather than Assembly.GetTypes(), which would
            // need every referenced assembly loadable at test time.
            Types.InAssembly(assembly)
                .GetTypes()
                .Any()
                .ShouldBeTrue($"{assembly.GetName().Name} has no types - boundary rules would pass vacuously.");
        }
    }

    private static void AssertNoDependency(Assembly assembly, string[] forbiddenNamespaces)
    {
        NetArchTest.Rules.TestResult result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenNamespaces)
            .GetResult();

        string offenders = result.FailingTypeNames is null
            ? string.Empty
            : string.Join(", ", result.FailingTypeNames);

        result.IsSuccessful.ShouldBeTrue(
            $"{assembly.GetName().Name} must not depend on [{string.Join(", ", forbiddenNamespaces)}]. " +
            $"Offending types: {offenders}");
    }
}
