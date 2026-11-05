namespace Kanabol.Api.Authorization;

/// <summary>The name of a permission's policy, which PermissionPolicyProvider builds when it's asked for.</summary>
public static class PermissionPolicy
{
    public const string Prefix = "Permission:";

    public static string NameFor(string permission) => Prefix + permission;
}
