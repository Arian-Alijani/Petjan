namespace Animora.Application.Security;

/// <summary>
/// Declares the one catalog permission code (<see cref="Domain.Identity.PermissionCodes"/>) a request needs.
/// Read ONCE at registration (<c>ApplicationRegistrationBuilder</c>); a non-catalog code fails startup.
/// An unannotated request is authenticated-only — never public.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RequiresPermissionAttribute(string permissionCode) : Attribute
{
    public string PermissionCode { get; } = permissionCode;
}
