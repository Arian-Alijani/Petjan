namespace Animora.Persistence.PostgreSql.Files;

public enum FileState : short { Staged, Uploaded, HashVerified, Committed, Available, Abandoned, Mismatch }
