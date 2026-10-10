using Animora.Domain.Results;
namespace Animora.Server.Licensing.Issuance;

public interface ILicenseSigner : IDisposable
{
    string KeyId { get; }
    Result<byte[]> Sign(ReadOnlySpan<byte> payload);
}
public interface ILicenseSignerProvider
{
    Task<ILicenseSigner> GetAsync(CancellationToken ct = default);
}
