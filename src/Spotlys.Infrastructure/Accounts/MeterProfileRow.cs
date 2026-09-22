namespace Spotlys.Infrastructure.Accounts;

/// <summary>docs/ARCHITECTURE.md §4's meter_profile, plus <see cref="ConsumptionRetentionYears"/>
/// (an addition: DATA.md §4's "default 3 years, user-set extension" retention needs
/// somewhere to live; per-meter is the natural granularity).</summary>
internal sealed class MeterProfileRow
{
    public required Guid Id { get; set; }
    public required Guid UserId { get; set; }
    public required string Zone { get; set; }
    public required string GridCompanyId { get; set; }
    public required string SupportScheme { get; set; } // 'stromstotte' | 'norgespris'
    public bool IsCabin { get; set; }
    public decimal SupplierMarkupExVatOrePerKwh { get; set; }
    public decimal SupplierMonthlyFeeExVatNok { get; set; }
    public int ConsumptionRetentionYears { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
