using Animora.Domain.Results;
namespace Animora.Application.Abstractions;
public interface IUnitOfWork
{
    Task<Result> SaveAsync(CancellationToken ct = default);
    Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default);
}
