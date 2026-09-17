using System.Runtime.CompilerServices;

// Lets contract tests instantiate internal implementation types (e.g.
// HvakosterstrommenPriceSource) directly instead of only through DI -- the standard .NET
// pattern for testing internals without widening production visibility.
[assembly: InternalsVisibleTo("Spotlys.Integration.Tests")]
