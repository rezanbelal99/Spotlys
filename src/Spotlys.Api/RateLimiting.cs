namespace Spotlys.Api;

/// <summary>Named rate-limit policies, referenced by both the endpoint that needs the
/// stricter limit and <c>Program.cs</c>'s <c>AddRateLimiter</c> registration.</summary>
internal static class RateLimiting
{
    /// <summary>docs/ARCHITECTURE.md §6: stricter limiting on <c>/plan</c> and
    /// <c>/bill/simulate</c> than the global per-IP policy -- both parse a request body and
    /// run the tariff engine, unlike the cheap read endpoints.</summary>
    public const string StrictPolicy = "strict";
}
