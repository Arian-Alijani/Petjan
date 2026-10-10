using Animora.Application.Abstractions;
using Animora.Application.Security;
using Animora.Application.UseCases.Search;
using Animora.Contracts.Search;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Search;

/// <summary>Cancelable trailing-edge palette search; target types resolve from the desktop assembly.</summary>
public sealed class SearchService(IServiceScopeFactory scopes) : ISearchService
{
    private readonly object _gate = new();
    private CancellationTokenSource? _active;

    public IReadOnlyList<SearchResult> DefaultSuggestions =>
    [
        new("بیماران", "پرونده‌ها و سرپرستان", "IconUsers", "teal", Target("Patients.PatientsPageViewModel")),
        new("فروش", "فاکتورها", "IconCart", "green", Target("Sales.SalePageViewModel")),
        new("نوبت‌ها", "تقویم کلینیک", "IconCalendar", "blue", Target("Appointments.AppointmentsCalendarViewModel")),
        new("انبار", "کالاها", "IconBox", "amber", Target("Inventory.InventoryPageViewModel")),
    ];

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return DefaultSuggestions;
        CancellationTokenSource current;
        lock (_gate)
        {
            _active?.Cancel();
            current = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _active = current;
        }
        try
        {
            await Task.Delay(200, current.Token);
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().BuildQueryContext();
            if (context.IsFailure) return [];
            var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
                .QueryAsync(new GlobalSearchQuery(query), context.Value!, current.Token);
            if (result.IsFailure) return [];
            return result.Value!.Select(row => new SearchResult(row.Title, row.Subtitle,
                row.Source switch { "invoice" => "IconCart", "product" => "IconBox",
                    "appointment" => "IconCalendar", _ => "IconUsers" },
                row.Source switch { "invoice" => "green", "product" => "amber",
                    "appointment" => "blue", "pet" => "violet", _ => "teal" },
                Target(row.Source switch { "invoice" => "Sales.SalePageViewModel",
                    "product" => "Inventory.InventoryPageViewModel",
                    "appointment" => "Appointments.AppointmentsCalendarViewModel",
                    _ => "Patients.PatientsPageViewModel" }), row.Id)).ToArray();
        }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_active, current)) _active = null; }
            current.Dispose();
        }
    }

    private static Type Target(string name) => Type.GetType($"Animora.Desktop.Pages.{name}, Animora.Desktop")
        ?? throw new InvalidOperationException($"Navigation target unavailable: {name}");
}
