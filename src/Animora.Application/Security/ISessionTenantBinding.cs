using Animora.Domain.Results;

namespace Animora.Application.Security;

/// <summary>Physical store's pinned server tenant; independent of the local T-LOCAL-DEFAULT row stamp.</summary>
public interface ISessionTenantBinding
{
    Result Bind(string serverTenantId);
    bool Matches(string serverTenantId);
    bool CanSync { get; }
    void Suspend();
}
