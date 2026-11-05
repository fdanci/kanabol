using Kanabol.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Options;

namespace Kanabol.Api.Tests.Authorization;

public sealed class PermissionPolicyProviderTests
{
    private readonly AuthorizationOptions _options = new();

    private PermissionPolicyProvider Provider => new(Options.Create(_options));

    [Fact]
    public async Task GetPolicyAsync_ForAPermission_RequiresASignedInUserWithIt()
    {
        var policy = await Provider.GetPolicyAsync("Permission:users.manage");

        policy.Should().NotBeNull();
        policy!.Requirements.Should().ContainSingle(requirement => requirement is DenyAnonymousAuthorizationRequirement);
        policy.Requirements.OfType<PermissionRequirement>().Should().ContainSingle()
            .Which.Permission.Should().Be("users.manage");
    }

    [Fact]
    public async Task GetPolicyAsync_ForAPermissionNobodyListed_StillBuildsItsPolicy()
    {
        var policy = await Provider.GetPolicyAsync("Permission:no.such.permission");

        policy!.Requirements.OfType<PermissionRequirement>().Should().ContainSingle()
            .Which.Permission.Should().Be("no.such.permission");
    }

    [Fact]
    public async Task GetPolicyAsync_ForAnyOtherName_LeavesItToTheRegisteredPolicies()
    {
        var registered = new AuthorizationPolicyBuilder().RequireClaim("some-claim").Build();
        _options.AddPolicy("registered", registered);

        (await Provider.GetPolicyAsync("registered")).Should().BeSameAs(registered);
        (await Provider.GetPolicyAsync("not-registered")).Should().BeNull();
    }

    [Fact]
    public async Task GetDefaultPolicyAsync_WhenAsked_IsTheOneForASignedInUser() =>
        (await Provider.GetDefaultPolicyAsync()).Should().BeSameAs(_options.DefaultPolicy);

    [Fact]
    public async Task GetFallbackPolicyAsync_WhenAsked_IsTheConfiguredOne() =>
        (await Provider.GetFallbackPolicyAsync()).Should().BeNull();

    [Fact]
    public void NameFor_WhenGivenAPermission_IsThePolicyNameInTheSpec() =>
        PermissionPolicy.NameFor("projects.view_all").Should().Be("Permission:projects.view_all");
}
