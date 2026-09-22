namespace Spotlys.Application.Accounts;

/// <summary>Persistence port for docs/ARCHITECTURE.md §4's meter_profile.</summary>
public interface IMeterProfileRepository
{
    /// <summary>Creates a new meter profile, owned by <see cref="MeterProfileEntry.UserId"/>.</summary>
    public Task CreateAsync(MeterProfileEntry meter, CancellationToken ct);

    /// <summary>Null if no meter with this id exists. Callers check
    /// <see cref="MeterProfileEntry.UserId"/> against the current user themselves --
    /// ownership is data-dependent, not an ASP.NET authorization policy
    /// (docs/ARCHITECTURE.md §6 / this phase's plan).</summary>
    public Task<MeterProfileEntry?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Every meter owned by one user, oldest first.</summary>
    public Task<IReadOnlyList<MeterProfileEntry>> ListForUserAsync(Guid userId, CancellationToken ct);

    /// <summary>Every meter in the system, across every user -- the retention job's own
    /// sweep needs this; nothing user-facing should ever call it.</summary>
    public Task<IReadOnlyList<MeterProfileEntry>> ListAllAsync(CancellationToken ct);
}
