using System.Runtime.CompilerServices;

// Lets contract tests instantiate internal implementation types (e.g.
// HvakosterstrommenPriceSource) directly instead of only through DI -- the standard .NET
// pattern for testing internals without widening production visibility.
[assembly: InternalsVisibleTo("Spotlys.Integration.Tests")]

// Same reasoning, for the point-in-time test suite's direct access to the weather/hydrology
// repositories' as-of-safe read methods (docs/ARCHITECTURE.md §2).
[assembly: InternalsVisibleTo("Spotlys.PointInTime.Tests")]
