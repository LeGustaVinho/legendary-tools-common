# Flexible Modifier System

`LegendaryTools.ModifierSystem` is a typed, reflection-free simulation runtime. It is independent from the legacy
`AttributeSystem` v1/v2 API, so existing projects can migrate deliberately.

## Minimal model

```csharp
public sealed class Empire : WorldEntity
{
    public GameAttribute<Empire, double> LeaderSkill { get; private set; }
    public void Initialize() => LeaderSkill = AddAttribute(Gameplay.LeaderSkill, 3d);
}

public sealed class Planet : WorldEntity
{
    public GameAttribute<Planet, double> Stability { get; private set; }
    public void Initialize() => Stability = AddAttribute(Gameplay.Stability, 50d);
}

public static class Gameplay
{
    public static readonly AttributeDefinition<Empire, double> LeaderSkill =
        new AttributeDefinition<Empire, double>("leader-skill", NumericValuePolicies.Double());

    public static readonly AttributeDefinition<Planet, double> Stability =
        new AttributeDefinition<Planet, double>("stability",
            NumericValuePolicies.Double(value => value < 0 || value > 100 ? "Out of range" : null));

    public static readonly RelationDefinition<Empire, Planet> Owns =
        new RelationDefinition<Empire, Planet>("owns", maximumToCount: 1);
}
```

Relations remain domain-facing properties on concrete entities; those properties can delegate to
`Related(Gameplay.Owns)`. The generic graph is also available for prepared traversal plans.

## Modifier definition

```csharp
var ownedPlanets = new PreparedTargetQuery<Empire, Planet>(
    empire => Query.Related(empire, Gameplay.Owns));

var agenda = new ModifierDefinition<Empire, double>(
        "skilled-administrator",
        durationTicks: 3600,
        stacking: new StackingPolicy(StackingMode.RefreshDuration))
    .Affects(
        ownedPlanets,
        Gameplay.Stability,
        ModifierOperation.Add,
        context => context.Source.LeaderSkill.FinalValue * context.Parameters,
        magnitudeEvaluation: MagnitudeEvaluation.Live,
        targetTracking: TargetTracking.Live);

ModifierInstance instance = world.ApplyModifier(agenda, empire, 2d);
```

The same definition can declaratively contribute to typed capabilities with `AffectsCapability`. Capability bindings
support live or snapshot decisions, live or snapshot target membership, conditions, priorities, reverse target
inspection, stacking, removal, expiration, and save/load continuity just like numeric bindings.

Registered `CapacityCollection` instances are also declarative targets through `AffectsCapacity`. Their base and final
capacity, ordered contributions, affected owners, overflow result, removal, and captured magnitudes remain inspectable
and survive save/load without baking modifier values into the stored base capacity.

Magnitude evaluation and target tracking are separate. A snapshot magnitude can follow live membership, and a live
magnitude can be attached to a snapshot target set. Attribute policies define the legal operations and validate every
intermediate result. The built-in stable pipeline is add, multiply, replace, limits, then custom operations.

Declare dependencies to opt into targeted invalidation:

```csharp
definition
    .DependsOn(Gameplay.LeaderSkill, ModifierDependencyScope.Source)
    .DependsOnRelation(Gameplay.Owns, RelationDependencyScope.Source);
```

Source, target, and global attribute dependencies are indexed independently. Definitions without declarations retain
conservative full reevaluation for compatibility. Relation dependencies can be source-scoped (the usual graph-navigation
case) or global. A source-scoped edge change only reevaluates instances attached to that source. Use
`BeginMutationBatch()` to coalesce large command/effect batches into one world-version change and one invalidation pass.
Attribute and modifier definitions are frozen after registration so their dependency indexes cannot silently become
stale.

Opaque query delegates are conservative by default. Declare every attribute read by a filter, projection, grouping, or
ordering delegate to use the incremental attribute-version index:

```csharp
PreparedQuery<Planet> unstable = Query.All<Planet>().Where(
    planet => planet.Stability.FinalValue < 40,
    Query.DependsOn(Gameplay.Stability));
```

Global and entity-scoped attribute dependencies are available, as are global and source-scoped relation dependencies.
Changes to a dependency also advance versions for its derived attributes. Modifier contribution changes therefore dirty
already-cached derived values and dependent prepared queries.

Materialize a prepared query when a consumer needs a retained membership snapshot and deterministic deltas:

```csharp
MaterializedQuery<Planet, EntityId> colonies = Query.Related(empire, Gameplay.Owns).Materialize();
colonies.Changed += delta =>
{
    foreach (Planet added in delta.Added) AddPlanetToUi(added);
    foreach (Planet removed in delta.Removed) RemovePlanetFromUi(removed);
};
colonies.Refresh(world);
```

`Refresh` reuses the prepared cache and performs no diff when its source snapshot is unchanged. Deltas distinguish initial
population, additions, removals, value updates, and changes to the relative order of retained keys. Generic projections
provide an explicit stable key selector; entity queries use `EntityId` through the parameterless `Materialize()` helper.
Duplicate or null keys reject the refresh atomically without corrupting the previous materialized snapshot.

Register a materialized query with the world when refreshes should follow simulation mutations automatically:

```csharp
using ScheduledQueryHandle subscription = world.Schedule(colonies, QueryRefreshMode.Immediate);

// Deferred is the default and coalesces any number of mutations until a simulation boundary.
using ScheduledQueryHandle economy = world.Schedule(monthlyProduction);
world.FlushScheduledQueries();
```

Immediate registrations refresh after a stable mutation or once after the outermost `BeginMutationBatch()` completes.
Deferred registrations remain pending until `FlushScheduledQueries`, allowing tick, frame, or monthly systems to choose
their synchronization boundary. The scheduler consults each prepared dependency snapshot first, so unrelated mutations
neither invoke immediate refreshes nor mark deferred registrations pending. Prepared plans expose structured world,
structure, attribute/entity, and relation/source keys. The world publishes the exact changed keys at each stable mutation,
and indexed registration/pending sets avoid scheduler-wide scans. `ScheduledQueryCandidateCheckCount`,
`ScheduledQueryRefreshExecutionCount`, and `ScheduledQueryDependencyIndexKeyCount` expose routing diagnostics.
Registrations refresh in stable creation order and are removed through their disposable handle. Source-scoped plans require
an attached entity so their index key always contains a valid stable `EntityId`. Observer failures do not undo committed simulation state:
`ScheduledQueryHandle.LastError` retains the failure, `ScheduledQueryFailed` reports it, and `Retry()` can retry without
another world mutation. Failed registrations also retry after the next mutation. Failure-event handlers are isolated.

## Runtime guarantees

- Public collections and values use non-castable read-only views; graph, modifier, capability, and effect mutations use
  controlled APIs.
- Derived attributes declare dependencies. Registration rejects cycles, and changes dirty only dependent values.
- Prepared queries use structural and per-relation/source dependency versions instead of invalidating on every world mutation.
  Target-query factories retain one weakly referenced execution plan per source, while opaque predicates and projections
  conservatively follow the world version. Execution counts are exposed for performance diagnostics.
- Prepared queries provide filtering, ordering, projection, quantifiers, aggregation, and deterministic random selection.
- Live target sets reconcile on structural changes, so new descendants inherit scope modifiers immediately.
- Timed modifiers use an expiration index. Stacking rules support stacking, replacement, strongest, refresh, maximum
  stacks, and source grouping.
- Attribute stages, sources, condition state, affected targets, capability decisions, and bounded history are inspectable.
- Typed tags and components complement typed relations. Effect staging helpers atomically add/remove relations and tags,
  set/remove components, create entities, and require explicit restoration for destructive entity operations.
- Non-numeric permissions use tri-state capability contributions. Structural mutations use explicit staged effects with
  validation, execution identity, deterministic random input, rollback hooks, results, and domain events.
- Effect contexts provide typed staging helpers for base attributes, relations, and modifier creation. Atomic effects run
  inside a mutation batch, reject non-reversible staged steps, and roll back completed structural steps when a later step
  fails. Random effects declare a named stream and exact draw count; rejected or failed executions rewind that stream.
  Observer failures are reported separately and cannot turn a committed effect into a failed result. Partial effects must
  be idempotent; resumable non-idempotent workflows belong in an explicit domain extension.
- `CapacityCollection` implements preserve/block, over-capacity penalty, deterministic disable/removal, clamp, explicit
  decision, and allow-exceeded policies. Selection supports oldest/newest, ranking, upkeep, and player selection.
- Capability resolution supports deny override, allow-unless-denied, highest priority, and required-source policies while
  retaining every contribution and the winning explanation.
- Typed counters and world/entity/effect/event-chain variables have explicit owners and lifetimes. Destroying an entity
  removes its relations, modifier targets/sources, counters, variables, capacities, capacity memberships, and capability
  contributions; retained runtime handles are deactivated.
- Specialized derived values can use `IDomainAggregator<TEntity, TValue>` to declare dependencies and provide a human
  explanation alongside their cached result.
- Stable entity ordering, operation ordering, IDs, modifier instance IDs, query results, and random streams support replay.
- Randomized effects may implement `IRandomizedGameEffect` to select a named deterministic stream. Every created stream
  is persisted independently, so unrelated systems do not perturb one another's random sequence.

## Persistence

`CaptureSaveState` records runtime continuity data, modifier parameters, captured magnitudes, snapshot membership,
remaining absolute expiration ticks, capability contributions, counters, scoped variables, registered capacities,
execution identities, and deterministic sequences. An
`ISimulationPersistenceAdapter` serializes game-specific entities, relations, base attributes, variables, counters,
capabilities, and parameter payloads. Register modifier definitions before `RestoreSaveState`; derived caches and query
indexes are rebuilt rather than serialized.

History policy distinguishes exact, sampled, aggregate-only, and disabled recording, with record count, retention,
sampling, selected change kinds, persistence, diagnostic-only, enforced memory budgets, aggregate summaries, and
overflow behavior. Gameplay-required accumulated
facts should use typed counters instead of unbounded event history.

Persistent trigger definitions declare exact attribute/relation query dependencies or time dependence. Trigger state,
activation and explanation survive save/load, while transitions and observer failures are isolated and deterministic.
Non-persistent histories are cleared on restore; persistent records and their aggregate summaries resume exactly.

## Scale and batch processing

Entities use monotonic compact IDs, an O(1) contiguous slot table, and per-concrete-type ID buffers rather than a tree
node per entity. Attribute storage is lazy and slot-indexed; contributions are value types held in contiguous lists.
Cached final values remain direct reads until a declared dependency dirties them.

Prepared queries expose allocation-light `ProcessBatches`, `Any`, `All`, `None`, `Count`, `Sum`, `Average`, `Max`,
`Min`, `MaxBy`, `MinBy`, deterministic `Random`, ordering, projection, grouping, joins, and materialized deltas.
Scheduled/materialized queries use exact dependency indexes, so an unrelated relation or attribute mutation is not a
refresh candidate.

`FlexibleModifierSystemTests.RunMillionEntityBenchmark()` is the acceptance smoke benchmark. It creates and queries one
million minimal entities inside one mutation batch. The regular suite also exercises a 100,000-entity batch and verifies
that it publishes exactly one world version. Managed batches are the default backend and can be called from Unity jobs,
specialized simulation threads, or a domain-specific Burst/native adapter at synchronization boundaries.
