using Animora.Application.Ports;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class LoginAttemptLogWriter(AnimoraLocalDbContext db) : ILoginAttemptLog
{
    public void Append(string usernameAttempted, string outcome, string deviceId, DateTimeOffset nowUtc) =>
        db.Set<LoginAttemptLog>().Add(LoginAttemptLog.Create(usernameAttempted, outcome, deviceId, nowUtc));
}
