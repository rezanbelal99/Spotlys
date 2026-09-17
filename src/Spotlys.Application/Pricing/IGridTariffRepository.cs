using Spotlys.Domain.Pricing;

namespace Spotlys.Application.Pricing;

/// <summary>Name and id of a seeded grid company, for API discovery (e.g. a dropdown).</summary>
public sealed record GridCompanySummary(string Id, string Name);

/// <summary>Resolves a grid company's versioned tariff for a given date.</summary>
public interface IGridTariffRepository
{
    /// <summary>The grid company's tariff effective on <paramref name="forDate"/>, or null
    /// if the company is unknown or has no tariff on/before that date.</summary>
    public Task<GridTariff?> GetAsync(string gridCompanyId, DateOnly forDate, CancellationToken ct);

    /// <summary>Every seeded grid company, for API discovery (e.g. a dropdown).</summary>
    public Task<IReadOnlyList<GridCompanySummary>> ListGridCompaniesAsync(CancellationToken ct);
}
