using System.Reflection;
using NetArchTest.Rules;

namespace Kanabol.Api.Tests.Architecture;

/// <summary>The dependency rule from SPEC section 4.1: dependencies only point inward.</summary>
public sealed class ArchitectureTests
{
    private const string EntityFramework = "Microsoft.EntityFrameworkCore";
    private const string Application = "Kanabol.Application";
    private const string Infrastructure = "Kanabol.Infrastructure";
    private const string Api = "Kanabol.Api";

    private static readonly Assembly _domain = typeof(Domain.Common.Entity).Assembly;
    private static readonly Assembly _application = typeof(Application.DependencyInjection).Assembly;
    private static readonly Assembly _infrastructure = typeof(Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void Domain_WhenInspected_DoesNotDependOnEntityFramework() =>
        ShouldNotDependOn(_domain, EntityFramework);

    [Fact]
    public void Domain_WhenInspected_DoesNotDependOnOuterLayers() =>
        ShouldNotDependOn(_domain, Application, Infrastructure, Api);

    [Fact]
    public void Application_WhenInspected_DoesNotDependOnOuterLayers() =>
        ShouldNotDependOn(_application, Infrastructure, Api);

    [Fact]
    public void Infrastructure_WhenInspected_DoesNotDependOnApi() =>
        ShouldNotDependOn(_infrastructure, Api);

    private static void ShouldNotDependOn(Assembly assembly, params string[] dependencies)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(dependencies).GetResult();

        result.IsSuccessful.Should().BeTrue(
            "these types break the dependency rule: {0}",
            string.Join(", ", result.FailingTypeNames ?? []));
    }
}
