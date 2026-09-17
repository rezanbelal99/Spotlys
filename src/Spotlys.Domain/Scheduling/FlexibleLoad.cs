namespace Spotlys.Domain.Scheduling;

/// <summary>
/// A load that can be shifted in time -- e.g. an EV charge that must finish by a deadline
/// (docs/FORECASTING.md §8). <see cref="NotBefore"/>/<see cref="Deadline"/> are treated as
/// bounds on which hourly buckets are usable, not as fractional-hour constraints -- every
/// scheduling input in this system (prices, consumption, forecasts) is already hour-bucketed,
/// so the optimizer only ever allocates whole hours whose start falls in
/// <c>[NotBefore, Deadline)</c>.
/// </summary>
/// <param name="EnergyKwh">Total energy the load needs, e.g. a 40 kWh EV charge.</param>
/// <param name="MaxPowerKw">The charger/appliance's power ceiling.</param>
/// <param name="MinPowerKw">The lowest power the load can run at while active; 0 for a
/// genuinely interruptible load. Deliberately not enforced on the final partial hour that
/// exactly completes <see cref="EnergyKwh"/> -- invariant 1 (total energy scheduled equals
/// requested energy) takes priority over the floor at that boundary, matching how a real
/// charger tapers off at the end of a cycle.</param>
/// <param name="NotBefore">Earliest hour the load may start.</param>
/// <param name="Deadline">Hour by which the load must be finished (exclusive upper bound).</param>
/// <param name="Interruptible">False for a load like a dryer that can't run below
/// <see cref="MinPowerKw"/> once started; true ignores <see cref="MinPowerKw"/> entirely.</param>
public sealed record FlexibleLoad(
    decimal EnergyKwh,
    decimal MaxPowerKw,
    decimal MinPowerKw,
    DateTimeOffset NotBefore,
    DateTimeOffset Deadline,
    bool Interruptible);
