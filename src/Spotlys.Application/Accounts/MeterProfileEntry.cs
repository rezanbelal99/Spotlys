namespace Spotlys.Application.Accounts;

/// <summary>docs/ARCHITECTURE.md §4's meter_profile, on the read side.</summary>
public sealed record MeterProfileEntry(
    Guid Id,
    Guid UserId,
    string Zone,
    string GridCompanyId,
    string SupportScheme,
    bool IsCabin,
    decimal SupplierMarkupExVatOrePerKwh,
    decimal SupplierMonthlyFeeExVatNok,
    int ConsumptionRetentionYears,
    DateTimeOffset CreatedAtUtc
);
