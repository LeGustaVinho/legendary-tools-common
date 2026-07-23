using System;
using System.Collections.Generic;
using System.Linq;
using LegendaryTools.ModifierSystem;
using NUnit.Framework;

namespace LegendaryTools.Tests.ModifierSystem
{
    public sealed class Empire : WorldEntity
    {
        public GameAttribute<Empire, double> Skill { get; private set; }
        public void Initialize(double skill) => Skill = AddAttribute(TestDefinitions.Skill, skill);
        public GameAttribute<Empire, double> AddCalculated(AttributeDefinition<Empire, double> definition) =>
            AddAttribute(definition, 0d);
    }

    public sealed class Planet : WorldEntity
    {
        public GameAttribute<Planet, double> Stability { get; private set; }
        public GameAttribute<Planet, double> Production { get; private set; }

        public void Initialize(double stability)
        {
            Stability = AddAttribute(TestDefinitions.Stability, stability);
            Production = AddAttribute(TestDefinitions.Production, 0d);
        }

        public GameAttribute<Planet, double> AddCalculated(AttributeDefinition<Planet, double> definition) =>
            AddAttribute(definition, 0d);
        public GameAttribute<Planet, double> AddValue(AttributeDefinition<Planet, double> definition, double value) =>
            AddAttribute(definition, value);
    }

    public sealed class BareEntity : WorldEntity
    {
    }

    internal static class TestDefinitions
    {
        public static readonly HistoryPolicy ExactHistory =
            new HistoryPolicy(HistoryRecordMode.Exact, maximumRecords: 8);

        public static readonly AttributeDefinition<Empire, double> Skill =
            new AttributeDefinition<Empire, double>("test.empire.skill", NumericValuePolicies.Double());

        public static readonly AttributeDefinition<Planet, double> Stability =
            new AttributeDefinition<Planet, double>("test.planet.stability",
                NumericValuePolicies.Double(value => value < 0 || value > 100 ? "Stability must be from 0 to 100." : null),
                ExactHistory);

        public static readonly AttributeDefinition<Planet, double> Production =
            AttributeDefinition<Planet, double>.Derived("test.planet.production", NumericValuePolicies.Double(),
                planet => planet.Stability.FinalValue * 2d, new IAttributeDefinition[] { Stability });

        public static readonly RelationDefinition<Empire, Planet> Owns =
            new RelationDefinition<Empire, Planet>("test.owns", maximumToCount: 1);

        public static readonly RelationDefinition<Empire, Planet> Observes =
            new RelationDefinition<Empire, Planet>("test.observes");

        public static readonly PreparedTargetQuery<Empire, Planet> OwnedPlanets =
            new PreparedTargetQuery<Empire, Planet>(empire => Query.Related(empire, Owns));
    }

    public readonly struct BonusParameters
    {
        public double Flat { get; }
        public BonusParameters(double flat) => Flat = flat;
    }

    [TestFixture]
    public sealed class FlexibleModifierSystemTests
    {
        [Test]
        public void ModifierPipeline_IsStableInspectableAndIncremental()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(3));
            Planet planet = world.Create<Planet>(item => item.Initialize(40));
            world.AddRelation(empire, TestDefinitions.Owns, planet);

            var multiply = new ModifierDefinition<Empire, BonusParameters>("test.multiply")
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability, ModifierOperation.Multiply,
                    context => 1.5d);
            var add = new ModifierDefinition<Empire, BonusParameters>("test.add")
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability, ModifierOperation.Add,
                    context => context.Parameters.Flat);

            world.ApplyModifier(multiply, empire, new BonusParameters(0));
            ModifierInstance addInstance = world.ApplyModifier(add, empire, new BonusParameters(10));

            ExpectEqual(75d, planet.Stability.FinalValue);
            ExpectEqual(150d, planet.Production.FinalValue);
            Assert.IsTrue(new[]
                {
                    AttributeEvaluationStage.Base, AttributeEvaluationStage.Additive,
                    AttributeEvaluationStage.Multiplicative, AttributeEvaluationStage.Final
                }.SequenceEqual(
                    planet.Stability.EvaluationStages.Select(stage => stage.Stage)));
            ExpectEqual(1, addInstance.AffectedAttributes.Count);

            planet.Stability.SetBaseValue(20, "test change");
            ExpectEqual(45d, planet.Stability.FinalValue);
            ExpectEqual(90d, planet.Production.FinalValue);
            Assert.IsTrue(planet.Stability.History.Any(change => change.Reason == "test change"));
        }

        [Test]
        public void ModifierContribution_InvalidatesAlreadyCachedDerivedAttributes()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            world.AddRelation(empire, TestDefinitions.Owns, planet);
            ExpectEqual(20d, planet.Production.FinalValue);

            var definition = new ModifierDefinition<Empire, BonusParameters>("test.derived-invalidation")
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability, ModifierOperation.Add,
                    context => context.Parameters.Flat);
            ModifierInstance instance = world.ApplyModifier(definition, empire, new BonusParameters(5));

            ExpectEqual(30d, planet.Production.FinalValue);
            world.RemoveModifier(instance);
            ExpectEqual(20d, planet.Production.FinalValue);
        }

        [Test]
        public void LiveScope_AutomaticallyTracksRelationshipMembership()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(2));
            Planet first = world.Create<Planet>(item => item.Initialize(20));
            world.AddRelation(empire, TestDefinitions.Owns, first);

            var definition = new ModifierDefinition<Empire, BonusParameters>("test.scope")
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability, ModifierOperation.Add,
                    context => context.Parameters.Flat, targetTracking: TargetTracking.Live);
            ModifierInstance instance = world.ApplyModifier(definition, empire, new BonusParameters(5));

            Planet second = world.Create<Planet>(item => item.Initialize(30));
            world.AddRelation(empire, TestDefinitions.Owns, second);
            ExpectEqual(25d, first.Stability.FinalValue);
            ExpectEqual(35d, second.Stability.FinalValue);
            ExpectEqual(2, instance.AffectedAttributes.Count);

            world.RemoveRelation(empire, TestDefinitions.Owns, first);
            ExpectEqual(20d, first.Stability.FinalValue);
            ExpectEqual(1, instance.AffectedAttributes.Count);
        }

        [Test]
        public void SnapshotAndLiveMagnitude_AreIndependentFromTargetTracking()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(2));
            Planet snapshotPlanet = world.Create<Planet>(item => item.Initialize(10));
            Planet livePlanet = world.Create<Planet>(item => item.Initialize(10));
            world.AddRelation(empire, TestDefinitions.Owns, snapshotPlanet);
            world.AddRelation(empire, TestDefinitions.Owns, livePlanet);

            var snapshot = new ModifierDefinition<Empire, BonusParameters>("test.snapshot")
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability, ModifierOperation.Add,
                    context => context.Source.Skill.FinalValue,
                    magnitudeEvaluation: MagnitudeEvaluation.Snapshot,
                    condition: context => context.Target == snapshotPlanet);
            var live = new ModifierDefinition<Empire, BonusParameters>("test.live")
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability, ModifierOperation.Add,
                    context => context.Source.Skill.FinalValue,
                    magnitudeEvaluation: MagnitudeEvaluation.Live,
                    condition: context => context.Target == livePlanet);
            world.ApplyModifier(snapshot, empire, default);
            world.ApplyModifier(live, empire, default);
            ExpectEqual(12d, snapshotPlanet.Stability.FinalValue);
            ExpectEqual(12d, livePlanet.Stability.FinalValue);
            ExpectEqual(24d, livePlanet.Production.FinalValue);

            empire.Skill.SetBaseValue(7);
            ExpectEqual(12d, snapshotPlanet.Stability.FinalValue);
            ExpectEqual(17d, livePlanet.Stability.FinalValue);
            ExpectEqual(34d, livePlanet.Production.FinalValue);
        }

        [Test]
        public void TimedModifier_ExpiresAtScheduledTick()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(50));
            world.AddRelation(empire, TestDefinitions.Owns, planet);
            var definition = new ModifierDefinition<Empire, BonusParameters>("test.timed", durationTicks: 10)
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability, ModifierOperation.Add,
                    context => context.Parameters.Flat);
            ModifierInstance instance = world.ApplyModifier(definition, empire, new BonusParameters(10));

            world.AdvanceTo(9);
            ExpectEqual(60d, planet.Stability.FinalValue);
            ExpectEqual(1L, instance.RemainingTicks.Value);
            world.AdvanceTo(10);
            ExpectEqual(50d, planet.Stability.FinalValue);
            Assert.IsFalse(world.Modifiers.Contains(instance));
        }

        [Test]
        public void Capability_DenyOverridesAllow_AndExplainsWinner()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            var capability = new CapabilityDefinition<Empire>("test.colonize-toxic",
                CapabilityResolutionPolicy.DenyOverridesAllow);
            CapabilityContributionHandle technology = world.ContributeCapability(empire, capability,
                CapabilityContribution.Allow, empire, sourceDescription: "Technology");
            CapabilityContributionHandle crisis = world.ContributeCapability(empire, capability,
                CapabilityContribution.Deny, priority: 100, sourceDescription: "Crisis");

            CapabilityEvaluation<Empire> evaluation = world.EvaluateCapability(empire, capability);
            Assert.IsFalse(evaluation.IsAllowed);
            ExpectEqual("Crisis", evaluation.WinningContribution.Value.Source);
            ExpectEqual(2, evaluation.Contributions.Count);

            crisis.Dispose();
            Assert.IsTrue(world.EvaluateCapability(empire, capability).IsAllowed);
            technology.Dispose();
        }

        [Test]
        public void NonIdempotentEffect_RequiresAndPersistsExecutionIdentity()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            var effect = new IncreaseSkillEffect(empire);
            Guid executionId = Guid.Parse("dd5bf84c-1cc5-4cf7-85f7-ad14636ad565");

            ExpectEqual(EffectStatus.Rejected, world.ExecuteEffect(effect, 2d).Status);
            ExpectEqual(EffectStatus.Succeeded, world.ExecuteEffect(effect, 2d, executionId).Status);
            ExpectEqual(3d, empire.Skill.FinalValue);
            ExpectEqual(EffectStatus.Duplicate, world.ExecuteEffect(effect, 2d, executionId).Status);
            ExpectEqual(3d, empire.Skill.FinalValue);
        }

        [Test]
        public void EffectExecution_PublishesOneStableScheduledQueryBoundary()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            MaterializedQuery<double, int> skill = new PreparedQuery<double>(simulation =>
                    new[] { empire.Skill.FinalValue })
                .Materialize(value => 0);
            using (world.Schedule(skill, QueryRefreshMode.Immediate))
            {
                EffectResult result = world.ExecuteEffect(new IncreaseSkillEffect(empire), 2d,
                    Guid.Parse("59141150-804a-4b42-a9d2-e6e8fc75aab8"));

                ExpectEqual(EffectStatus.Succeeded, result.Status);
                ExpectEqual(2L, skill.RefreshCount);
                ExpectEqual(3d, skill.Current[0]);
            }
        }

        [Test]
        public void PreparedQueries_ProvideDeterministicQuantifiersAndRandomSelection()
        {
            var world = new SimulationWorld();
            Planet first = world.Create<Planet>(item => item.Initialize(20));
            world.Create<Planet>(item => item.Initialize(80));
            PreparedQuery<Planet> unstable = Query.All<Planet>().Where(item => item.Stability.FinalValue < 40);

            Assert.IsTrue(unstable.Any(world));
            ExpectEqual(1, unstable.Count(world));
            Assert.AreSame(first, unstable.Random(world, new XorShiftRandom(123)));
            ExpectEqual(50d, Query.All<Planet>().Average(world, item => item.Stability.FinalValue));
            first.Stability.SetBaseValue(60);
            ExpectEqual(0, unstable.Count(world));
        }

        [Test]
        public void PreparedRelationQuery_InvalidatesOnlyForRelevantDependencies()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Empire otherEmpire = world.Create<Empire>(item => item.Initialize(1));
            Planet first = world.Create<Planet>(item => item.Initialize(20));
            Planet second = world.Create<Planet>(item => item.Initialize(30));
            Planet otherPlanet = world.Create<Planet>(item => item.Initialize(40));
            world.AddRelation(empire, TestDefinitions.Owns, first);
            PreparedQuery<Planet> owned = Query.Related(empire, TestDefinitions.Owns);

            ExpectEqual(1, owned.Execute(world).Count);
            ExpectEqual(1L, owned.ExecutionCount);

            first.Stability.SetBaseValue(25);
            ExpectEqual(1, owned.Execute(world).Count);
            ExpectEqual(1L, owned.ExecutionCount);

            world.AddRelation(empire, TestDefinitions.Observes, second);
            ExpectEqual(1, owned.Execute(world).Count);
            ExpectEqual(1L, owned.ExecutionCount);

            world.AddRelation(otherEmpire, TestDefinitions.Owns, otherPlanet);
            ExpectEqual(1, owned.Execute(world).Count);
            ExpectEqual(1L, owned.ExecutionCount);

            world.AddRelation(empire, TestDefinitions.Owns, second);
            ExpectEqual(2, owned.Execute(world).Count);
            ExpectEqual(2L, owned.ExecutionCount);
        }

        [Test]
        public void DeclaredQueryAttributeDependencies_AvoidUnrelatedInvalidation()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet first = world.Create<Planet>(item => item.Initialize(20));
            world.Create<Planet>(item => item.Initialize(80));
            PreparedQuery<Planet> unstable = Query.All<Planet>().Where(
                item => item.Stability.FinalValue < 50,
                Query.DependsOn(TestDefinitions.Stability));
            PreparedQuery<double> production = Query.All<Planet>().Select(
                item => item.Production.FinalValue,
                Query.DependsOn(TestDefinitions.Production));

            ExpectEqual(1, unstable.Count(world));
            ExpectEqual(40d, production.Execute(world)[0]);
            ExpectEqual(1L, unstable.ExecutionCount);
            ExpectEqual(1L, production.ExecutionCount);

            empire.Skill.SetBaseValue(2);
            ExpectEqual(1, unstable.Count(world));
            ExpectEqual(40d, production.Execute(world)[0]);
            ExpectEqual(1L, unstable.ExecutionCount);
            ExpectEqual(1L, production.ExecutionCount);

            first.Stability.SetBaseValue(60);
            ExpectEqual(0, unstable.Count(world));
            ExpectEqual(120d, production.Execute(world)[0]);
            ExpectEqual(2L, unstable.ExecutionCount);
            ExpectEqual(2L, production.ExecutionCount);

            PreparedQuery<Planet> entityScoped = Query.All<Planet>().Where(
                item => item == first,
                Query.DependsOn(first, TestDefinitions.Stability));
            var otherWorld = new SimulationWorld();
            otherWorld.Create<Planet>(item => item.Initialize(10));
            Assert.Throws<InvalidOperationException>(() => entityScoped.Execute(otherWorld));
        }

        [Test]
        public void PreparedTargetQuery_ReusesOnePlanPerSource()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(20));
            world.AddRelation(empire, TestDefinitions.Owns, planet);
            int plansCreated = 0;
            var targets = new PreparedTargetQuery<Empire, Planet>(source =>
            {
                plansCreated++;
                return Query.Related(source, TestDefinitions.Owns);
            });

            ExpectEqual(1, targets.Execute(world, empire).Count);
            planet.Stability.SetBaseValue(30);
            ExpectEqual(1, targets.Execute(world, empire).Count);
            ExpectEqual(1, plansCreated);
        }

        [Test]
        public void MaterializedQuery_EmitsDeterministicMembershipDeltas()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet first = world.Create<Planet>(item => item.Initialize(10));
            Planet second = world.Create<Planet>(item => item.Initialize(20));
            Planet third = world.Create<Planet>(item => item.Initialize(30));
            world.AddRelation(empire, TestDefinitions.Owns, first);
            MaterializedQuery<Planet, EntityId> materialized = Query.Related(empire, TestDefinitions.Owns)
                .Materialize();
            var notifications = new List<QueryDelta<Planet, EntityId>>();
            materialized.Changed += notifications.Add;

            QueryDelta<Planet, EntityId> initial = materialized.Refresh(world);
            Assert.IsTrue(initial.IsInitial);
            Assert.IsTrue(new[] { first }.SequenceEqual(initial.Added));
            ExpectEqual(1, notifications.Count);
            ExpectEqual(1L, materialized.DiffCount);

            first.Stability.SetBaseValue(15);
            QueryDelta<Planet, EntityId> unrelated = materialized.Refresh(world);
            Assert.IsFalse(unrelated.HasChanges);
            ExpectEqual(1, notifications.Count);
            ExpectEqual(1L, materialized.DiffCount);

            world.AddRelation(empire, TestDefinitions.Owns, second);
            QueryDelta<Planet, EntityId> added = materialized.Refresh(world);
            Assert.IsTrue(new[] { second }.SequenceEqual(added.Added));
            Assert.IsFalse(added.OrderChanged);

            using (world.BeginMutationBatch())
            {
                world.RemoveRelation(empire, TestDefinitions.Owns, first);
                world.AddRelation(empire, TestDefinitions.Owns, third);
            }
            QueryDelta<Planet, EntityId> changed = materialized.Refresh(world);
            Assert.IsTrue(new[] { third }.SequenceEqual(changed.Added));
            Assert.IsTrue(new[] { first }.SequenceEqual(changed.Removed));
            Assert.IsTrue(new[] { second, third }.SequenceEqual(materialized.Current));
            Assert.IsFalse(changed.OrderChanged);
            ExpectEqual(3, notifications.Count);
            ExpectEqual(4L, materialized.RefreshCount);
            ExpectEqual(3L, materialized.DiffCount);
        }

        [Test]
        public void MaterializedQuery_ReportsValueAndOrderingChangesAtomically()
        {
            var world = new SimulationWorld();
            Planet first = world.Create<Planet>(item => item.Initialize(10));
            Planet second = world.Create<Planet>(item => item.Initialize(20));
            QueryDependency stability = Query.DependsOn(TestDefinitions.Stability);
            MaterializedQuery<KeyValuePair<EntityId, double>, EntityId> materialized = Query.All<Planet>()
                .Select(item => new KeyValuePair<EntityId, double>(item.Id, item.Stability.FinalValue), stability)
                .Ordered(item => item.Value, false, stability)
                .Materialize(item => item.Key);
            materialized.Refresh(world);

            first.Stability.SetBaseValue(30);
            QueryDelta<KeyValuePair<EntityId, double>, EntityId> delta = materialized.Refresh(world);

            ExpectEqual(1, delta.Updated.Count);
            ExpectEqual(first.Id, delta.Updated[0].Key);
            ExpectEqual(10d, delta.Updated[0].Previous.Value);
            ExpectEqual(30d, delta.Updated[0].Current.Value);
            Assert.IsTrue(delta.OrderChanged);
            ExpectEqual(second.Id, materialized.Current[0].Key);

            MaterializedQuery<int, int> invalid = new PreparedQuery<int>(simulation => new[] { 1, 3 })
                .Materialize(value => value % 2);
            Assert.Throws<InvalidOperationException>(() => invalid.Refresh(world));
            Assert.IsFalse(invalid.IsInitialized);
        }

        [Test]
        public void ImmediateScheduledQuery_CoalescesMutationBatchAndSkipsUnchangedDiffs()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet first = world.Create<Planet>(item => item.Initialize(10));
            Planet second = world.Create<Planet>(item => item.Initialize(20));
            Planet third = world.Create<Planet>(item => item.Initialize(30));
            MaterializedQuery<Planet, EntityId> materialized = Query.Related(empire, TestDefinitions.Owns)
                .Materialize();
            MaterializedQuery<Planet, EntityId> observed = Query.Related(empire, TestDefinitions.Observes)
                .Materialize();
            var notifications = new List<QueryDelta<Planet, EntityId>>();
            var refreshOrder = new List<string>();
            materialized.Changed += delta =>
            {
                notifications.Add(delta);
                refreshOrder.Add("owns");
            };
            observed.Changed += delta => refreshOrder.Add("observes");
            ScheduledQueryHandle handle = world.Schedule(materialized, QueryRefreshMode.Immediate);
            ScheduledQueryHandle observedHandle = world.Schedule(observed, QueryRefreshMode.Immediate);
            ExpectEqual(3, world.ScheduledQueryDependencyIndexKeyCount);
            long candidateChecks = world.ScheduledQueryCandidateCheckCount;
            refreshOrder.Clear();

            using (world.BeginMutationBatch())
            {
                world.AddRelation(empire, TestDefinitions.Owns, first);
                world.AddRelation(empire, TestDefinitions.Owns, second);
                world.AddRelation(empire, TestDefinitions.Observes, first);
            }

            ExpectEqual(2, notifications.Count);
            Assert.IsTrue(new[] { first, second }.SequenceEqual(notifications[1].Added));
            Assert.IsTrue(new[] { "owns", "observes" }.SequenceEqual(refreshOrder));
            ExpectEqual(2L, materialized.RefreshCount);
            ExpectEqual(2L, materialized.DiffCount);
            ExpectEqual(2L, observed.RefreshCount);
            ExpectEqual(candidateChecks + 2, world.ScheduledQueryCandidateCheckCount);

            refreshOrder.Clear();
            first.Stability.SetBaseValue(15);
            ExpectEqual(2L, materialized.RefreshCount);
            ExpectEqual(2L, observed.RefreshCount);
            ExpectEqual(2L, materialized.DiffCount);
            ExpectEqual(2, notifications.Count);
            ExpectEqual(0, refreshOrder.Count);
            ExpectEqual(candidateChecks + 2, world.ScheduledQueryCandidateCheckCount);

            world.AddRelation(empire, TestDefinitions.Owns, third);
            Assert.IsTrue(new[] { "owns" }.SequenceEqual(refreshOrder));
            ExpectEqual(3L, materialized.RefreshCount);
            ExpectEqual(2L, observed.RefreshCount);
            ExpectEqual(candidateChecks + 3, world.ScheduledQueryCandidateCheckCount);
            Assert.IsFalse(handle.IsPending);
            handle.Dispose();
            observedHandle.Dispose();
            ExpectEqual(0, world.ScheduledQueryCount);
            ExpectEqual(0, world.ScheduledQueryDependencyIndexKeyCount);
        }

        [Test]
        public void DeferredScheduledQuery_CoalescesUntilFlushAndSupportsDisposal()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet first = world.Create<Planet>(item => item.Initialize(10));
            Planet second = world.Create<Planet>(item => item.Initialize(20));
            Planet third = world.Create<Planet>(item => item.Initialize(30));
            MaterializedQuery<Planet, EntityId> materialized = Query.Related(empire, TestDefinitions.Owns)
                .Materialize();
            var notifications = new List<QueryDelta<Planet, EntityId>>();
            materialized.Changed += notifications.Add;
            ScheduledQueryHandle handle = world.Schedule(materialized);

            world.AddRelation(empire, TestDefinitions.Owns, first);
            world.AddRelation(empire, TestDefinitions.Owns, second);
            Assert.IsTrue(handle.IsPending);
            Assert.IsTrue(world.HasPendingScheduledQueries);
            ExpectEqual(1L, materialized.RefreshCount);

            world.FlushScheduledQueries();
            Assert.IsTrue(new[] { first, second }.SequenceEqual(materialized.Current));
            ExpectEqual(2L, materialized.RefreshCount);
            ExpectEqual(2, notifications.Count);
            Assert.IsFalse(handle.IsPending);
            first.Stability.SetBaseValue(15);
            Assert.IsFalse(handle.IsPending);
            ExpectEqual(2L, materialized.RefreshCount);
            Assert.Throws<InvalidOperationException>(() => world.Schedule(materialized));

            handle.Dispose();
            world.AddRelation(empire, TestDefinitions.Owns, third);
            world.FlushScheduledQueries();
            Assert.IsTrue(new[] { first, second }.SequenceEqual(materialized.Current));
            ExpectEqual(2L, materialized.RefreshCount);

            using (world.Schedule(materialized))
                Assert.IsTrue(new[] { first, second, third }.SequenceEqual(materialized.Current));
        }

        [Test]
        public void ScheduledQueryFailure_IsIsolatedAndRecoversOnNextMutation()
        {
            var world = new SimulationWorld();
            Planet first = world.Create<Planet>(item => item.Initialize(10));
            bool fail = false;
            MaterializedQuery<Planet, EntityId> materialized = Query.All<Planet>().Materialize(item =>
            {
                if (fail) throw new InvalidOperationException("Simulated observer failure");
                return item.Id;
            });
            int failures = 0;
            world.ScheduledQueryFailed += failure =>
            {
                failures++;
                throw new InvalidOperationException("Failure observers cannot break committed mutations.");
            };
            using (ScheduledQueryHandle handle = world.Schedule(materialized, QueryRefreshMode.Immediate))
            {
                fail = true;
                Planet second = world.Create<Planet>(item => item.Initialize(20));
                ExpectEqual(1, failures);
                Assert.IsTrue(handle.LastError != null);
                Assert.IsTrue(new[] { first }.SequenceEqual(materialized.Current));

                fail = false;
                Assert.IsTrue(handle.Retry());
                Assert.IsTrue(handle.LastError == null);
                Assert.IsTrue(new[] { first, second }.SequenceEqual(materialized.Current));
                Planet third = world.Create<Planet>(item => item.Initialize(30));
                Assert.IsTrue(new[] { first, second, third }.SequenceEqual(materialized.Current));
            }
        }

        [Test]
        public void SaveRestore_InvalidatesTransientPreparedQueryCaches()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(20));
            world.AddRelation(empire, TestDefinitions.Owns, planet);
            PreparedQuery<Planet> owned = Query.Related(empire, TestDefinitions.Owns);
            var adapter = new PassthroughPersistenceAdapter();

            ExpectEqual(1, owned.Execute(world).Count);
            ExpectEqual(1L, owned.ExecutionCount);
            SimulationSaveState save = world.CaptureSaveState(adapter);

            world.RestoreSaveState(save, adapter);
            ExpectEqual(1, owned.Execute(world).Count);
            ExpectEqual(2L, owned.ExecutionCount);
        }

        [Test]
        public void SaveRestore_PreservesSnapshotMagnitudeAndExpirationTick()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(2));
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            world.AddRelation(empire, TestDefinitions.Owns, planet);
            var definition = new ModifierDefinition<Empire, BonusParameters>("test.persisted", durationTicks: 10)
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability, ModifierOperation.Add,
                    context => context.Source.Skill.FinalValue,
                    magnitudeEvaluation: MagnitudeEvaluation.Snapshot,
                    targetTracking: TargetTracking.Snapshot);
            world.ApplyModifier(definition, empire, default);
            world.AdvanceTo(4);
            var adapter = new PassthroughPersistenceAdapter();
            SimulationSaveState save = world.CaptureSaveState(adapter);

            empire.Skill.SetBaseValue(9);
            world.AdvanceTo(10);
            ExpectEqual(10d, planet.Stability.FinalValue);

            world.RestoreSaveState(save, adapter);
            ExpectEqual(4L, world.CurrentTick);
            ExpectEqual(12d, planet.Stability.FinalValue);
            ExpectEqual(6L, world.Modifiers[0].RemainingTicks.Value);
            world.AdvanceTo(10);
            ExpectEqual(10d, planet.Stability.FinalValue);
        }

        [Test]
        public void DeclaredDependencies_ReevaluateOnlyAffectedModifierInstances()
        {
            var world = new SimulationWorld();
            Empire firstEmpire = world.Create<Empire>(item => item.Initialize(1));
            Empire secondEmpire = world.Create<Empire>(item => item.Initialize(1));
            Planet firstPlanet = world.Create<Planet>(item => item.Initialize(10));
            Planet secondPlanet = world.Create<Planet>(item => item.Initialize(10));
            world.AddRelation(firstEmpire, TestDefinitions.Owns, firstPlanet);
            world.AddRelation(secondEmpire, TestDefinitions.Owns, secondPlanet);
            var evaluations = new Dictionary<EntityId, int>();
            var definition = new ModifierDefinition<Empire, BonusParameters>("test.indexed-dependencies",
                    condition: (simulation, source, parameters) =>
                    {
                        evaluations.TryGetValue(source.Id, out int count);
                        evaluations[source.Id] = count + 1;
                        return source.Skill.FinalValue > 0;
                    }, conditionDescription: "Skill is positive")
                .DependsOn(TestDefinitions.Skill, ModifierDependencyScope.Source)
                .DependsOnRelation(TestDefinitions.Owns)
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability, ModifierOperation.Add,
                    context => 1d, magnitudeEvaluation: MagnitudeEvaluation.Live);

            world.ApplyModifier(definition, firstEmpire, default);
            world.ApplyModifier(definition, secondEmpire, default);
            ExpectEqual(1, evaluations[firstEmpire.Id]);
            ExpectEqual(1, evaluations[secondEmpire.Id]);

            firstEmpire.Skill.SetBaseValue(2);
            ExpectEqual(2, evaluations[firstEmpire.Id]);
            ExpectEqual(1, evaluations[secondEmpire.Id]);
            ExpectEqual(11d, firstPlanet.Stability.FinalValue);
            ExpectEqual(11d, secondPlanet.Stability.FinalValue);
        }

        [Test]
        public void RuntimePersistence_RestoresCapabilitiesCountersAndVariables()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            var capability = new CapabilityDefinition<Empire>("test.persisted-capability",
                CapabilityResolutionPolicy.DenyOverridesAllow);
            CapabilityContributionHandle handle = world.ContributeCapability(empire, capability,
                CapabilityContribution.Allow, sourceDescription: "Persisted technology");
            var counterKey = new CounterKey<Empire, int>("test.victories");
            TypedCounter<Empire, int> counter = world.Counter(counterKey, empire, 0, (left, right) => left + right);
            counter.Increment(5);
            var variableKey = new VariableKey<string>("test.event-owner");
            VariableOwnerId eventChain = VariableOwnerId.EventChain("test.chain.alpha");
            world.Variables.Set(variableKey, "alpha", VariableScope.EventChain, eventChain);
            var adapter = new PassthroughPersistenceAdapter();
            SimulationSaveState save = world.CaptureSaveState(adapter);

            handle.Dispose();
            counter.Set(99);
            world.Variables.Set(variableKey, "changed", VariableScope.EventChain, eventChain);
            world.RestoreSaveState(save, adapter);

            Assert.IsTrue(world.EvaluateCapability(empire, capability).IsAllowed);
            ExpectEqual(5, counter.Value);
            Assert.IsTrue(world.Variables.TryGet(variableKey, out string restored,
                VariableScope.EventChain, eventChain));
            ExpectEqual("alpha", restored);
        }

        [Test]
        public void CapacityPersistence_PreservesMembershipAndDisabledSelection()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet first = world.Create<Planet>(item => item.Initialize(10));
            Planet second = world.Create<Planet>(item => item.Initialize(10));
            var definition = new CapacityDefinition<Empire, Planet>("test.planet-capacity",
                CapacityOverflowPolicy.DisableExcess, CapacitySelectionPolicy.OldestFirst);
            CapacityCollection<Empire, Planet> capacity = world.CreateCapacity(empire, definition, 2);
            capacity.TryAdd(first);
            capacity.TryAdd(second);
            capacity.SetCapacity(1);
            var adapter = new PassthroughPersistenceAdapter();
            SimulationSaveState save = world.CaptureSaveState(adapter);

            capacity.SetCapacity(2);
            capacity.Remove(first);
            world.RestoreSaveState(save, adapter);

            ExpectEqual(1, capacity.Capacity);
            ExpectEqual(2, capacity.Items.Count);
            Assert.IsTrue(capacity.DisabledItems.Contains(first.Id));
        }

        [Test]
        public void DomainAggregator_DeclaresDependenciesAndExplainsEvaluation()
        {
            var world = new SimulationWorld();
            Planet planet = world.Create<Planet>(item => item.Initialize(20));
            var definition = AttributeDefinition<Planet, double>.Derived("test.aggregate-production",
                NumericValuePolicies.Double(), new TripleStabilityAggregator());
            GameAttribute<Planet, double> aggregate = planet.AddCalculated(definition);

            ExpectEqual(60d, aggregate.FinalValue);
            ExpectEqual("Three times final stability", aggregate.EvaluationStages[0].Description);
            planet.Stability.SetBaseValue(10);
            ExpectEqual(30d, aggregate.FinalValue);
        }

        [Test]
        public void AtomicEffect_RollsBackStructuralRelationOnFailure()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            world.AddRelation(empire, TestDefinitions.Owns, planet);

            EffectResult result = world.ExecuteEffect(new FailingTransferEffect(empire, planet), 0);
            ExpectEqual(EffectStatus.Failed, result.Status);
            ExpectEqual(1, world.Related(empire, TestDefinitions.Owns).Count);
        }

        [Test]
        public void MutationBatch_CoalescesInvalidationIntoOneWorldVersion()
        {
            var world = new SimulationWorld();
            Planet first = world.Create<Planet>(item => item.Initialize(10));
            Planet second = world.Create<Planet>(item => item.Initialize(20));
            long before = world.Version;

            using (world.BeginMutationBatch())
            {
                first.Stability.SetBaseValue(30);
                second.Stability.SetBaseValue(40);
            }

            ExpectEqual(before + 1, world.Version);
            ExpectEqual(60d, first.Production.FinalValue);
            ExpectEqual(80d, second.Production.FinalValue);
        }

        [Test]
        public void MutationBatch_CoalescesNonAttributeRuntimeMutations()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            var counterKey = new CounterKey<Empire, int>("test.batched-counter");
            TypedCounter<Empire, int> counter = world.Counter(counterKey, empire, 0, (left, right) => left + right);
            var variableKey = new VariableKey<int>("test.batched-variable");
            var capability = new CapabilityDefinition<Empire>("test.batched-capability",
                CapabilityResolutionPolicy.DenyOverridesAllow);
            var capacityDefinition = new CapacityDefinition<Empire, Planet>("test.batched-capacity",
                CapacityOverflowPolicy.PreserveAndBlockNew, CapacitySelectionPolicy.OldestFirst);
            CapabilityContributionHandle capabilityHandle = null;
            CapacityCollection<Empire, Planet> capacity = null;
            long before = world.Version;

            using (world.BeginMutationBatch())
            {
                counter.Increment(2);
                world.Variables.Set(variableKey, 7, VariableScope.Entity, empire.Id);
                capabilityHandle = world.ContributeCapability(empire, capability, CapabilityContribution.Allow);
                capacity = world.CreateCapacity(empire, capacityDefinition, 3);
            }

            ExpectEqual(before + 1, world.Version);
            ExpectEqual(2, counter.Value);
            Assert.IsTrue(world.Variables.TryGet(variableKey, out int variable, VariableScope.Entity, empire.Id));
            ExpectEqual(7, variable);
            Assert.IsTrue(world.EvaluateCapability(empire, capability).IsAllowed);
            ExpectEqual(3, capacity.Capacity);
            capabilityHandle.Dispose();
        }

        [Test]
        public void AggregateHistory_TracksSummaryWithoutRetainingRecords()
        {
            var world = new SimulationWorld();
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            var policy = new HistoryPolicy(HistoryRecordMode.AggregateOnly,
                changes: HistoryChangeKind.BaseValue, memoryBudgetBytes: 64);
            var definition = new AttributeDefinition<Planet, double>("test.aggregate-history",
                NumericValuePolicies.Double(), policy);
            GameAttribute<Planet, double> value = planet.AddValue(definition, 0);

            value.SetBaseValue(1);
            value.SetBaseValue(3);
            value.SetBaseValue(2);

            ExpectEqual(0, value.History.Count);
            ExpectEqual(3L, value.HistorySummary.Count);
            ExpectEqual(1d, value.HistorySummary.Minimum);
            ExpectEqual(3d, value.HistorySummary.Maximum);
            ExpectEqual(2d, value.HistorySummary.Last);
        }

        [Test]
        public void NamedRandomStreams_ContinueAcrossSaveRestore()
        {
            var world = new SimulationWorld(42);
            var streamId = new StableId<RandomStreamIdKind>("combat.critical-hits");
            XorShiftRandom stream = world.GetRandomStream(streamId);
            stream.NextInt(0, 1000);
            var adapter = new PassthroughPersistenceAdapter();
            SimulationSaveState save = world.CaptureSaveState(adapter);
            int expectedNext = stream.NextInt(0, 1000);

            world.RestoreSaveState(save, adapter);
            int actualNext = world.GetRandomStream(streamId).NextInt(0, 1000);
            ExpectEqual(expectedNext, actualNext);
        }

        [Test]
        public void GroupBySourceStacking_ReplacesPreviousInstanceFromSameSource()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            world.AddRelation(empire, TestDefinitions.Owns, planet);
            var definition = new ModifierDefinition<Empire, BonusParameters>("test.group-source",
                    stacking: new StackingPolicy(StackingMode.GroupBySource))
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability, ModifierOperation.Add,
                    context => context.Parameters.Flat);

            world.ApplyModifier(definition, empire, new BonusParameters(2));
            world.ApplyModifier(definition, empire, new BonusParameters(5));

            ExpectEqual(15d, planet.Stability.FinalValue);
            ExpectEqual(1, world.Modifiers.Count);
        }

        [Test]
        public void CapabilityModifier_TracksTargetsRemovesAndRestoresDeclaratively()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(3));
            Planet first = world.Create<Planet>(item => item.Initialize(10));
            world.AddRelation(empire, TestDefinitions.Owns, first);
            var capability = new CapabilityDefinition<Planet>("test.terraform",
                CapabilityResolutionPolicy.DenyOverridesAllow);
            var definition = new ModifierDefinition<Empire, BonusParameters>("test.capability-modifier")
                .DependsOnRelation(TestDefinitions.Owns)
                .AffectsCapability(TestDefinitions.OwnedPlanets, capability,
                    context => CapabilityContribution.Allow,
                    decisionEvaluation: MagnitudeEvaluation.Snapshot,
                    targetTracking: TargetTracking.Live,
                    conditionDescription: "Terraforming technology is active");
            ModifierInstance instance = world.ApplyModifier(definition, empire, default);

            Assert.IsTrue(world.EvaluateCapability(first, capability).IsAllowed);
            Planet second = world.Create<Planet>(item => item.Initialize(10));
            world.AddRelation(empire, TestDefinitions.Owns, second);
            Assert.IsTrue(world.EvaluateCapability(second, capability).IsAllowed);
            ExpectEqual(2, instance.AffectedCapabilities.Count);

            var adapter = new PassthroughPersistenceAdapter();
            SimulationSaveState save = world.CaptureSaveState(adapter);
            world.RemoveModifier(instance);
            Assert.IsFalse(world.EvaluateCapability(first, capability).IsAllowed);
            world.RestoreSaveState(save, adapter);
            Assert.IsTrue(world.EvaluateCapability(first, capability).IsAllowed);
            ExpectEqual(2, world.Modifiers[0].AffectedCapabilities.Count);

            world.RemoveModifier(world.Modifiers[0]);
            Assert.IsFalse(world.EvaluateCapability(first, capability).IsAllowed);
            Assert.IsFalse(world.EvaluateCapability(second, capability).IsAllowed);
        }

        [Test]
        public void CapacityModifier_IsInspectableReversibleAndPersistent()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            var capacityDefinition = new CapacityDefinition<Empire, Planet>("test.declarative-capacity",
                CapacityOverflowPolicy.PreserveAndBlockNew);
            CapacityCollection<Empire, Planet> capacity = world.CreateCapacity(empire, capacityDefinition, 1);
            var self = new PreparedTargetQuery<Empire, Empire>(source =>
                new PreparedQuery<Empire>(simulation => new[] { source }));
            var modifier = new ModifierDefinition<Empire, int>("test.capacity-modifier")
                .AffectsCapacity(self, capacityDefinition, ModifierOperation.Add,
                    context => context.Parameters,
                    magnitudeEvaluation: MagnitudeEvaluation.Snapshot,
                    targetTracking: TargetTracking.Snapshot);
            ModifierInstance instance = world.ApplyModifier(modifier, empire, 2);

            ExpectEqual(1, capacity.BaseCapacity);
            ExpectEqual(3, capacity.Capacity);
            ExpectEqual(1, capacity.Modifiers.Count);
            ExpectEqual(1, instance.AffectedCapacities.Count);
            var adapter = new PassthroughPersistenceAdapter();
            SimulationSaveState save = world.CaptureSaveState(adapter);

            world.RemoveModifier(instance);
            ExpectEqual(1, capacity.Capacity);
            world.RestoreSaveState(save, adapter);
            ExpectEqual(3, capacity.Capacity);
            ExpectEqual(1, world.Modifiers[0].AffectedCapacities.Count);
            world.RemoveModifier(world.Modifiers[0]);
            ExpectEqual(1, capacity.Capacity);
        }

        [Test]
        public void EffectObserverFailure_DoesNotChangeCommittedResult()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            int dispatchFailures = 0;
            world.DomainEventEmitted += _ => throw new InvalidOperationException("Observer failed");
            world.DomainEventDispatchFailed += _ => dispatchFailures++;

            EffectResult result = world.ExecuteEffect(new IncreaseSkillEffect(empire), 2d, Guid.NewGuid());

            ExpectEqual(EffectStatus.Succeeded, result.Status);
            ExpectEqual(3d, empire.Skill.FinalValue);
            ExpectEqual(1, dispatchFailures);
        }

        [Test]
        public void PersistentHistory_RestoresRecordsAndAggregateSummary()
        {
            var world = new SimulationWorld();
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            planet.Stability.SetBaseValue(20, "saved");
            SimulationSaveState save = world.CaptureSaveState(new PassthroughPersistenceAdapter());
            long savedCount = planet.Stability.HistorySummary.Count;
            planet.Stability.SetBaseValue(30, "after save");

            world.RestoreSaveState(save, new PassthroughPersistenceAdapter());

            ExpectEqual(savedCount, planet.Stability.HistorySummary.Count);
            ExpectEqual(30d, planet.Stability.FinalValue);
            ExpectEqual(savedCount, planet.Stability.HistorySummary.Count);
            Assert.IsTrue(planet.Stability.History.All(item => item.Reason != "after save"));
            Assert.IsTrue(planet.Stability.History.Any(item => item.Reason == "saved"));
        }

        [Test]
        public void CapacityPenaltyAndExplicitDecision_AreObservableAndDeterministic()
        {
            var world = new SimulationWorld();
            Empire owner = world.Create<Empire>(item => item.Initialize(1));
            Planet first = world.Create<Planet>(item => item.Initialize(10));
            Planet second = world.Create<Planet>(item => item.Initialize(20));
            var penaltyDefinition = new CapacityDefinition<Empire, Planet>("test.penalty-capacity",
                CapacityOverflowPolicy.PreserveWithPenalty, overCapacityPenalty: excess => excess * 2d);
            CapacityCollection<Empire, Planet> penalty = world.CreateCapacity(owner, penaltyDefinition, 1);
            penalty.TryAdd(first);
            penalty.TryAdd(second);
            ExpectEqual(1, penalty.OverCapacityAmount);
            ExpectEqual(2d, penalty.CurrentOverCapacityPenalty);

            var decisionDefinition = new CapacityDefinition<Empire, Planet>("test.decision-capacity",
                CapacityOverflowPolicy.RequestDecision, CapacitySelectionPolicy.PlayerSelection);
            CapacityCollection<Empire, Planet> decision = world.CreateCapacity(owner, decisionDefinition, 1);
            decision.TryAdd(first);
            decision.TryAdd(second);
            Assert.IsTrue(decision.RequiresOverflowDecision);
            decision.ResolveOverflowDecision(CapacityDecisionAction.RemoveSelected, new[] { second });
            Assert.IsFalse(decision.RequiresOverflowDecision);
            Assert.IsTrue(new[] { first }.SequenceEqual(decision.Items));
        }

        [Test]
        public void StructuralEffect_RollsBackTagsAndComponents()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            var tag = new TagDefinition<Empire>("test.tag");
            var component = new ComponentDefinition<Empire, int>("test.component");

            EffectResult result = world.ExecuteEffect(new FailingStructuralEffect(empire, tag, component), 0);

            ExpectEqual(EffectStatus.Failed, result.Status);
            Assert.IsFalse(empire.HasTag(tag));
            Assert.IsFalse(empire.TryGetComponent(component, out int _));
        }

        [Test]
        public void ReadOnlyAttribute_RejectsModifierRegistration()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            var readOnly = new AttributeDefinition<Planet, double>("test.read-only",
                NumericValuePolicies.Double(), isModifiable: false);
            planet.AddValue(readOnly, 4);
            var definition = new ModifierDefinition<Empire, double>("test.invalid-read-only");

            Assert.Throws<InvalidOperationException>(() => definition.Affects(
                new PreparedTargetQuery<Empire, Planet>(_ => Query.All<Planet>()),
                readOnly, ModifierOperation.Add, context => context.Parameters));
        }

        [Test]
        public void CrossEntityAndRelationDerivedDependencies_InvalidateIncrementally()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            var output = AttributeDefinition<Empire, double>.Derived("test.empire-output",
                    NumericValuePolicies.Double(),
                    owner => owner.Related(TestDefinitions.Owns).Sum(item => item.Stability.FinalValue),
                    Array.Empty<IAttributeDefinition>())
                .DependsOnGlobal(TestDefinitions.Stability)
                .DependsOnRelation(TestDefinitions.Owns);
            GameAttribute<Empire, double> total = empire.AddCalculated(output);
            ExpectEqual(0d, total.FinalValue);

            world.AddRelation(empire, TestDefinitions.Owns, planet);
            ExpectEqual(10d, total.FinalValue);
            planet.Stability.SetBaseValue(25);
            ExpectEqual(25d, total.FinalValue);
        }

        [Test]
        public void TimeDependentModifier_ReevaluatesOnlyAtDeclaredTimeBoundary()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            world.AddRelation(empire, TestDefinitions.Owns, planet);
            int evaluations = 0;
            var definition = new ModifierDefinition<Empire, double>("test.time-triggered",
                    condition: (simulation, source, value) =>
                    {
                        evaluations++;
                        return simulation.CurrentTick >= 5;
                    },
                    conditionDescription: "Tick reached")
                .DependsOnTime()
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            world.ApplyModifier(definition, empire, 5d);
            ExpectEqual(10d, planet.Stability.FinalValue);

            world.AdvanceTo(5);

            ExpectEqual(15d, planet.Stability.FinalValue);
            ExpectEqual(2, evaluations);
        }

        [Test]
        public void Trigger_TracksDeclaredDependencyAndPersistsState()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            var definition = new TriggerDefinition<int>("test.skill-trigger",
                (simulation, threshold) => new TriggerEvaluation(empire.Skill.FinalValue >= threshold,
                    $"Skill >= {threshold}"),
                dependencies: Query.DependsOn(empire, TestDefinitions.Skill));
            TriggerInstance<int> trigger = world.RegisterTrigger(definition, 3);
            Assert.IsFalse(trigger.IsActive);
            int transitions = 0;
            world.TriggerTransitioned += _ => transitions++;
            SimulationSaveState save = world.CaptureSaveState(new PassthroughPersistenceAdapter());

            empire.Skill.SetBaseValue(4);
            Assert.IsTrue(trigger.IsActive);
            ExpectEqual(1, transitions);
            trigger.SetState(5);
            Assert.IsFalse(trigger.IsActive);

            world.RestoreSaveState(save, new PassthroughPersistenceAdapter());

            ExpectEqual(3, trigger.State);
            Assert.IsFalse(trigger.IsActive);
        }

        [Test]
        public void AttributeRegistration_IsAtomicDetectsCyclesAndFreezesDefinitions()
        {
            var world = new SimulationWorld();
            var first = AttributeDefinition<Empire, double>.Derived("test.cycle.first",
                NumericValuePolicies.Double(), _ => 1d, Array.Empty<IAttributeDefinition>());
            var second = AttributeDefinition<Empire, double>.Derived("test.cycle.second",
                NumericValuePolicies.Double(), _ => 2d, Array.Empty<IAttributeDefinition>());
            first.DependsOnGlobal(second);
            second.DependsOnGlobal(first);

            Assert.Throws<InvalidOperationException>(() => world.RegisterAttribute(first));

            var valid = AttributeDefinition<Empire, double>.Derived("test.frozen",
                    NumericValuePolicies.Double(), _ => 3d, new[] { TestDefinitions.Skill });
            world.RegisterAttribute(valid);
            Assert.Throws<InvalidOperationException>(() => valid.DependsOnGlobal(TestDefinitions.Skill));

            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            ExpectEqual(3d, empire.AddCalculated(valid).FinalValue);

            var modifier = new ModifierDefinition<Empire, double>("test.frozen-modifier")
                .Affects(new PreparedTargetQuery<Empire, Empire>(source =>
                        Query.All<Empire>().Where(item => item == source)),
                    TestDefinitions.Skill, ModifierOperation.Add, context => context.Parameters);
            world.ApplyModifier(modifier, empire, 1d);
            Assert.Throws<InvalidOperationException>(() => modifier.DependsOnTime());
        }

        [Test]
        public void StructuralDefinitions_RequireUniqueStableIdentity()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            var firstRelation = new RelationDefinition<Empire, Planet>("test.unique-relation");
            var duplicateRelation = new RelationDefinition<Empire, Planet>("test.unique-relation");
            world.AddRelation(empire, firstRelation, planet);
            Assert.Throws<InvalidOperationException>(() =>
                world.AddRelation(empire, duplicateRelation, planet));

            var firstTag = new TagDefinition<Empire>("test.unique-tag");
            var duplicateTag = new TagDefinition<Empire>("test.unique-tag");
            world.AddTag(empire, firstTag);
            Assert.Throws<InvalidOperationException>(() => world.AddTag(empire, duplicateTag));
        }

        [Test]
        public void Destroy_CleansRelationsModifiersVariablesCapacitiesAndCapabilitySources()
        {
            var world = new SimulationWorld();
            Empire source = world.Create<Empire>(item => item.Initialize(1));
            Empire owner = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            Planet spare = world.Create<Planet>(item => item.Initialize(10));
            world.AddRelation(source, TestDefinitions.Owns, planet);
            var modifier = new ModifierDefinition<Empire, double>("test.destroy-modifier")
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            world.ApplyModifier(modifier, source, 5d);
            ExpectEqual(15d, planet.Stability.FinalValue);

            var variable = new VariableKey<int>("test.destroy-variable");
            world.Variables.Set(variable, 7, VariableScope.Entity, source.Id);
            TypedCounter<Empire, int> counter = world.Counter(
                new CounterKey<Empire, int>("test.destroy-counter"), source, 0, (left, right) => left + right);
            var capability = new CapabilityDefinition<Planet>("test.destroy-capability",
                CapabilityResolutionPolicy.HighestPriorityWins);
            world.ContributeCapability(planet, capability, CapabilityContribution.Allow, source);
            var capacityDefinition = new CapacityDefinition<Empire, Planet>("test.destroy-capacity",
                CapacityOverflowPolicy.AllowExceeded);
            CapacityCollection<Empire, Planet> capacity =
                world.CreateCapacity(owner, capacityDefinition, 2);
            capacity.TryAdd(planet);

            world.Destroy(source);

            ExpectEqual(10d, planet.Stability.FinalValue);
            Assert.IsFalse(world.Variables.TryGet(variable, out int _, VariableScope.Entity, source.Id));
            Assert.Throws<InvalidOperationException>(() =>
                world.Variables.Set(variable, 8, VariableScope.Entity, source.Id));
            Assert.Throws<ObjectDisposedException>(() => counter.Increment(1));
            Assert.IsFalse(world.EvaluateCapability(planet, capability).IsAllowed);
            ExpectEqual(0, world.RelatedFrom(planet, TestDefinitions.Owns).Count);

            world.Destroy(planet);
            ExpectEqual(0, capacity.Items.Count);
            world.Destroy(owner);
            Assert.Throws<ObjectDisposedException>(() => capacity.TryAdd(spare));
        }

        [Test]
        public void PublicRuntimeCollections_CannotBeCastBackToMutableStorage()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            world.AddRelation(empire, TestDefinitions.Owns, planet);
            var modifier = new ModifierDefinition<Empire, double>("test.readonly-collections")
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            world.ApplyModifier(modifier, empire, 1d);
            var capacityDefinition = new CapacityDefinition<Empire, Planet>("test.readonly-capacity",
                CapacityOverflowPolicy.AllowExceeded);
            CapacityCollection<Empire, Planet> capacity =
                world.CreateCapacity(empire, capacityDefinition, 1);
            capacity.TryAdd(planet);

            Assert.IsFalse(world.Modifiers is List<ModifierInstance>);
            Assert.IsFalse(planet.Stability.Modifiers is List<AttributeContribution<double>>);
            Assert.IsFalse(planet.Stability.EvaluationStages is List<EvaluationStage<double>>);
            Assert.IsFalse(planet.Stability.History is List<ValueChange<double>>);
            Assert.IsFalse(capacity.Items is List<Planet>);
            Assert.IsFalse(capacity.Modifiers is List<CapacityModifierContribution>);
            Assert.IsFalse(TestDefinitions.Stability.Policy.SupportedOperations is HashSet<ModifierOperation>);
        }

        [Test]
        public void CustomOperation_IsPolicyControlledAndOneModifierCanAffectMultipleAttributes()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            world.AddRelation(empire, TestDefinitions.Owns, planet);
            var customPolicy = new DelegateValuePolicy<double>(
                new[] { ModifierOperation.Custom },
                (current, operation, operand) => current + operand * 2d);
            var customAttribute = new AttributeDefinition<Planet, double>(
                "test.custom-operation-attribute", customPolicy);
            GameAttribute<Planet, double> custom = planet.AddValue(customAttribute, 10);
            var definition = new ModifierDefinition<Empire, double>("test.custom-operation")
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters)
                .Affects(TestDefinitions.OwnedPlanets, customAttribute,
                    ModifierOperation.Custom, context => context.Parameters);

            ModifierInstance instance = world.ApplyModifier(definition, empire, 3d);

            ExpectEqual(13d, planet.Stability.FinalValue);
            ExpectEqual(16d, custom.FinalValue);
            ExpectEqual(2, instance.AffectedAttributes.Count);
            Assert.Throws<InvalidOperationException>(() =>
                new ModifierDefinition<Empire, double>("test.rejected-custom")
                    .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                        ModifierOperation.Custom, context => context.Parameters));
        }

        [Test]
        public void PreparedQuery_ProcessesStableBatchesAndExtremes()
        {
            var world = new SimulationWorld();
            world.Create<Planet>(item => item.Initialize(30));
            world.Create<Planet>(item => item.Initialize(10));
            world.Create<Planet>(item => item.Initialize(20));
            PreparedQuery<Planet> query = Query.All<Planet>();
            var visited = new List<EntityId>();

            int batches = query.ProcessBatches(world, 2, batch =>
            {
                for (int index = 0; index < batch.Count; index++) visited.Add(batch[index].Id);
            });

            ExpectEqual(2, batches);
            Assert.IsTrue(visited.SequenceEqual(visited.OrderBy(item => item)));
            ExpectEqual(30d, query.Max(world, item => item.Stability.FinalValue));
            ExpectEqual(10d, query.Min(world, item => item.Stability.FinalValue));
            ExpectEqual(30d, query.MaxBy(world, item => item.Stability.FinalValue).Stability.FinalValue);
        }

        [Test]
        public void RandomEffectContract_RejectsAndRewindsMismatchedDrawCount()
        {
            var world = new SimulationWorld(123);
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            XorShiftRandom stream = world.GetRandomStream(
                new StableId<RandomStreamIdKind>("test.random-contract"));
            ulong state = stream.State;

            EffectResult result = world.ExecuteEffect(new MismatchedRandomEffect(empire), 5d);

            ExpectEqual(EffectStatus.Rejected, result.Status);
            ExpectEqual(state, stream.State);
            ExpectEqual(1d, empire.Skill.FinalValue);
        }

        [Test]
        public void CapabilityPolicies_AreCompleteAndExplainable()
        {
            var world = new SimulationWorld();
            Empire owner = world.Create<Empire>(item => item.Initialize(1));
            var highest = new CapabilityDefinition<Empire>("test.capability-highest",
                CapabilityResolutionPolicy.HighestPriorityWins);
            world.ContributeCapability(owner, highest, CapabilityContribution.Allow, priority: 1,
                sourceDescription: "technology");
            world.ContributeCapability(owner, highest, CapabilityContribution.Deny, priority: 3,
                sourceDescription: "crisis");
            CapabilityEvaluation<Empire> highestResult = world.EvaluateCapability(owner, highest);
            Assert.IsFalse(highestResult.IsAllowed);
            ExpectEqual("crisis", highestResult.WinningContribution.Value.Source);

            var required = new CapabilityDefinition<Empire>("test.capability-required",
                CapabilityResolutionPolicy.AllRequiredMustAllow,
                requiredSources: new[] { "technology", "policy" });
            world.ContributeCapability(owner, required, CapabilityContribution.Allow,
                sourceDescription: "technology");
            Assert.IsFalse(world.EvaluateCapability(owner, required).IsAllowed);
            world.ContributeCapability(owner, required, CapabilityContribution.Allow,
                sourceDescription: "policy");
            Assert.IsTrue(world.EvaluateCapability(owner, required).IsAllowed);
        }

        [Test]
        public void CapacityOverflowPolicies_CoverRemoveClampAndAllow()
        {
            var world = new SimulationWorld();
            Empire owner = world.Create<Empire>(item => item.Initialize(1));
            Planet first = world.Create<Planet>(item => item.Initialize(10));
            Planet second = world.Create<Planet>(item => item.Initialize(20));

            var removeDefinition = new CapacityDefinition<Empire, Planet>("test.capacity-remove",
                CapacityOverflowPolicy.RemoveExcess, CapacitySelectionPolicy.NewestFirst);
            CapacityCollection<Empire, Planet> remove = world.CreateCapacity(owner, removeDefinition, 1);
            remove.TryAdd(first);
            remove.TryAdd(second);
            Assert.IsTrue(new[] { first }.SequenceEqual(remove.Items));

            var clampDefinition = new CapacityDefinition<Empire, Planet>("test.capacity-clamp",
                CapacityOverflowPolicy.ClampReductionToUsage);
            CapacityCollection<Empire, Planet> clamp = world.CreateCapacity(owner, clampDefinition, 2);
            clamp.TryAdd(first);
            clamp.TryAdd(second);
            clamp.SetCapacity(1);
            ExpectEqual(2, clamp.Capacity);
            ExpectEqual(1, clamp.BaseCapacity);
            clamp.Remove(second);
            ExpectEqual(1, clamp.Capacity);

            var allowDefinition = new CapacityDefinition<Empire, Planet>("test.capacity-allow",
                CapacityOverflowPolicy.AllowExceeded);
            CapacityCollection<Empire, Planet> allow = world.CreateCapacity(owner, allowDefinition, 1);
            allow.TryAdd(first);
            allow.TryAdd(second);
            ExpectEqual(1, allow.OverCapacityAmount);
            Assert.IsFalse(allow.RequiresOverflowDecision);
        }

        [Test]
        public void SourceScopedRelationDependency_ReevaluatesOnlyChangedSource()
        {
            var world = new SimulationWorld();
            Empire first = world.Create<Empire>(item => item.Initialize(1));
            Empire second = world.Create<Empire>(item => item.Initialize(1));
            Planet firstPlanet = world.Create<Planet>(item => item.Initialize(10));
            Planet secondPlanet = world.Create<Planet>(item => item.Initialize(10));
            Planet added = world.Create<Planet>(item => item.Initialize(10));
            world.AddRelation(first, TestDefinitions.Owns, firstPlanet);
            world.AddRelation(second, TestDefinitions.Owns, secondPlanet);
            int evaluations = 0;
            var definition = new ModifierDefinition<Empire, double>("test.source-relation-index",
                    condition: (simulation, source, value) =>
                    {
                        evaluations++;
                        return true;
                    })
                .DependsOnRelation(TestDefinitions.Owns, RelationDependencyScope.Source)
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            world.ApplyModifier(definition, first, 1d);
            world.ApplyModifier(definition, second, 1d);
            int initialEvaluations = evaluations;

            world.AddRelation(first, TestDefinitions.Owns, added);

            ExpectEqual(initialEvaluations + 1, evaluations);
            ExpectEqual(11d, added.Stability.FinalValue);
        }

        [Test]
        public void ContiguousEntityBackend_CoalescesLargeBatch()
        {
            var world = new SimulationWorld();
            const int entityCount = 100000;
            using (world.BeginMutationBatch())
                for (int index = 0; index < entityCount; index++) world.Create<BareEntity>();

            ExpectEqual(entityCount, world.Entities.Count);
            ExpectEqual(1L, world.Version);
            ExpectEqual(entityCount, Query.All<BareEntity>().Count(world));
        }

        [Test]
        public void StackingPolicies_CoverStackReplaceStrongestMaximumAndRefresh()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            world.AddRelation(empire, TestDefinitions.Owns, planet);

            var maximum = new ModifierDefinition<Empire, double>("test.stack-maximum",
                    new StackingPolicy(StackingMode.MaximumStacks, maximumStacks: 2))
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            world.ApplyModifier(maximum, empire, 1d);
            world.ApplyModifier(maximum, empire, 1d);
            world.ApplyModifier(maximum, empire, 1d);
            ExpectEqual(12d, planet.Stability.FinalValue);
            ExpectEqual(2, world.Modifiers.Count);

            foreach (ModifierInstance instance in world.Modifiers.ToArray()) world.RemoveModifier(instance);
            var replace = new ModifierDefinition<Empire, double>("test.stack-replace",
                    new StackingPolicy(StackingMode.Replace))
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            world.ApplyModifier(replace, empire, 2d);
            world.ApplyModifier(replace, empire, 4d);
            ExpectEqual(14d, planet.Stability.FinalValue);
            ExpectEqual(1, world.Modifiers.Count);

            foreach (ModifierInstance instance in world.Modifiers.ToArray()) world.RemoveModifier(instance);
            var strongest = new ModifierDefinition<Empire, double>("test.stack-strongest",
                    new StackingPolicy(StackingMode.KeepStrongest), strength: (source, value) => value)
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            ModifierInstance five = world.ApplyModifier(strongest, empire, 5d);
            ExpectEqual(five, world.ApplyModifier(strongest, empire, 3d));
            world.ApplyModifier(strongest, empire, 7d);
            ExpectEqual(17d, planet.Stability.FinalValue);
            ExpectEqual(1, world.Modifiers.Count);

            foreach (ModifierInstance instance in world.Modifiers.ToArray()) world.RemoveModifier(instance);
            var stack = new ModifierDefinition<Empire, double>("test.stack-normal",
                    new StackingPolicy(StackingMode.Stack))
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            world.ApplyModifier(stack, empire, 2d);
            world.ApplyModifier(stack, empire, 3d);
            ExpectEqual(15d, planet.Stability.FinalValue);

            foreach (ModifierInstance instance in world.Modifiers.ToArray()) world.RemoveModifier(instance);
            var refresh = new ModifierDefinition<Empire, double>("test.stack-refresh",
                    new StackingPolicy(StackingMode.RefreshDuration), durationTicks: 5)
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            ModifierInstance timed = world.ApplyModifier(refresh, empire, 2d);
            world.AdvanceTo(3);
            ExpectEqual(timed, world.ApplyModifier(refresh, empire, 9d));
            ExpectEqual(8L, timed.ExpirationTick.Value);
            world.AdvanceTo(7);
            ExpectEqual(12d, planet.Stability.FinalValue);
            world.AdvanceTo(8);
            ExpectEqual(10d, planet.Stability.FinalValue);
        }

        [Test]
        public void HistoryPolicies_CoverSamplingRetentionOverflowBudgetAndNonPersistence()
        {
            var world = new SimulationWorld();
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            var sampledDefinition = new AttributeDefinition<Planet, double>("test.history-sampled",
                NumericValuePolicies.Double(),
                new HistoryPolicy(HistoryRecordMode.Sampled, maximumRecords: 4,
                    retentionTicks: 6, sampleIntervalTicks: 5));
            GameAttribute<Planet, double> sampled = planet.AddValue(sampledDefinition, 0);
            sampled.SetBaseValue(1, "first");
            sampled.SetBaseValue(2, "same sample");
            world.AdvanceTo(5);
            sampled.SetBaseValue(3, "second");
            world.AdvanceTo(12);
            sampled.SetBaseValue(4, "retained");
            ExpectEqual(1, sampled.History.Count);
            ExpectEqual(4L, sampled.HistorySummary.Count);

            var mergedDefinition = new AttributeDefinition<Planet, double>("test.history-merge",
                NumericValuePolicies.Double(),
                new HistoryPolicy(HistoryRecordMode.Exact, maximumRecords: 2,
                    overflowPolicy: HistoryOverflowPolicy.MergeOldest));
            GameAttribute<Planet, double> merged = planet.AddValue(mergedDefinition, 0);
            merged.SetBaseValue(1, "one");
            merged.SetBaseValue(2, "two");
            merged.SetBaseValue(3, "three");
            ExpectEqual(2, merged.History.Count);
            Assert.IsTrue(merged.History[0].Reason.Contains("one"));
            Assert.IsTrue(merged.History[0].Reason.Contains("two"));

            var budgetDefinition = new AttributeDefinition<Planet, double>("test.history-budget",
                NumericValuePolicies.Double(),
                new HistoryPolicy(HistoryRecordMode.Exact, memoryBudgetBytes: 64,
                    estimatedRecordBytes: 64, overflowPolicy: HistoryOverflowPolicy.RejectNewest,
                    persist: false));
            GameAttribute<Planet, double> budgeted = planet.AddValue(budgetDefinition, 0);
            budgeted.SetBaseValue(1, "kept");
            budgeted.SetBaseValue(2, "rejected");
            ExpectEqual(1, budgeted.History.Count);
            SimulationSaveState save = world.CaptureSaveState(new PassthroughPersistenceAdapter());
            budgeted.SetBaseValue(3, "after-save");

            world.RestoreSaveState(save, new PassthroughPersistenceAdapter());

            ExpectEqual(0, budgeted.History.Count);
            ExpectEqual(0L, budgeted.HistorySummary.Count);
        }

        public static TimeSpan RunMillionEntityBenchmark()
        {
            var world = new SimulationWorld();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using (world.BeginMutationBatch())
                for (int index = 0; index < 1000000; index++) world.Create<BareEntity>();
            watch.Stop();
            if (world.Entities.Count != 1000000 || Query.All<BareEntity>().Count(world) != 1000000)
                throw new InvalidOperationException("Million-entity benchmark produced an inconsistent world.");
            return watch.Elapsed;
        }

        private static void ExpectEqual<T>(T expected, T actual)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException($"Expected {expected}, but found {actual}.");
        }

        private sealed class IncreaseSkillEffect : IGameEffect<double>, IEffectEventContract
        {
            private readonly Empire _empire;
            public StableId<EffectIdKind> Id { get; } = new StableId<EffectIdKind>("test.increase-skill");
            public bool IsIdempotent => false;
            public EffectAtomicity Atomicity => EffectAtomicity.Atomic;
            public EffectReversibility Reversibility => EffectReversibility.Rollback;
            public IReadOnlyCollection<Type> EmittedEventTypes { get; } = new[] { typeof(string) };
            public IncreaseSkillEffect(Empire empire) => _empire = empire;
            public EffectValidation Validate(SimulationWorld world, double parameters) =>
                parameters > 0 ? EffectValidation.Valid() : EffectValidation.Rejected("Amount must be positive.");
            public EffectStatus Stage(EffectContext context, double parameters)
            {
                context.StageSetBaseValue(_empire.Skill, _empire.Skill.BaseValue + parameters);
                context.Emit("SkillChanged");
                return EffectStatus.Succeeded;
            }
        }

        private sealed class MismatchedRandomEffect : IGameEffect<double>, IRandomizedGameEffect
        {
            private readonly Empire _empire;
            public StableId<EffectIdKind> Id { get; } =
                new StableId<EffectIdKind>("test.mismatched-random");
            public StableId<RandomStreamIdKind> RandomStreamId { get; } =
                new StableId<RandomStreamIdKind>("test.random-contract");
            public int ExpectedRandomDrawCount => 2;
            public bool IsIdempotent => true;
            public EffectAtomicity Atomicity => EffectAtomicity.Atomic;
            public EffectReversibility Reversibility => EffectReversibility.Rollback;
            public MismatchedRandomEffect(Empire empire) => _empire = empire;
            public EffectValidation Validate(SimulationWorld world, double parameters) =>
                EffectValidation.Valid();
            public EffectStatus Stage(EffectContext context, double parameters)
            {
                context.Random.NextInt(0, 10);
                context.StageSetBaseValue(_empire.Skill, parameters);
                return EffectStatus.Succeeded;
            }
        }

        private sealed class PassthroughPersistenceAdapter : ISimulationPersistenceAdapter
        {
            public object CaptureDomainState(SimulationWorld world) => null;
            public void RestoreDomainState(SimulationWorld world, object state) { }
            public object SerializeModifierParameters(StableId<ModifierIdKind> definitionId, object parameters) => parameters;
            public object DeserializeModifierParameters(StableId<ModifierIdKind> definitionId, object state) => state;
        }

        private sealed class TripleStabilityAggregator : IDomainAggregator<Planet, double>
        {
            public IReadOnlyList<IAttributeDefinition> Dependencies { get; } =
                new IAttributeDefinition[] { TestDefinitions.Stability };
            public double Evaluate(Planet entity) => entity.Stability.FinalValue * 3d;
            public string Explain(Planet entity) => "Three times final stability";
        }

        private sealed class FailingTransferEffect : IGameEffect<int>
        {
            private readonly Empire _empire;
            private readonly Planet _planet;
            public StableId<EffectIdKind> Id { get; } = new StableId<EffectIdKind>("test.failing-transfer");
            public bool IsIdempotent => true;
            public EffectAtomicity Atomicity => EffectAtomicity.Atomic;
            public EffectReversibility Reversibility => EffectReversibility.Rollback;
            public FailingTransferEffect(Empire empire, Planet planet) { _empire = empire; _planet = planet; }
            public EffectValidation Validate(SimulationWorld world, int parameters) => EffectValidation.Valid();
            public EffectStatus Stage(EffectContext context, int parameters)
            {
                context.StageRemoveRelation(_empire, TestDefinitions.Owns, _planet);
                context.Stage(() => throw new InvalidOperationException("Simulated failure"));
                return EffectStatus.Succeeded;
            }
        }

        private sealed class FailingStructuralEffect : IGameEffect<int>
        {
            private readonly Empire _empire;
            private readonly TagDefinition<Empire> _tag;
            private readonly ComponentDefinition<Empire, int> _component;
            public StableId<EffectIdKind> Id { get; } = new StableId<EffectIdKind>("test.failing-structural");
            public bool IsIdempotent => true;
            public EffectAtomicity Atomicity => EffectAtomicity.Atomic;
            public EffectReversibility Reversibility => EffectReversibility.Rollback;

            public FailingStructuralEffect(Empire empire, TagDefinition<Empire> tag,
                ComponentDefinition<Empire, int> component)
            {
                _empire = empire;
                _tag = tag;
                _component = component;
            }

            public EffectValidation Validate(SimulationWorld world, int parameters) => EffectValidation.Valid();

            public EffectStatus Stage(EffectContext context, int parameters)
            {
                context.StageAddTag(_empire, _tag);
                context.StageSetComponent(_empire, _component, 42);
                context.Stage(() => throw new InvalidOperationException("Simulated structural failure"), () => { });
                return EffectStatus.Succeeded;
            }
        }
    }
}
