namespace Animora.Contracts.Search;

/// <summary>
/// One command-palette row: a navigable action, mirroring the reference's
/// <c>CMDK_ACTIONS</c> entry (title / subtitle / icon / tone / target). The
/// <see cref="Target"/> is a <c>PageViewModelBase</c>-derived ViewModel type
/// resolved through <see cref="Animora.Contracts.Navigation"/>'s navigation seam.
/// </summary>
/// <param name="Title">Primary line (e.g. «جستجوی بیمار»).</param>
/// <param name="Subtitle">Secondary muted line (e.g. «پرونده‌ها و سرپرستان»).</param>
/// <param name="IconKey">Resource key of the icon geometry (e.g. "IconUsers").</param>
/// <param name="Tone">
/// Colored chip tone — one of <c>teal</c>/<c>blue</c>/<c>violet</c>/<c>green</c>/<c>amber</c>,
/// matching the reference's <c>.ci-ic.tone-*</c> palette.
/// </param>
/// <param name="Target">The screen ViewModel type this result navigates to.</param>
/// <param name="DrillId">Optional record id for a future drill-in navigation.</param>
public sealed record SearchResult(
    string Title,
    string Subtitle,
    string IconKey,
    string Tone,
    Type Target,
    string? DrillId = null);

/// <summary>
/// Backend-ready seam for the global command palette (Ctrl+K) search. The palette
/// ViewModel depends only on this interface; the in-memory <c>MockSearchService</c>
/// is the sole swap point for a future backend that searches patients, invoices,
/// appointments, etc.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// The four default suggestions shown before the user types anything
    /// (the reference's <c>CMDK_ACTIONS.slice(0, 4)</c>).
    /// </summary>
    IReadOnlyList<SearchResult> DefaultSuggestions { get; }

    /// <summary>
    /// Returns matching results for <paramref name="query"/>: the static command
    /// actions filtered by title/subtitle/keywords, plus live patient/owner
    /// matches once the query is long enough to be meaningful. Runs off the UI
    /// thread; honors <paramref name="ct"/> so a superseded keystroke cancels the
    /// in-flight search.
    /// </summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct = default);
}
