using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flirty.Mcp;

/// <summary>
/// The host's declared database targets, as a singleton: name lookup for the route, the projection
/// <c>flirty_db_list_targets</c> reports, and one prebuilt <see cref="DbContextOptions{TContext}"/> per
/// target.
/// </summary>
/// <remarks>
/// <para>
/// The options are built <b>once, eagerly</b>, in the constructor. Eagerly, because that turns an
/// unusable provider/connection-string combination into a startup failure instead of a puzzling first
/// tool call; once, because a <see cref="DbContextOptions{TContext}"/> is immutable and reusable, and
/// this runs per HTTP request rather than per Blazor gesture – the designer's
/// <c>ConnectionProfileContextBuilder</c> rebuilds each time only because its profile is mutable.
/// </para>
/// <para>
/// A plain <see cref="Dictionary{TKey,TValue}"/> is enough for the cache despite the singleton lifetime:
/// it is fully populated before the instance is published to the container and never written again, and
/// concurrent reads of a frozen dictionary are safe.
/// </para>
/// </remarks>
internal sealed class FlirtyMcpTargetRegistry
{
    private readonly Dictionary<string, FlirtyMcpTarget> _targets;
    private readonly Dictionary<string, DbContextOptions<FlirtyDbContext>> _options;

    /// <summary>Builds the registry from the declared targets.</summary>
    /// <param name="targets">The targets declared with <c>AddTarget</c>, keyed case-insensitively.</param>
    /// <param name="defaultTargetName">
    /// The name of the target served on a route without a <c>{target}</c> segment, already cross-checked
    /// against <paramref name="targets"/> by the caller.
    /// </param>
    internal FlirtyMcpTargetRegistry(
        IReadOnlyDictionary<string, FlirtyMcpTarget> targets, string? defaultTargetName)
    {
        _targets = new Dictionary<string, FlirtyMcpTarget>(targets, StringComparer.OrdinalIgnoreCase);
        _options = new Dictionary<string, DbContextOptions<FlirtyDbContext>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var target in _targets.Values)
        {
            // UseFlirtyProvider is the core's public API and the single place the three MigrationsAssembly
            // names are anchored - the same call the designer makes for a connection profile.
            var builder = new DbContextOptionsBuilder<FlirtyDbContext>();
            builder.UseFlirtyProvider(target.Provider, target.ConnectionString);
            _options[target.Name] = builder.Options;
        }

        DefaultTarget = defaultTargetName is null ? null : _targets[defaultTargetName];
    }

    /// <summary>Whether the host declared any target at all.</summary>
    internal bool HasTargets => _targets.Count > 0;

    /// <summary>
    /// The target served without a <c>{target}</c> route segment, or <see langword="null"/> when that is
    /// the host's own <c>AddFlirty(...)</c> database.
    /// </summary>
    internal FlirtyMcpTarget? DefaultTarget { get; }

    /// <summary>The declared names in the host's spelling, ordered, for an error message.</summary>
    internal IReadOnlyList<string> Names =>
        [.. _targets.Values.Select(target => target.Name).Order(StringComparer.Ordinal)];

    /// <summary>Looks a target up by the name from the route, case-insensitively.</summary>
    internal bool TryGet(string name, out FlirtyMcpTarget target) => _targets.TryGetValue(name, out target!);

    /// <summary>
    /// The wire projection of every declared target: name, provider, description and whether it is the
    /// default. Deliberately no connection string – see <see cref="FlirtyMcpTarget"/>.
    /// </summary>
    internal IReadOnlyList<FlirtyMcpTargetInfo> Describe() =>
        [.. _targets.Values
            .OrderBy(target => target.Name, StringComparer.Ordinal)
            .Select(target => new FlirtyMcpTargetInfo(
                target.Name,
                target.Provider,
                target.Description,
                ReferenceEquals(target, DefaultTarget)))];

    /// <summary>The prebuilt EF Core options of a declared target.</summary>
    internal DbContextOptions<FlirtyDbContext> GetOptions(FlirtyMcpTarget target) => _options[target.Name];
}
