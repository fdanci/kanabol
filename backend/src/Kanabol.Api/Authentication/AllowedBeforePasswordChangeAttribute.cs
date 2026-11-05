namespace Kanabol.Api.Authentication;

/// <summary>Marks the few endpoints a user who has to change their password can still use.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowedBeforePasswordChangeAttribute : Attribute
{
}
