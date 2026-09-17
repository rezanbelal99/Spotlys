using Microsoft.Extensions.Logging;
using Spotlys.Application.Ingestion;

namespace Spotlys.Ingestion;

// Source-generated logging (CA1848/CA1873 want this over the ILogger extension methods
// directly -- no allocation/formatting cost when the level is disabled). Status is passed
// as the enum itself, not pre-stringified, so there's nothing to evaluate at the call site
// when the level is disabled either.
internal static partial class IngestionLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "{JobName}: nothing to do")]
    public static partial void NothingToDo(ILogger logger, string jobName);

    [LoggerMessage(Level = LogLevel.Information, Message = "{JobName} finished: {Status}, {Rows} rows, {DurationMs}ms")]
    public static partial void JobFinished(ILogger logger, string jobName, IngestionRunStatus status, int? rows, long durationMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "{JobName} threw")]
    public static partial void JobThrew(ILogger logger, Exception exception, string jobName);
}
