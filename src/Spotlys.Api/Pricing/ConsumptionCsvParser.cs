using System.Globalization;
using Spotlys.Domain.Pricing;

namespace Spotlys.Api.Pricing;

/// <summary>Either the parsed rows or the Problem Details response to return -- never both.</summary>
internal sealed record ConsumptionCsvParseResult(IReadOnlyList<HourlyConsumption>? Consumption, IResult? Error)
{
    public static ConsumptionCsvParseResult Ok(IReadOnlyList<HourlyConsumption> consumption) => new(consumption, null);

    public static ConsumptionCsvParseResult Problem(string title, string detail) =>
        new(null, Results.Problem(title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest));
}

/// <summary>
/// Strict, streaming CSV reader for the <c>hour_start_utc,kwh</c> consumption import
/// (docs/ARCHITECTURE.md §9: "never <c>File.ReadAllText</c> into a raw string builder,
/// treat uploaded files as hostile"). Reads line by line rather than buffering the whole
/// body, and enforces its own row cap independently of the caller's request-body-size cap.
/// </summary>
internal static class ConsumptionCsvParser
{
    private const string ExpectedHeader = "hour_start_utc,kwh";

    // One non-leap year of hourly rows -- generous headroom over the one-calendar-month
    // bill the engine actually computes, still a hard, explicit bound.
    private const int MaxRows = 8_760;

    public static async Task<ConsumptionCsvParseResult> ParseAsync(Stream body, CancellationToken ct)
    {
        using var reader = new StreamReader(body, leaveOpen: true);

        var headerLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (!string.Equals(headerLine?.Trim(), ExpectedHeader, StringComparison.Ordinal))
        {
            return ConsumptionCsvParseResult.Problem(
                "Invalid CSV header",
                $"The first line must be exactly '{ExpectedHeader}'.");
        }

        var consumption = new List<HourlyConsumption>();
        var lineNumber = 1;
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            lineNumber++;

            if (line.Length == 0)
            {
                continue; // tolerate a trailing blank line
            }

            if (consumption.Count >= MaxRows)
            {
                return ConsumptionCsvParseResult.Problem(
                    "Consumption file too large",
                    $"More than {MaxRows} data rows -- an import covers at most one year of hourly readings.");
            }

            var parts = line.Split(',');
            if (parts.Length != 2)
            {
                return ConsumptionCsvParseResult.Problem(
                    "Malformed consumption row",
                    $"Line {lineNumber}: expected exactly two comma-separated fields.");
            }

            if (!DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var hourStartUtc))
            {
                return ConsumptionCsvParseResult.Problem(
                    "Malformed consumption row",
                    $"Line {lineNumber}: '{parts[0]}' is not a valid ISO-8601 timestamp.");
            }

            if (!decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var kwh) || kwh < 0)
            {
                return ConsumptionCsvParseResult.Problem(
                    "Malformed consumption row",
                    $"Line {lineNumber}: '{parts[1]}' is not a valid non-negative decimal kWh value.");
            }

            consumption.Add(new HourlyConsumption(hourStartUtc, kwh));
        }

        if (consumption.Count == 0)
        {
            return ConsumptionCsvParseResult.Problem("Empty consumption file", "No data rows found after the header.");
        }

        return ConsumptionCsvParseResult.Ok(consumption);
    }
}
