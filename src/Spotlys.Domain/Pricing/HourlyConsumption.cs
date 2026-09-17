namespace Spotlys.Domain.Pricing;

/// <summary>One meter's metered consumption for one hour -- the tariff engine's input,
/// either from a CSV import or a seeded demo profile.</summary>
/// <param name="HourStartUtc">Start of the hour, UTC.</param>
/// <param name="Kwh">Energy consumed during the hour.</param>
public sealed record HourlyConsumption(DateTimeOffset HourStartUtc, decimal Kwh);
