namespace Animora.Application.Security;

/// <summary>
/// An instance-level rule evaluated after RBAC and named policies, only when the permission being
/// checked equals <see cref="PermissionCode"/> and the resource is a <typeparamref name="TResource"/>.
/// Rules are registered as typed services and invoked without reflection.
/// </summary>
public interface IResourceAuthorizationRule<in TResource>
    where TResource : class
{
    string PermissionCode { get; }

    bool IsSatisfied(AuthorizationSubject subject, TResource resource);
}
