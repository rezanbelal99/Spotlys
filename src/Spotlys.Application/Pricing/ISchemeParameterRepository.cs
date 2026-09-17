using Spotlys.Domain.Pricing;

namespace Spotlys.Application.Pricing;

/// <summary>
/// Resolves versioned scheme_parameter rows into the Domain's already-resolved parameter
/// types, for a given date -- the one place a date-based lookup happens, keeping
/// <see cref="Domain.Pricing.TariffEngine"/> itself pure (docs/DOMAIN.md §7).
/// </summary>
public interface ISchemeParameterRepository
{
    /// <summary>Strømstøtte parameters effective on <paramref name="forDate"/>.</summary>
    public Task<StromstotteParameters> GetStromstotteParametersAsync(DateOnly forDate, CancellationToken ct);

    /// <summary>Norgespris parameters effective on <paramref name="forDate"/>.</summary>
    public Task<NorgesprisParameters> GetNorgesprisParametersAsync(DateOnly forDate, bool isCabin, CancellationToken ct);

    /// <summary>Levy and VAT parameters effective on <paramref name="forDate"/> for <paramref name="zone"/>.</summary>
    public Task<LevyParameters> GetLevyParametersAsync(DateOnly forDate, PriceArea zone, CancellationToken ct);
}
