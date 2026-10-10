using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Server.Authorization;
using Animora.Server.Commands;
using Xunit;

namespace Animora.Server.Tests.Authorization;

public sealed class CommandPermissionCatalogTests
{
    // Reserved types are deliberately unregistered and rejected before authorization.
    [Fact]
    public void EveryCommandType_HasARequiredPermission()
    {
        var missing = new List<string>();
        foreach (string commandType in CommandTypes.All)
        {
            if (ReservedCommandTypes.All.Contains(commandType))
            {
                Assert.Null(CommandPermissionTable.RequiredFor(commandType));
                continue;
            }
            string? required = CommandPermissionTable.RequiredFor(commandType);
            if (required is null || !PermissionCodes.Exists(required))
            {
                missing.Add(commandType);
            }
        }

        Assert.True(missing.Count == 0, "Command types without a (valid) required permission: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryTableRow_PointsAtACommandTypeAndACatalogPermission()
    {
        // Active invariant that holds from day one (vacuously while the table is empty).
        foreach ((string commandType, string code) in CommandPermissionTable.Rows)
        {
            Assert.Contains(commandType, CommandTypes.All);
            Assert.True(PermissionCodes.Exists(code), $"{commandType} maps to unknown permission '{code}'.");
        }
    }
}
