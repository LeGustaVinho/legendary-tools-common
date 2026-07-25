# Flexible Modifier System

`LegendaryTools.ModifierSystem` is a typed, reflection-free simulation runtime. It is independent from the legacy
`AttributeSystem` v1/v2 API, so existing projects can migrate deliberately.

Simulation-facing numeric examples use `DeterministicFixedPoint.DetS64`. Convert authoring/UI values at the boundary;
the runtime's built-in numeric policies and save codecs do not use `float` or `double`.

## Minimal model

```csharp
using DeterministicFixedPoint;

public sealed class Empire : WorldEntity
{
    public GameAttribute<Empire, DetS64> LeaderSkill { get; private set; }
    public RelatedEntityCollection<Empire, Planet> Planets => Relation(Gameplay.Owns);
    public RelatedEntityReference<Empire, Planet> Capital => RelationReference(Gameplay.Capital);
    public void Initialize() => LeaderSkill = AddAttribute(Gameplay.LeaderSkill, DetS64.FromLong(3));
}

public sealed class Planet : WorldEntity
{
    public GameAttribute<Planet, DetS64> Stability { get; private set; }
    public IncomingRelatedEntityCollection<Empire, Planet> Owners => IncomingRelation(Gameplay.Owns);
    public void Initialize() => Stability = AddAttribute(Gameplay.Stability, DetS64.FromLong(50));
}

public static class Gameplay
{
    public static readonly AttributeDefinition<Empire, DetS64> LeaderSkill =
        new AttributeDefinition<Empire, DetS64>("leader-skill", NumericValuePolicies.FixedS64());

    public static readonly AttributeDefinition<Planet, DetS64> Stability =
        new AttributeDefinition<Planet, DetS64>("stability",
            NumericValuePolicies.FixedS64(value =>
                value < DetS64.Zero || value > DetS64.FromLong(100) ? "Out of range" : null));

    public static readonly RelationDefinition<Empire, Planet> Owns =
        new RelationDefinition<Empire, Planet>("owns", maximumToCount: 1);

    public static readonly RelationDefinition<Empire, Planet> Capital =
        new RelationDefinition<Empire, Planet>("capital", maximumFromCount: 1, maximumToCount: 1);
}
```

`Relation`, `IncomingRelation`, and `RelationReference` cache typed domain views, so ordinary gameplay code uses
`empire.Planets`, `planet.Owners`, or `empire.Capital.Value` without manipulating the generic graph. Reference mutations
use `Set` and `Clear`, enforce one-to-one/outbound cardinality, and update the same graph indexes and query dependencies
as explicit world commands.

Any number of typed edges can be traversed without adding rule-specific helpers:

```csharp
PreparedQuery<Job> jobs = empire.Planets.Query
    .Follow(Gameplay.Contains)
    .Follow(Gameplay.WorksAs);
```

`FollowIncoming` traverses an edge in reverse. Paths deduplicate destinations by entity identity and keep deterministic
`EntityId` order.

## Modifier definition

```csharp
var ownedPlanets = new PreparedTargetQuery<Empire, Planet>(
    empire => Query.Related(empire, Gameplay.Owns));

var agenda = new ModifierDefinition<Empire, DetS64>(
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

ModifierInstance instance = world.ApplyModifier(agenda, empire, DetS64.FromLong(2));
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

Use `AffectsScope` for large inherited scopes. It registers one live contribution object for the modifier binding and
keeps only indexed target IDs; target attributes resolve that shared contribution on demand instead of storing one
contribution object per descendant. Newly related targets inherit it during the same relation reconciliation.

`AffectsCollectionMembership` declaratively includes or excludes typed items from a `DeclarativeCollection`. Base
membership remains domain-owned, while modifier decisions are inspectable and resolve deterministically by priority and
sequence. This models unlockable options and inclusion rules separately from numeric `CapacityCollection` limits.

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

`Refresh` reuses the prepared cache and performs no diff when its source snapshot is unchanged. Direct `Related` and
`RelatedFrom` plans subscribe to typed edge mutations after their first materialization. Their scheduled refresh consumes
only the affected additions/removals—including entity destruction and create-and-relate batches—without executing or
diffing the complete prepared query. Declared `Where` dependencies preserve this delta path for relationship changes and
fall back to a full prepared execution when an attribute dependency changes. Deltas distinguish initial
population, additions, removals, value updates, and changes to the relative order of retained keys. Generic projections
provide an explicit stable key selector; entity queries use `EntityId` through the parameterless `Materialize()` helper.
Duplicate or null keys reject the refresh atomically without corrupting the previous materialized snapshot.
Indexed producers that already know their changed keys can call `ApplyDelta`; this updates only supplied removals and
upserts and does not execute or diff the full prepared query.

Ordering is stable for equal keys and supports `ThenBy`/`ThenByDescending`. Query composition also includes `Skip`,
`Distinct`, `First`, `FirstOrDefault`, `Single`, and `SingleOrDefault` in addition to filtering, projection,
`SelectMany`, joins, grouping, ordering, aggregates, quantifiers, batches, and deterministic random selection.

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
- Conditions expose `Waiting`, `Satisfied`, and `Unsatisfied`. `ProduceModifierFromTrigger` declaratively owns a modifier
  for a trigger, removing it on deactivation and refreshing its typed source/parameters when trigger state changes.
- Typed tags and components complement typed relations. Effect staging helpers atomically add/remove relations, tags,
  collection entries, capacity entries, and capability contributions; set/remove components and capacities; create
  entities, collections, and capacities; and require explicit restoration for destructive entity operations.
- Non-numeric permissions use tri-state capability contributions. Structural mutations use explicit staged effects with
  validation, execution identity, deterministic random input, rollback hooks, results, and domain events.
- Effect contexts provide typed staging helpers for base attributes, relations, and modifier creation. Atomic effects run
  inside a mutation batch, reject non-reversible staged steps, and roll back the currently failing step as well as every
  completed step. World mutations attempted directly from validation or staging are rejected. Effects declaring
  compensation implement `ICompensatingGameEffect<T>` and are compensated at most once per persisted execution ID.
  Random effects declare a named stream and exact draw count; rejected or failed executions rewind that stream.
  Attribute, relation-query, declarative-collection, capacity, and overflow observers are retained until commit, so an
  atomic rollback is externally invisible and successful observers always see the complete committed state. Attribute
  history, capacity membership order, query versions, and deterministic ID/sequence counters are restored on rollback.
  Observer failures are isolated through `EffectObserverDispatchFailed` and cannot turn a committed effect into a failed
  result. Intentionally partial effects publish their completed mutations and must be idempotent; resumable
  non-idempotent workflows belong in an explicit domain extension.
- `CapacityCollection` implements preserve/block, over-capacity penalty, deterministic disable/removal, clamp, explicit
  decision, and allow-exceeded policies. Selection supports oldest/newest, ranking, upkeep, and player selection.
- Capability resolution supports deny override, allow-unless-denied, highest priority, and required-source policies while
  retaining every contribution and the winning explanation. Required sources can use typed
  `StableId<CapabilitySourceIdKind>` keys.
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
remaining absolute expiration ticks, capability contributions, counters, scoped variables, registered capacities and
declarative collections, completed/compensated execution identities, custom history streams, and deterministic
sequences. An
`ISimulationPersistenceAdapter` serializes game-specific entities, relations, base attributes, variables, counters,
capabilities, and parameter payloads. Register modifier definitions before `RestoreSaveState`; derived caches and query
indexes are rebuilt rather than serialized.

`SimulationSaveBinaryCodec` supplies the concrete, versioned `LTMS` binary envelope for a complete
`SimulationSaveState`. Its output order is canonical, and `SimulationValueCodecRegistry` requires every gameplay
payload type to opt into a stable ID plus typed `BinaryWriter`/`BinaryReader` delegates. Built-in primitive, string,
GUID, and byte-array codecs are registered automatically; no reflective object serializer is used. A load adapter may
therefore reconstruct entities, relations, base attributes, counters, capacities, collections, trigger/history
definitions, and modifier definitions in a fresh `SimulationWorld`, then let `RestoreSaveState` restore deterministic
runtime continuity. Capture silently materializes lazy attribute baselines so crossing a save boundary cannot change
subsequent history.

History policy distinguishes exact, sampled, aggregate-only, and disabled recording, with record count, retention,
sampling, selected change kinds, persistence, diagnostic-only, enforced memory budgets, aggregate summaries, and
overflow behavior. Gameplay-required accumulated
facts should use typed counters instead of unbounded event history.

`HistoryStreamDefinition<T>` extends the same bounded policy to event categories and entity-owned timelines. Streams can
record totals with an explicit typed accumulator, state transitions, and time spent in each state. `TrackDomainEvents`
connects declared domain event types to a persistent stream.

Persistent trigger definitions declare exact attribute/relation query dependencies or time dependence. Trigger state,
activation and explanation survive save/load, while transitions and observer failures are isolated and deterministic.
Non-persistent histories are cleared on restore; persistent records and their aggregate summaries resume exactly.

## Scale and batch processing

Entities use monotonic compact IDs, an O(1) contiguous slot table, and per-concrete-type ID buffers rather than a tree
node per entity. Attribute storage is lazy and slot-indexed; contributions are value types held in contiguous lists.
Cached final values remain direct reads until a declared dependency dirties them. Local/shared contribution views are
merged only when their version changes. Declarative collection membership is resolved in linear time after a relevant
world change, cached as a stable ordered view plus an O(1) membership set, and reused without allocating on stable
reads; `ResolutionCount` exposes recomputation for profiling and acceptance tests.

Prepared queries expose allocation-light `ProcessBatches`, `Any`, `All`, `None`, `Count`, `Sum`, `Average`, `Max`,
`Min`, `MaxBy`, `MinBy`, deterministic `Random`, ordering, projection, grouping, joins, and materialized deltas.
Scheduled/materialized queries use exact dependency indexes, so an unrelated relation or attribute mutation is not a
refresh candidate.

`FlexibleModifierSystemTests.RunMillionEntityBenchmark()` is the acceptance smoke benchmark. It creates and queries one
million minimal entities inside one mutation batch. `RunMillionContributionBenchmark()` is the opt-in contribution
acceptance benchmark. The regular suite also exercises a 100,000-entity batch and verifies that it publishes exactly one
world version.

Managed batches remain the object-oriented default backend. The companion
`LegendaryTools.FlexibleModifierSystem.UnityJobs` assembly supplies a concrete Burst-compiled
`IJobParallelFor` fixed-point evaluator over raw `DetS32` integer buffers, ranges, blittable contributions, and results. Custom
managed policies and object-graph navigation stay outside jobs; synchronization happens only at contiguous buffer
boundaries.
