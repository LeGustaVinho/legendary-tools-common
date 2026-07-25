using System;
using System.Collections.Generic;
using System.Linq;
using DeterministicFixedPoint;
using LegendaryTools.ModifierSystem;
using NUnit.Framework;

namespace LegendaryTools.Tests.ModifierSystem
{
    public sealed class Empire : WorldEntity
    {
        public GameAttribute<Empire, DetS64> Skill { get; private set; }
        public RelatedEntityCollection<Empire, Planet> Planets => Relation(TestDefinitions.Owns);
        public RelatedEntityCollection<Empire, Planet> ObservedPlanets => Relation(TestDefinitions.Observes);
        public RelatedEntityReference<Empire, Planet> Capital => RelationReference(TestDefinitions.Capital);
        public void Initialize(DetS64 skill) => Skill = AddAttribute(TestDefinitions.Skill, skill);
        public GameAttribute<Empire, DetS64> AddCalculated(AttributeDefinition<Empire, DetS64> definition) =>
            AddAttribute(definition, DetS64.FromLong(0));
    }

    public sealed class Planet : WorldEntity
    {
        public GameAttribute<Planet, DetS64> Stability { get; private set; }
        public GameAttribute<Planet, DetS64> Production { get; private set; }
        public IncomingRelatedEntityCollection<Empire, Planet> Owners => IncomingRelation(TestDefinitions.Owns);
        public RelatedEntityCollection<Planet, Pop> Pops => Relation(TestDefinitions.Contains);

        public void Initialize(DetS64 stability)
        {
            Stability = AddAttribute(TestDefinitions.Stability, stability);
            Production = AddAttribute(TestDefinitions.Production, DetS64.FromLong(0));
        }

        public GameAttribute<Planet, DetS64> AddCalculated(AttributeDefinition<Planet, DetS64> definition) =>
            AddAttribute(definition, DetS64.FromLong(0));
        public GameAttribute<Planet, DetS64> AddValue(AttributeDefinition<Planet, DetS64> definition, DetS64 value) =>
            AddAttribute(definition, value);
    }

    public sealed class BareEntity : WorldEntity
    {
    }

    public sealed class Pop : WorldEntity
    {
        public RelatedEntityReference<Pop, Job> Job => RelationReference(TestDefinitions.WorksAs);
    }

    public sealed class Job : WorldEntity
    {
    }

    internal static class TestDefinitions
    {
        public static readonly HistoryPolicy ExactHistory =
            new HistoryPolicy(HistoryRecordMode.Exact, maximumRecords: 8);

        public static readonly AttributeDefinition<Empire, DetS64> Skill =
            new AttributeDefinition<Empire, DetS64>("test.empire.skill", NumericValuePolicies.FixedS64());

        public static readonly AttributeDefinition<Planet, DetS64> Stability =
            new AttributeDefinition<Planet, DetS64>("test.planet.stability",
                NumericValuePolicies.FixedS64(value => value < 0 || value > 100 ? "Stability must be from 0 to 100." : null),
                ExactHistory);

        public static readonly AttributeDefinition<Planet, DetS64> Production =
            AttributeDefinition<Planet, DetS64>.Derived("test.planet.production", NumericValuePolicies.FixedS64(),
                planet => planet.Stability.FinalValue * DetS64.FromLong(2), new IAttributeDefinition[] { Stability });

        public static readonly RelationDefinition<Empire, Planet> Owns =
            new RelationDefinition<Empire, Planet>("test.owns", maximumToCount: 1);

        public static readonly RelationDefinition<Empire, Planet> Observes =
            new RelationDefinition<Empire, Planet>("test.observes");

        public static readonly RelationDefinition<Empire, Planet> Capital =
            new RelationDefinition<Empire, Planet>("test.capital", maximumFromCount: 1,
                maximumToCount: 1);

        public static readonly RelationDefinition<Planet, Pop> Contains =
            new RelationDefinition<Planet, Pop>("test.contains", maximumToCount: 1);

        public static readonly RelationDefinition<Pop, Job> WorksAs =
            new RelationDefinition<Pop, Job>("test.works-as", maximumFromCount: 1);

        public static readonly PreparedTargetQuery<Empire, Planet> OwnedPlanets =
            new PreparedTargetQuery<Empire, Planet>(empire => Query.Related(empire, Owns));
    }

    public readonly struct BonusParameters
    {
        public DetS64 Flat { get; }
        public BonusParameters(DetS64 flat) => Flat = flat;
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
                    context => DetS64.FromRaw(1500));
            var add = new ModifierDefinition<Empire, BonusParameters>("test.add")
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability, ModifierOperation.Add,
                    context => context.Parameters.Flat);

            world.ApplyModifier(multiply, empire, new BonusParameters(0));
            ModifierInstance addInstance = world.ApplyModifier(add, empire, new BonusParameters(10));

            ExpectEqual(DetS64.FromLong(75), planet.Stability.FinalValue);
            ExpectEqual(DetS64.FromLong(150), planet.Production.FinalValue);
            Assert.IsTrue(new[]
                {
                    AttributeEvaluationStage.Base, AttributeEvaluationStage.Additive,
                    AttributeEvaluationStage.Multiplicative, AttributeEvaluationStage.Final
                }.SequenceEqual(
                    planet.Stability.EvaluationStages.Select(stage => stage.Stage)));
            ExpectEqual(1, addInstance.AffectedAttributes.Count);

            planet.Stability.SetBaseValue(20, "test change");
            ExpectEqual(DetS64.FromLong(45), planet.Stability.FinalValue);
            ExpectEqual(DetS64.FromLong(90), planet.Production.FinalValue);
            Assert.IsTrue(planet.Stability.History.Any(change => change.Reason == "test change"));
        }

        [Test]
        public void ModifierContribution_InvalidatesAlreadyCachedDerivedAttributes()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            world.AddRelation(empire, TestDefinitions.Owns, planet);
            ExpectEqual(DetS64.FromLong(20), planet.Production.FinalValue);

            var definition = new ModifierDefinition<Empire, BonusParameters>("test.derived-invalidation")
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability, ModifierOperation.Add,
                    context => context.Parameters.Flat);
            ModifierInstance instance = world.ApplyModifier(definition, empire, new BonusParameters(5));

            ExpectEqual(DetS64.FromLong(30), planet.Production.FinalValue);
            world.RemoveModifier(instance);
            ExpectEqual(DetS64.FromLong(20), planet.Production.FinalValue);
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
            ExpectEqual(DetS64.FromLong(25), first.Stability.FinalValue);
            ExpectEqual(DetS64.FromLong(35), second.Stability.FinalValue);
            ExpectEqual(2, instance.AffectedAttributes.Count);

            world.RemoveRelation(empire, TestDefinitions.Owns, first);
            ExpectEqual(DetS64.FromLong(20), first.Stability.FinalValue);
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
            ExpectEqual(DetS64.FromLong(12), snapshotPlanet.Stability.FinalValue);
            ExpectEqual(DetS64.FromLong(12), livePlanet.Stability.FinalValue);
            ExpectEqual(DetS64.FromLong(24), livePlanet.Production.FinalValue);

            empire.Skill.SetBaseValue(7);
            ExpectEqual(DetS64.FromLong(12), snapshotPlanet.Stability.FinalValue);
            ExpectEqual(DetS64.FromLong(17), livePlanet.Stability.FinalValue);
            ExpectEqual(DetS64.FromLong(34), livePlanet.Production.FinalValue);
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
            ExpectEqual(DetS64.FromLong(60), planet.Stability.FinalValue);
            ExpectEqual(1L, instance.RemainingTicks.Value);
            world.AdvanceTo(10);
            ExpectEqual(DetS64.FromLong(50), planet.Stability.FinalValue);
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

            ExpectEqual(EffectStatus.Rejected, world.ExecuteEffect(effect, DetS64.FromLong(2)).Status);
            ExpectEqual(EffectStatus.Succeeded, world.ExecuteEffect(effect, DetS64.FromLong(2), executionId).Status);
            ExpectEqual(DetS64.FromLong(3), empire.Skill.FinalValue);
            ExpectEqual(EffectStatus.Duplicate, world.ExecuteEffect(effect, DetS64.FromLong(2), executionId).Status);
            ExpectEqual(DetS64.FromLong(3), empire.Skill.FinalValue);
        }

        [Test]
        public void EffectExecution_PublishesOneStableScheduledQueryBoundary()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            MaterializedQuery<DetS64, int> skill = new PreparedQuery<DetS64>(simulation =>
                    new[] { empire.Skill.FinalValue })
                .Materialize(value => 0);
            using (world.Schedule(skill, QueryRefreshMode.Immediate))
            {
                EffectResult result = world.ExecuteEffect(new IncreaseSkillEffect(empire), DetS64.FromLong(2),
                    Guid.Parse("59141150-804a-4b42-a9d2-e6e8fc75aab8"));

                ExpectEqual(EffectStatus.Succeeded, result.Status);
                ExpectEqual(2L, skill.RefreshCount);
                ExpectEqual(DetS64.FromLong(3), skill.Current[0]);
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
            ExpectEqual(DetS64.FromLong(50), Query.All<Planet>().Average(world, item => item.Stability.FinalValue));
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
        public void NaturalRelationNavigation_ExposesTypedCollectionsInverseViewsAndReferences()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet first = world.Create<Planet>(item => item.Initialize(20));
            Planet second = world.Create<Planet>(item => item.Initialize(30));

            world.AddRelation(empire, TestDefinitions.Owns, first);

            Assert.AreSame(empire.Planets, empire.Planets);
            ExpectEqual(1, empire.Planets.Count);
            Assert.AreSame(first, empire.Planets[0]);
            Assert.IsTrue(empire.Planets.Contains(first));
            Assert.IsTrue(first.Owners.Contains(empire));
            Assert.AreSame(empire, first.Owners.Query.Single(world));

            Assert.IsTrue(empire.Capital.Set(first));
            Assert.AreSame(first, empire.Capital.Value);
            Assert.IsTrue(empire.Capital.Set(second));
            Assert.AreSame(second, empire.Capital.Value);
            Assert.IsFalse(world.HasRelation(empire, TestDefinitions.Capital, first));
            Empire other = world.Create<Empire>(item => item.Initialize(1));
            Assert.IsTrue(other.Capital.Set(first));
            Assert.Throws<InvalidOperationException>(() => empire.Capital.Set(first));
            Assert.AreSame(second, empire.Capital.Value);
            Assert.IsTrue(empire.Capital.Clear());
            Assert.IsFalse(empire.Capital.HasValue);
        }

        [Test]
        public void GraphPath_FollowsArbitraryTypedRelationsWithoutCustomHelpers()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(20));
            Pop first = world.Create<Pop>();
            Pop second = world.Create<Pop>();
            Job miner = world.Create<Job>();
            world.AddRelation(empire, TestDefinitions.Owns, planet);
            world.AddRelation(planet, TestDefinitions.Contains, first);
            world.AddRelation(planet, TestDefinitions.Contains, second);
            first.Job.Set(miner);
            second.Job.Set(miner);

            PreparedQuery<Job> jobs = empire.Planets.Query
                .Follow(TestDefinitions.Contains)
                .Follow(TestDefinitions.WorksAs);

            ExpectEqual(1, jobs.Count(world));
            Assert.AreSame(miner, jobs.Single(world));
        }

        [Test]
        public void RelatedMaterializedQuery_ConsumesAutomaticGraphDeltas()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Empire unrelated = world.Create<Empire>(item => item.Initialize(1));
            PreparedQuery<Planet> prepared = empire.Planets.Query;
            using (MaterializedQuery<Planet, EntityId> materialized = prepared.Materialize())
            using (world.Schedule(materialized, QueryRefreshMode.Immediate))
            {
                ExpectEqual(1L, prepared.ExecutionCount);
                Planet first;
                Planet second;
                using (world.BeginMutationBatch())
                {
                    first = world.Create<Planet>(item => item.Initialize(10));
                    second = world.Create<Planet>(item => item.Initialize(20));
                    world.AddRelation(empire, TestDefinitions.Owns, second);
                    world.AddRelation(empire, TestDefinitions.Owns, first);
                }

                Assert.IsTrue(new[] { first, second }.SequenceEqual(materialized.Current));
                ExpectEqual(1L, prepared.ExecutionCount);
                ExpectEqual(1L, materialized.IncrementalUpdateCount);

                Planet other = world.Create<Planet>(item => item.Initialize(30));
                world.AddRelation(unrelated, TestDefinitions.Owns, other);
                ExpectEqual(2L, materialized.RefreshCount);
                ExpectEqual(1L, prepared.ExecutionCount);

                world.Destroy(first);
                Assert.IsTrue(new[] { second }.SequenceEqual(materialized.Current));
                ExpectEqual(1L, prepared.ExecutionCount);
                ExpectEqual(2L, materialized.IncrementalUpdateCount);
            }
        }

        [Test]
        public void FilteredRelatedQuery_UsesDeltasAndFallsBackForAttributeChanges()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet low = world.Create<Planet>(item => item.Initialize(10));
            Planet high = world.Create<Planet>(item => item.Initialize(60));
            PreparedQuery<Planet> prepared = empire.Planets.Query.Where(
                item => item.Stability.FinalValue >= 50,
                Query.DependsOn(TestDefinitions.Stability));
            using (MaterializedQuery<Planet, EntityId> materialized = prepared.Materialize())
            using (world.Schedule(materialized, QueryRefreshMode.Immediate))
            {
                using (world.BeginMutationBatch())
                {
                    world.AddRelation(empire, TestDefinitions.Owns, low);
                    world.AddRelation(empire, TestDefinitions.Owns, high);
                }
                Assert.IsTrue(new[] { high }.SequenceEqual(materialized.Current));
                ExpectEqual(1L, prepared.ExecutionCount);
                ExpectEqual(1L, materialized.IncrementalUpdateCount);

                low.Stability.SetBaseValue(55);
                Assert.IsTrue(new[] { low, high }.SequenceEqual(materialized.Current));
                ExpectEqual(2L, prepared.ExecutionCount);
            }
        }

        [Test]
        public void PreparedQuery_ComposesSqlStyleJoinGroupingProjectionAndAggregates()
        {
            var world = new SimulationWorld();
            Planet first = world.Create<Planet>(item => item.Initialize(20));
            Planet second = world.Create<Planet>(item => item.Initialize(40));
            var labels = new PreparedQuery<KeyValuePair<EntityId, string>>(_ => new[]
            {
                new KeyValuePair<EntityId, string>(second.Id, "second"),
                new KeyValuePair<EntityId, string>(first.Id, "first")
            });
            PreparedQuery<KeyValuePair<string, DetS64>> joined = Query.All<Planet>().Join(
                labels,
                planet => planet.Id,
                label => label.Key,
                (planet, label) =>
                    new KeyValuePair<string, DetS64>(label.Value, planet.Stability.FinalValue));
            PreparedQuery<QueryGroup<bool, KeyValuePair<string, DetS64>>> grouped =
                joined.GroupBy(item => item.Value >= 30);

            ExpectEqual(2, joined.Count(world));
            ExpectEqual(DetS64.FromLong(60), joined.Sum(world, item => item.Value, (left, right) => left + right, DetS64.FromLong(0)));
            Assert.IsTrue(joined.All(world, item => item.Value >= 20));
            Assert.IsTrue(joined.None(world, item => item.Value < 0));
            ExpectEqual(2, grouped.Count(world));
            Assert.IsTrue(joined.Select(item => item.Key).Execute(world)
                .SequenceEqual(new[] { "first", "second" }));
        }

        [Test]
        public void PreparedQuery_StableOrderingSupportsThenBySkipAndDistinct()
        {
            var world = new SimulationWorld();
            Planet first = world.Create<Planet>(item => item.Initialize(20));
            Planet second = world.Create<Planet>(item => item.Initialize(20));
            Planet third = world.Create<Planet>(item => item.Initialize(10));

            PreparedQuery<Planet> stable = Query.All<Planet>()
                .OrderByDescending(item => item.Stability.FinalValue)
                .ThenBy(item => item.Id);
            Assert.IsTrue(new[] { first, second, third }.SequenceEqual(stable.Execute(world)));

            PreparedQuery<Planet> paged = new PreparedQuery<Planet>(_ =>
                    new[] { first, first, second, third })
                .Distinct()
                .Skip(1)
                .Take(2);
            Assert.IsTrue(new[] { second, third }.SequenceEqual(paged.Execute(world)));
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
            PreparedQuery<DetS64> production = Query.All<Planet>().Select(
                item => item.Production.FinalValue,
                Query.DependsOn(TestDefinitions.Production));

            ExpectEqual(1, unstable.Count(world));
            ExpectEqual(DetS64.FromLong(40), production.Execute(world)[0]);
            ExpectEqual(1L, unstable.ExecutionCount);
            ExpectEqual(1L, production.ExecutionCount);

            empire.Skill.SetBaseValue(2);
            ExpectEqual(1, unstable.Count(world));
            ExpectEqual(DetS64.FromLong(40), production.Execute(world)[0]);
            ExpectEqual(1L, unstable.ExecutionCount);
            ExpectEqual(1L, production.ExecutionCount);

            first.Stability.SetBaseValue(60);
            ExpectEqual(0, unstable.Count(world));
            ExpectEqual(DetS64.FromLong(120), production.Execute(world)[0]);
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
            MaterializedQuery<KeyValuePair<EntityId, DetS64>, EntityId> materialized = Query.All<Planet>()
                .Select(item => new KeyValuePair<EntityId, DetS64>(item.Id, item.Stability.FinalValue), stability)
                .Ordered(item => item.Value, false, stability)
                .Materialize(item => item.Key);
            materialized.Refresh(world);

            first.Stability.SetBaseValue(30);
            QueryDelta<KeyValuePair<EntityId, DetS64>, EntityId> delta = materialized.Refresh(world);

            ExpectEqual(1, delta.Updated.Count);
            ExpectEqual(first.Id, delta.Updated[0].Key);
            ExpectEqual(DetS64.FromLong(10), delta.Updated[0].Previous.Value);
            ExpectEqual(DetS64.FromLong(30), delta.Updated[0].Current.Value);
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
            ExpectEqual(2, world.ScheduledQueryDependencyIndexKeyCount);
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
            ExpectEqual(DetS64.FromLong(10), planet.Stability.FinalValue);

            world.RestoreSaveState(save, adapter);
            ExpectEqual(4L, world.CurrentTick);
            ExpectEqual(DetS64.FromLong(12), planet.Stability.FinalValue);
            ExpectEqual(6L, world.Modifiers[0].RemainingTicks.Value);
            world.AdvanceTo(10);
            ExpectEqual(DetS64.FromLong(10), planet.Stability.FinalValue);
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
                    context => DetS64.FromLong(1), magnitudeEvaluation: MagnitudeEvaluation.Live);

            world.ApplyModifier(definition, firstEmpire, default);
            world.ApplyModifier(definition, secondEmpire, default);
            ExpectEqual(1, evaluations[firstEmpire.Id]);
            ExpectEqual(1, evaluations[secondEmpire.Id]);

            firstEmpire.Skill.SetBaseValue(2);
            ExpectEqual(2, evaluations[firstEmpire.Id]);
            ExpectEqual(1, evaluations[secondEmpire.Id]);
            ExpectEqual(DetS64.FromLong(11), firstPlanet.Stability.FinalValue);
            ExpectEqual(DetS64.FromLong(11), secondPlanet.Stability.FinalValue);
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
            var definition = AttributeDefinition<Planet, DetS64>.Derived("test.aggregate-production",
                NumericValuePolicies.FixedS64(), new TripleStabilityAggregator());
            GameAttribute<Planet, DetS64> aggregate = planet.AddCalculated(definition);

            ExpectEqual(DetS64.FromLong(60), aggregate.FinalValue);
            ExpectEqual("Three times final stability", aggregate.EvaluationStages[0].Description);
            planet.Stability.SetBaseValue(10);
            ExpectEqual(DetS64.FromLong(30), aggregate.FinalValue);
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
            ExpectEqual(DetS64.FromLong(60), first.Production.FinalValue);
            ExpectEqual(DetS64.FromLong(80), second.Production.FinalValue);
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
            var definition = new AttributeDefinition<Planet, DetS64>("test.aggregate-history",
                NumericValuePolicies.FixedS64(), policy);
            GameAttribute<Planet, DetS64> value = planet.AddValue(definition, 0);

            value.SetBaseValue(1);
            value.SetBaseValue(3);
            value.SetBaseValue(2);

            ExpectEqual(0, value.History.Count);
            ExpectEqual(3L, value.HistorySummary.Count);
            ExpectEqual(DetS64.FromLong(1), value.HistorySummary.Minimum);
            ExpectEqual(DetS64.FromLong(3), value.HistorySummary.Maximum);
            ExpectEqual(DetS64.FromLong(2), value.HistorySummary.Last);
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

            ExpectEqual(DetS64.FromLong(15), planet.Stability.FinalValue);
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

            EffectResult result = world.ExecuteEffect(new IncreaseSkillEffect(empire), DetS64.FromLong(2), Guid.NewGuid());

            ExpectEqual(EffectStatus.Succeeded, result.Status);
            ExpectEqual(DetS64.FromLong(3), empire.Skill.FinalValue);
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
            ExpectEqual(DetS64.FromLong(30), planet.Stability.FinalValue);
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
                CapacityOverflowPolicy.PreserveWithPenalty, overCapacityPenalty: excess => excess * DetS64.FromLong(2));
            CapacityCollection<Empire, Planet> penalty = world.CreateCapacity(owner, penaltyDefinition, 1);
            penalty.TryAdd(first);
            penalty.TryAdd(second);
            ExpectEqual(1, penalty.OverCapacityAmount);
            ExpectEqual(DetS64.FromLong(2), penalty.CurrentOverCapacityPenalty);

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
            var readOnly = new AttributeDefinition<Planet, DetS64>("test.read-only",
                NumericValuePolicies.FixedS64(), isModifiable: false);
            planet.AddValue(readOnly, 4);
            var definition = new ModifierDefinition<Empire, DetS64>("test.invalid-read-only");

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
            var output = AttributeDefinition<Empire, DetS64>.Derived("test.empire-output",
                    NumericValuePolicies.FixedS64(),
                    owner => owner.Related(TestDefinitions.Owns)
                        .Aggregate(DetS64.Zero, (sum, item) => sum + item.Stability.FinalValue),
                    Array.Empty<IAttributeDefinition>())
                .DependsOnGlobal(TestDefinitions.Stability)
                .DependsOnRelation(TestDefinitions.Owns);
            GameAttribute<Empire, DetS64> total = empire.AddCalculated(output);
            ExpectEqual(DetS64.FromLong(0), total.FinalValue);

            world.AddRelation(empire, TestDefinitions.Owns, planet);
            ExpectEqual(DetS64.FromLong(10), total.FinalValue);
            planet.Stability.SetBaseValue(25);
            ExpectEqual(DetS64.FromLong(25), total.FinalValue);
        }

        [Test]
        public void TimeDependentModifier_ReevaluatesOnlyAtDeclaredTimeBoundary()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            world.AddRelation(empire, TestDefinitions.Owns, planet);
            int evaluations = 0;
            var definition = new ModifierDefinition<Empire, DetS64>("test.time-triggered",
                    condition: (simulation, source, value) =>
                    {
                        evaluations++;
                        return simulation.CurrentTick >= 5;
                    },
                    conditionDescription: "Tick reached")
                .DependsOnTime()
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            world.ApplyModifier(definition, empire, DetS64.FromLong(5));
            ExpectEqual(DetS64.FromLong(10), planet.Stability.FinalValue);

            world.AdvanceTo(5);

            ExpectEqual(DetS64.FromLong(15), planet.Stability.FinalValue);
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
            var first = AttributeDefinition<Empire, DetS64>.Derived("test.cycle.first",
                NumericValuePolicies.FixedS64(), _ => DetS64.FromLong(1), Array.Empty<IAttributeDefinition>());
            var second = AttributeDefinition<Empire, DetS64>.Derived("test.cycle.second",
                NumericValuePolicies.FixedS64(), _ => DetS64.FromLong(2), Array.Empty<IAttributeDefinition>());
            first.DependsOnGlobal(second);
            second.DependsOnGlobal(first);

            Assert.Throws<InvalidOperationException>(() => world.RegisterAttribute(first));

            var valid = AttributeDefinition<Empire, DetS64>.Derived("test.frozen",
                    NumericValuePolicies.FixedS64(), _ => DetS64.FromLong(3), new[] { TestDefinitions.Skill });
            world.RegisterAttribute(valid);
            Assert.Throws<InvalidOperationException>(() => valid.DependsOnGlobal(TestDefinitions.Skill));

            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            ExpectEqual(DetS64.FromLong(3), empire.AddCalculated(valid).FinalValue);

            var modifier = new ModifierDefinition<Empire, DetS64>("test.frozen-modifier")
                .Affects(new PreparedTargetQuery<Empire, Empire>(source =>
                        Query.All<Empire>().Where(item => item == source)),
                    TestDefinitions.Skill, ModifierOperation.Add, context => context.Parameters);
            world.ApplyModifier(modifier, empire, DetS64.FromLong(1));
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
            var modifier = new ModifierDefinition<Empire, DetS64>("test.destroy-modifier")
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            world.ApplyModifier(modifier, source, DetS64.FromLong(5));
            ExpectEqual(DetS64.FromLong(15), planet.Stability.FinalValue);

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

            ExpectEqual(DetS64.FromLong(10), planet.Stability.FinalValue);
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
            var modifier = new ModifierDefinition<Empire, DetS64>("test.readonly-collections")
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            world.ApplyModifier(modifier, empire, DetS64.FromLong(1));
            var capacityDefinition = new CapacityDefinition<Empire, Planet>("test.readonly-capacity",
                CapacityOverflowPolicy.AllowExceeded);
            CapacityCollection<Empire, Planet> capacity =
                world.CreateCapacity(empire, capacityDefinition, 1);
            capacity.TryAdd(planet);

            Assert.IsFalse(world.Modifiers is List<ModifierInstance>);
            Assert.IsFalse(planet.Stability.Modifiers is List<AttributeContribution<DetS64>>);
            Assert.IsFalse(planet.Stability.EvaluationStages is List<EvaluationStage<DetS64>>);
            Assert.IsFalse(planet.Stability.History is List<ValueChange<DetS64>>);
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
            var customPolicy = new DelegateValuePolicy<DetS64>(
                new[] { ModifierOperation.Custom },
                (current, operation, operand) => current + operand * DetS64.FromLong(2));
            var customAttribute = new AttributeDefinition<Planet, DetS64>(
                "test.custom-operation-attribute", customPolicy);
            GameAttribute<Planet, DetS64> custom = planet.AddValue(customAttribute, 10);
            var definition = new ModifierDefinition<Empire, DetS64>("test.custom-operation")
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters)
                .Affects(TestDefinitions.OwnedPlanets, customAttribute,
                    ModifierOperation.Custom, context => context.Parameters);

            ModifierInstance instance = world.ApplyModifier(definition, empire, DetS64.FromLong(3));

            ExpectEqual(DetS64.FromLong(13), planet.Stability.FinalValue);
            ExpectEqual(DetS64.FromLong(16), custom.FinalValue);
            ExpectEqual(2, instance.AffectedAttributes.Count);
            Assert.Throws<InvalidOperationException>(() =>
                new ModifierDefinition<Empire, DetS64>("test.rejected-custom")
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
            ExpectEqual(DetS64.FromLong(30), query.Max(world, item => item.Stability.FinalValue));
            ExpectEqual(DetS64.FromLong(10), query.Min(world, item => item.Stability.FinalValue));
            ExpectEqual(DetS64.FromLong(30), query.MaxBy(world, item => item.Stability.FinalValue).Stability.FinalValue);
        }

        [Test]
        public void RandomEffectContract_RejectsAndRewindsMismatchedDrawCount()
        {
            var world = new SimulationWorld(123);
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            XorShiftRandom stream = world.GetRandomStream(
                new StableId<RandomStreamIdKind>("test.random-contract"));
            ulong state = stream.State;

            EffectResult result = world.ExecuteEffect(new MismatchedRandomEffect(empire), DetS64.FromLong(5));

            ExpectEqual(EffectStatus.Rejected, result.Status);
            ExpectEqual(state, stream.State);
            ExpectEqual(DetS64.FromLong(1), empire.Skill.FinalValue);
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
            var definition = new ModifierDefinition<Empire, DetS64>("test.source-relation-index",
                    condition: (simulation, source, value) =>
                    {
                        evaluations++;
                        return true;
                    })
                .DependsOnRelation(TestDefinitions.Owns, RelationDependencyScope.Source)
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            world.ApplyModifier(definition, first, DetS64.FromLong(1));
            world.ApplyModifier(definition, second, DetS64.FromLong(1));
            int initialEvaluations = evaluations;

            world.AddRelation(first, TestDefinitions.Owns, added);

            ExpectEqual(initialEvaluations + 1, evaluations);
            ExpectEqual(DetS64.FromLong(11), added.Stability.FinalValue);
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

            var maximum = new ModifierDefinition<Empire, DetS64>("test.stack-maximum",
                    new StackingPolicy(StackingMode.MaximumStacks, maximumStacks: 2))
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            world.ApplyModifier(maximum, empire, DetS64.FromLong(1));
            world.ApplyModifier(maximum, empire, DetS64.FromLong(1));
            world.ApplyModifier(maximum, empire, DetS64.FromLong(1));
            ExpectEqual(DetS64.FromLong(12), planet.Stability.FinalValue);
            ExpectEqual(2, world.Modifiers.Count);

            foreach (ModifierInstance instance in world.Modifiers.ToArray()) world.RemoveModifier(instance);
            var replace = new ModifierDefinition<Empire, DetS64>("test.stack-replace",
                    new StackingPolicy(StackingMode.Replace))
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            world.ApplyModifier(replace, empire, DetS64.FromLong(2));
            world.ApplyModifier(replace, empire, DetS64.FromLong(4));
            ExpectEqual(DetS64.FromLong(14), planet.Stability.FinalValue);
            ExpectEqual(1, world.Modifiers.Count);

            foreach (ModifierInstance instance in world.Modifiers.ToArray()) world.RemoveModifier(instance);
            var strongest = new ModifierDefinition<Empire, DetS64>("test.stack-strongest",
                    new StackingPolicy(StackingMode.KeepStrongest), strength: (source, value) => value)
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            ModifierInstance five = world.ApplyModifier(strongest, empire, DetS64.FromLong(5));
            ExpectEqual(five, world.ApplyModifier(strongest, empire, DetS64.FromLong(3)));
            world.ApplyModifier(strongest, empire, DetS64.FromLong(7));
            ExpectEqual(DetS64.FromLong(17), planet.Stability.FinalValue);
            ExpectEqual(1, world.Modifiers.Count);

            foreach (ModifierInstance instance in world.Modifiers.ToArray()) world.RemoveModifier(instance);
            var stack = new ModifierDefinition<Empire, DetS64>("test.stack-normal",
                    new StackingPolicy(StackingMode.Stack))
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            world.ApplyModifier(stack, empire, DetS64.FromLong(2));
            world.ApplyModifier(stack, empire, DetS64.FromLong(3));
            ExpectEqual(DetS64.FromLong(15), planet.Stability.FinalValue);

            foreach (ModifierInstance instance in world.Modifiers.ToArray()) world.RemoveModifier(instance);
            var refresh = new ModifierDefinition<Empire, DetS64>("test.stack-refresh",
                    new StackingPolicy(StackingMode.RefreshDuration), durationTicks: 5)
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            ModifierInstance timed = world.ApplyModifier(refresh, empire, DetS64.FromLong(2));
            world.AdvanceTo(3);
            ExpectEqual(timed, world.ApplyModifier(refresh, empire, DetS64.FromLong(9)));
            ExpectEqual(8L, timed.ExpirationTick.Value);
            world.AdvanceTo(7);
            ExpectEqual(DetS64.FromLong(12), planet.Stability.FinalValue);
            world.AdvanceTo(8);
            ExpectEqual(DetS64.FromLong(10), planet.Stability.FinalValue);
        }

        [Test]
        public void HistoryPolicies_CoverSamplingRetentionOverflowBudgetAndNonPersistence()
        {
            var world = new SimulationWorld();
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            var sampledDefinition = new AttributeDefinition<Planet, DetS64>("test.history-sampled",
                NumericValuePolicies.FixedS64(),
                new HistoryPolicy(HistoryRecordMode.Sampled, maximumRecords: 4,
                    retentionTicks: 6, sampleIntervalTicks: 5));
            GameAttribute<Planet, DetS64> sampled = planet.AddValue(sampledDefinition, 0);
            sampled.SetBaseValue(1, "first");
            sampled.SetBaseValue(2, "same sample");
            world.AdvanceTo(5);
            sampled.SetBaseValue(3, "second");
            world.AdvanceTo(12);
            sampled.SetBaseValue(4, "retained");
            ExpectEqual(1, sampled.History.Count);
            ExpectEqual(4L, sampled.HistorySummary.Count);

            var mergedDefinition = new AttributeDefinition<Planet, DetS64>("test.history-merge",
                NumericValuePolicies.FixedS64(),
                new HistoryPolicy(HistoryRecordMode.Exact, maximumRecords: 2,
                    overflowPolicy: HistoryOverflowPolicy.MergeOldest));
            GameAttribute<Planet, DetS64> merged = planet.AddValue(mergedDefinition, 0);
            merged.SetBaseValue(1, "one");
            merged.SetBaseValue(2, "two");
            merged.SetBaseValue(3, "three");
            ExpectEqual(2, merged.History.Count);
            Assert.IsTrue(merged.History[0].Reason.Contains("one"));
            Assert.IsTrue(merged.History[0].Reason.Contains("two"));

            var budgetDefinition = new AttributeDefinition<Planet, DetS64>("test.history-budget",
                NumericValuePolicies.FixedS64(),
                new HistoryPolicy(HistoryRecordMode.Exact, memoryBudgetBytes: 64,
                    estimatedRecordBytes: 64, overflowPolicy: HistoryOverflowPolicy.RejectNewest,
                    persist: false));
            GameAttribute<Planet, DetS64> budgeted = planet.AddValue(budgetDefinition, 0);
            budgeted.SetBaseValue(1, "kept");
            budgeted.SetBaseValue(2, "rejected");
            ExpectEqual(1, budgeted.History.Count);
            SimulationSaveState save = world.CaptureSaveState(new PassthroughPersistenceAdapter());
            budgeted.SetBaseValue(3, "after-save");

            world.RestoreSaveState(save, new PassthroughPersistenceAdapter());

            ExpectEqual(0, budgeted.History.Count);
            ExpectEqual(0L, budgeted.HistorySummary.Count);
        }

        [Test]
        public void AtomicEffect_RollsBackTheStepThatThrowsAfterMutation()
        {
            var world = new SimulationWorld();
            int external = 0;
            EffectResult result = world.ExecuteEffect(new PartiallyFailingStepEffect(
                () => external = 1, () => external = 0), 0);
            ExpectEqual(EffectStatus.Failed, result.Status);
            ExpectEqual(0, external);
        }

        [Test]
        public void AtomicEffect_RollbackIsInvisibleAcrossAttributesGraphCollectionsCapacityAndCapabilities()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            var collectionDefinition =
                new CollectionDefinition<Empire, Planet>("test.atomic-observer-collection");
            DeclarativeCollection<Empire, Planet> collection =
                world.CreateCollection(empire, collectionDefinition);
            var capacityDefinition = new CapacityDefinition<Empire, Planet>(
                "test.atomic-observer-capacity", CapacityOverflowPolicy.AllowExceeded);
            CapacityCollection<Empire, Planet> capacity =
                world.CreateCapacity(empire, capacityDefinition, 1);
            var capabilityDefinition = new CapabilityDefinition<Empire>(
                "test.atomic-observer-capability", CapabilityResolutionPolicy.DenyOverridesAllow);
            using (MaterializedQuery<Planet, EntityId> related =
                   Query.Related(empire, TestDefinitions.Owns).Materialize())
            using (world.Schedule(related, QueryRefreshMode.Immediate))
            {
                int attributeNotifications = 0;
                int collectionNotifications = 0;
                int capacityNotifications = 0;
                int queryNotifications = 0;
                planet.Stability.BaseValueChanged += (_, __, ___) => attributeNotifications++;
                collection.Changed += _ => collectionNotifications++;
                capacity.Changed += _ => capacityNotifications++;
                related.Changed += _ => queryNotifications++;
                long version = world.Version;
                int historyCount = planet.Stability.History.Count;

                EffectResult result = world.ExecuteEffect(new CompositeObservableEffect(
                    empire, planet, collection, capacity, capabilityDefinition, true), 0);

                ExpectEqual(EffectStatus.Failed, result.Status);
                ExpectEqual(DetS64.FromLong(10), planet.Stability.BaseValue);
                ExpectEqual(historyCount, planet.Stability.History.Count);
                Assert.IsFalse(world.HasRelation(empire, TestDefinitions.Owns, planet));
                Assert.IsFalse(collection.Contains(planet));
                ExpectEqual(0, capacity.Items.Count);
                Assert.IsFalse(world.EvaluateCapability(empire, capabilityDefinition).IsAllowed);
                ExpectEqual(0, related.Current.Count);
                ExpectEqual(0, attributeNotifications);
                ExpectEqual(0, collectionNotifications);
                ExpectEqual(0, capacityNotifications);
                ExpectEqual(0, queryNotifications);
                ExpectEqual(version, world.Version);
            }
        }

        [Test]
        public void AtomicEffect_PublishesOnlyCommittedStateAndIsolatesObserverFailures()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            var collectionDefinition =
                new CollectionDefinition<Empire, Planet>("test.committed-observer-collection");
            DeclarativeCollection<Empire, Planet> collection =
                world.CreateCollection(empire, collectionDefinition);
            var capacityDefinition = new CapacityDefinition<Empire, Planet>(
                "test.committed-observer-capacity", CapacityOverflowPolicy.AllowExceeded);
            CapacityCollection<Empire, Planet> capacity =
                world.CreateCapacity(empire, capacityDefinition, 1);
            var capabilityDefinition = new CapabilityDefinition<Empire>(
                "test.committed-observer-capability", CapabilityResolutionPolicy.DenyOverridesAllow);
            int observerFailures = 0;
            int consistentNotifications = 0;
            world.EffectObserverDispatchFailed += _ => observerFailures++;
            planet.Stability.BaseValueChanged += (_, __, ___) =>
                throw new InvalidOperationException("Observer failure must not fail a committed effect.");
            planet.Stability.BaseValueChanged += (_, __, ___) =>
            {
                Assert.IsTrue(world.HasRelation(empire, TestDefinitions.Owns, planet));
                Assert.IsTrue(collection.Contains(planet));
                Assert.IsTrue(capacity.Items.Contains(planet));
                Assert.IsTrue(world.EvaluateCapability(empire, capabilityDefinition).IsAllowed);
                consistentNotifications++;
            };

            EffectResult result = world.ExecuteEffect(new CompositeObservableEffect(
                empire, planet, collection, capacity, capabilityDefinition, false), 0);

            ExpectEqual(EffectStatus.Succeeded, result.Status);
            ExpectEqual(DetS64.FromLong(20), planet.Stability.BaseValue);
            ExpectEqual(1, observerFailures);
            ExpectEqual(1, consistentNotifications);
        }

        [Test]
        public void EffectCapacityRollback_RestoresMembershipAndDeterministicSelectionOrder()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet first = world.Create<Planet>(item => item.Initialize(10));
            Planet second = world.Create<Planet>(item => item.Initialize(20));
            var definition = new CapacityDefinition<Empire, Planet>(
                "test.atomic-capacity-order", CapacityOverflowPolicy.RemoveExcess,
                CapacitySelectionPolicy.NewestFirst);
            CapacityCollection<Empire, Planet> capacity = world.CreateCapacity(empire, definition, 2);
            capacity.TryAdd(first);
            capacity.TryAdd(second);
            int notifications = 0;
            capacity.Changed += _ => notifications++;

            EffectResult result = world.ExecuteEffect(
                new FailingCapacityReductionEffect(capacity), 1);

            ExpectEqual(EffectStatus.Failed, result.Status);
            ExpectEqual(2, capacity.Capacity);
            Assert.IsTrue(capacity.Items.Contains(first));
            Assert.IsTrue(capacity.Items.Contains(second));
            ExpectEqual(0, notifications);

            capacity.SetCapacity(1);
            Assert.IsTrue(capacity.Items.Contains(first));
            Assert.IsFalse(capacity.Items.Contains(second));
        }

        [Test]
        public void PartialEffect_PublishesIntentionalPartialCompletion()
        {
            var world = new SimulationWorld();
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            int notifications = 0;
            planet.Stability.BaseValueChanged += (_, __, value) =>
            {
                ExpectEqual(DetS64.FromLong(25), value);
                ExpectEqual(DetS64.FromLong(25), planet.Stability.BaseValue);
                notifications++;
            };

            EffectResult result = world.ExecuteEffect(new PartialAttributeEffect(planet), DetS64.FromLong(25));

            ExpectEqual(EffectStatus.Failed, result.Status);
            ExpectEqual(DetS64.FromLong(25), planet.Stability.BaseValue);
            ExpectEqual(1, notifications);
        }

        [Test]
        public void EffectCapabilityRemoval_RollsBackAndCommitsThroughTypedContract()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            var definition = new CapabilityDefinition<Empire>(
                "test.effect-capability-removal", CapabilityResolutionPolicy.DenyOverridesAllow);
            CapabilityContributionHandle contribution = world.ContributeCapability(
                empire, definition, CapabilityContribution.Allow);

            EffectResult failed = world.ExecuteEffect(
                new CapabilityRemovalEffect(contribution, true), 0);
            ExpectEqual(EffectStatus.Failed, failed.Status);
            Assert.IsTrue(world.EvaluateCapability(empire, definition).IsAllowed);

            EffectResult succeeded = world.ExecuteEffect(
                new CapabilityRemovalEffect(contribution, false), 0);
            ExpectEqual(EffectStatus.Succeeded, succeeded.Status);
            Assert.IsFalse(world.EvaluateCapability(empire, definition).IsAllowed);
            contribution.Dispose();
        }

        [Test]
        public void EffectRuntimeStructureCreation_IsRemovedOnRollbackAndRetainedOnCommit()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            var collectionDefinition =
                new CollectionDefinition<Empire, Planet>("test.effect-created-collection");
            var capacityDefinition = new CapacityDefinition<Empire, Planet>(
                "test.effect-created-capacity", CapacityOverflowPolicy.AllowExceeded);

            EffectResult failed = world.ExecuteEffect(new RuntimeStructureCreationEffect(
                empire, collectionDefinition, capacityDefinition, true), 0);
            ExpectEqual(EffectStatus.Failed, failed.Status);
            Assert.IsNull(world.GetCollection(empire, collectionDefinition));
            Assert.IsNull(world.GetCapacity(empire, capacityDefinition));

            EffectResult succeeded = world.ExecuteEffect(new RuntimeStructureCreationEffect(
                empire, collectionDefinition, capacityDefinition, false), 0);
            ExpectEqual(EffectStatus.Succeeded, succeeded.Status);
            Assert.IsNotNull(world.GetCollection(empire, collectionDefinition));
            Assert.IsNotNull(world.GetCapacity(empire, capacityDefinition));
        }

        [Test]
        public void EffectStage_CannotMutateWorldOutsideTransaction()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            EffectResult result = world.ExecuteEffect(new DirectMutationEffect(empire), DetS64.FromLong(10));
            ExpectEqual(EffectStatus.Failed, result.Status);
            ExpectEqual(DetS64.FromLong(1), empire.Skill.BaseValue);
        }

        [Test]
        public void SharedScopeContribution_InheritsWithoutPerTargetStoredContribution()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet first = world.Create<Planet>(item => item.Initialize(20));
            world.AddRelation(empire, TestDefinitions.Owns, first);
            var definition = new ModifierDefinition<Empire, DetS64>("test.shared-scope")
                .DependsOnRelation(TestDefinitions.Owns)
                .AffectsScope(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            ModifierInstance instance = world.ApplyModifier(definition, empire, DetS64.FromLong(5));
            ExpectEqual(DetS64.FromLong(25), first.Stability.FinalValue);

            Planet second = world.Create<Planet>(item => item.Initialize(30));
            world.AddRelation(empire, TestDefinitions.Owns, second);
            ExpectEqual(DetS64.FromLong(35), second.Stability.FinalValue);
            ExpectEqual(2, instance.AffectedAttributes.Count);
            ExpectEqual(1, first.Stability.Modifiers.Count);
            ExpectEqual(1, second.Stability.Modifiers.Count);
        }

        [Test]
        public void DeclarativeCollection_ModifierIncludesAndRemovesOptions()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet option = world.Create<Planet>(item => item.Initialize(20));
            var collectionDefinition =
                new CollectionDefinition<Empire, Planet>("test.build-options");
            DeclarativeCollection<Empire, Planet> options =
                world.CreateCollection(empire, collectionDefinition);
            var self = new PreparedTargetQuery<Empire, Empire>(
                source => new PreparedQuery<Empire>(_ => new[] { source }));
            var modifier = new ModifierDefinition<Empire, Planet>("test.unlock-option")
                .AffectsCollectionMembership(self, collectionDefinition,
                    context => new[] { context.Parameters });
            ModifierInstance instance = world.ApplyModifier(modifier, empire, option);
            Assert.IsTrue(options.Contains(option));
            ExpectEqual(1, options.Contributions.Count);
            world.RemoveModifier(instance);
            Assert.IsFalse(options.Contains(option));
        }

        [Test]
        public void TriggerProducesModifier_AndWaitingConditionIsInspectable()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(20));
            world.AddRelation(empire, TestDefinitions.Owns, planet);
            var triggerDefinition = new TriggerDefinition<bool>("test.trigger-modifier",
                (simulation, active) => new TriggerEvaluation(active));
            TriggerInstance<bool> trigger = world.RegisterTrigger(triggerDefinition, false);
            var modifier = new ModifierDefinition<Empire, DetS64>("test.triggered-bonus")
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            using (TriggerModifierLink<bool, Empire, DetS64> link =
                   world.ProduceModifierFromTrigger(trigger, modifier, state => empire, state => DetS64.FromLong(7)))
            {
                ExpectEqual(DetS64.FromLong(20), planet.Stability.FinalValue);
                trigger.SetState(true);
                ExpectEqual(DetS64.FromLong(27), planet.Stability.FinalValue);
                SimulationSaveState activeSave =
                    world.CaptureSaveState(new PassthroughPersistenceAdapter());
                trigger.SetState(false);
                ExpectEqual(DetS64.FromLong(20), planet.Stability.FinalValue);
                world.RestoreSaveState(activeSave, new PassthroughPersistenceAdapter());
                ExpectEqual(DetS64.FromLong(27), planet.Stability.FinalValue);
                ExpectEqual(1, world.Modifiers.Count);
                Assert.IsNotNull(link.ProducedModifier);
                trigger.SetState(false);
                ExpectEqual(DetS64.FromLong(20), planet.Stability.FinalValue);
            }

            var waiting = new ModifierDefinition<Empire, DetS64>("test.waiting-condition",
                    conditionDescription: "Waiting for authority")
                .WithCondition((simulation, source, parameters) => ConditionEvaluationState.Waiting)
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters);
            ModifierInstance waitingInstance = world.ApplyModifier(waiting, empire, DetS64.FromLong(99));
            Assert.IsFalse(waitingInstance.IsActive);
            Assert.IsTrue(waitingInstance.Conditions.Single().IsWaiting);
        }

        [Test]
        public void HistoryStreams_RecordTotalsEventsStateTimeAndPersist()
        {
            var world = new SimulationWorld();
            var totals = new HistoryStreamDefinition<DetS64>("test.resource-history",
                new HistoryPolicy(HistoryRecordMode.Exact, maximumRecords: 8,
                    totalAccumulator: (left, right) => (DetS64)left + (DetS64)right,
                    aggregates: HistoryAggregateKind.Total | HistoryAggregateKind.Last));
            world.RecordHistory(totals, DetS64.FromLong(2));
            world.RecordHistory(totals, DetS64.FromLong(3));
            ExpectEqual(DetS64.FromLong(5), world.GetHistory(totals).Summary.Total);
            ExpectEqual(DetS64.FromLong(3), world.GetHistory(totals).Summary.Last);
            ExpectEqual(0L, world.GetHistory(totals).Summary.Count);

            var states = new HistoryStreamDefinition<string>("test.war-state",
                new HistoryPolicy(HistoryRecordMode.Exact, maximumRecords: 8));
            world.TransitionHistory(states, "peace");
            world.AdvanceTo(5);
            world.TransitionHistory(states, "war");
            world.AdvanceTo(10);
            ExpectEqual(5L, world.GetHistory(states).TimeSpentInState("peace", world.CurrentTick));
            ExpectEqual(5L, world.GetHistory(states).TimeSpentInState("war", world.CurrentTick));

            var events = new HistoryStreamDefinition<string>("test.event-history",
                new HistoryPolicy(HistoryRecordMode.Exact, maximumRecords: 8));
            using (world.TrackDomainEvents(events))
                world.ExecuteEffect(new EventOnlyEffect(), 0);
            ExpectEqual(1, world.GetHistory(events).Records.Count);

            var adapter = new PassthroughPersistenceAdapter();
            SimulationSaveState save = world.CaptureSaveState(adapter);
            world.AdvanceTo(20);
            world.RestoreSaveState(save, adapter);
            ExpectEqual(10L, world.CurrentTick);
            ExpectEqual(DetS64.FromLong(5), world.GetHistory(totals).Summary.Total);
            ExpectEqual(5L, world.GetHistory(states).TimeSpentInState("war", world.CurrentTick));
        }

        [Test]
        public void CapabilityRequiredSources_AreTypedAndInspectable()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            var technology = new StableId<CapabilitySourceIdKind>("test.technology");
            var definition = new CapabilityDefinition<Empire>("test.typed-capability",
                CapabilityResolutionPolicy.AllRequiredMustAllow,
                requiredSourceIds: new[] { technology });
            world.ContributeCapability(empire, definition, CapabilityContribution.Allow, technology);
            CapabilityEvaluation<Empire> evaluation = world.EvaluateCapability(empire, definition);
            Assert.IsTrue(evaluation.IsAllowed);
            ExpectEqual(technology, evaluation.Contributions.Single().SourceKey.Value);
        }

        [Test]
        public void CapacityOldestAndNewest_UseInsertionOrderNotEntityId()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet lowerId = world.Create<Planet>(item => item.Initialize(10));
            Planet higherId = world.Create<Planet>(item => item.Initialize(10));
            var definition = new CapacityDefinition<Empire, Planet>("test.insertion-capacity",
                CapacityOverflowPolicy.RemoveExcess, CapacitySelectionPolicy.NewestFirst);
            CapacityCollection<Empire, Planet> capacity = world.CreateCapacity(empire, definition, 2);
            capacity.TryAdd(higherId);
            capacity.TryAdd(lowerId);
            capacity.SetCapacity(1);
            Assert.IsTrue(capacity.Items.Contains(higherId));
            Assert.IsFalse(capacity.Items.Contains(lowerId));
        }

        [Test]
        public void CompensationAndDifferentialSaveLoad_PreserveExecutionTrace()
        {
            var world = new SimulationWorld(123);
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            var compensation = new CompensatingSkillEffect(empire);
            Guid completed = Guid.NewGuid();
            ExpectEqual(EffectStatus.Succeeded,
                world.ExecuteEffect(compensation, DetS64.FromLong(4), completed).Status);
            ExpectEqual(DetS64.FromLong(5), empire.Skill.BaseValue);
            ExpectEqual(EffectStatus.Succeeded,
                world.CompensateEffect(compensation, DetS64.FromLong(4), completed).Status);
            ExpectEqual(DetS64.FromLong(1), empire.Skill.BaseValue);
            ExpectEqual(EffectStatus.Duplicate,
                world.CompensateEffect(compensation, DetS64.FromLong(4), completed).Status);

            var adapter = new SingleEmpirePersistenceAdapter(empire);
            SimulationSaveState checkpoint = world.CaptureSaveState(adapter);
            Guid execution = Guid.NewGuid();
            EffectResult uninterrupted = world.ExecuteEffect(new IncreaseSkillEffect(empire), DetS64.FromLong(3), execution);
            int uninterruptedRandom = world.Random.NextInt(0, 1000);
            DetS64 uninterruptedValue = empire.Skill.BaseValue;

            world.RestoreSaveState(checkpoint, adapter);
            EffectResult resumed = world.ExecuteEffect(new IncreaseSkillEffect(empire), DetS64.FromLong(3), execution);
            int resumedRandom = world.Random.NextInt(0, 1000);
            ExpectEqual(uninterrupted.Status, resumed.Status);
            ExpectEqual(uninterruptedValue, empire.Skill.BaseValue);
            ExpectEqual(uninterruptedRandom, resumedRandom);
        }

        [Test]
        public void BinarySave_FreshWorldContinuationMatchesUninterruptedSimulation()
        {
            var modifier = new ModifierDefinition<Empire, DetS64>(
                    "test.fresh-world-timed", durationTicks: 10)
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => context.Parameters,
                    MagnitudeEvaluation.Snapshot, TargetTracking.Snapshot);
            var capability = new CapabilityDefinition<Empire>(
                "test.fresh-world-capability", CapabilityResolutionPolicy.DenyOverridesAllow);
            var capacityDefinition = new CapacityDefinition<Empire, Planet>(
                "test.fresh-world-capacity", CapacityOverflowPolicy.DisableExcess,
                CapacitySelectionPolicy.OldestFirst);
            var collectionDefinition = new CollectionDefinition<Empire, Planet>(
                "test.fresh-world-collection");
            var counterKey = new CounterKey<Empire, int>("test.fresh-world-counter");
            var variableKey = new VariableKey<string>("test.fresh-world-variable");
            VariableOwnerId eventOwner = VariableOwnerId.EventChain("test.fresh-world-event");
            var streamId = new StableId<RandomStreamIdKind>("test.fresh-world-random");
            var triggerDefinition = new TriggerDefinition<int>(
                "test.fresh-world-trigger",
                (simulation, threshold) =>
                {
                    Empire current = simulation.Entities.OfType<Empire>().Single();
                    return new TriggerEvaluation(
                        current.Skill.FinalValue >= threshold,
                        $"Skill >= {threshold}");
                });
            var historyDefinition = new HistoryStreamDefinition<string>(
                "test.fresh-world-history",
                new HistoryPolicy(HistoryRecordMode.Exact, maximumRecords: 8));
            Guid completedExecution =
                new Guid("37e47b04-30e7-42a3-93b7-2213405b3586");

            var uninterruptedWorld = new SimulationWorld(918273);
            Empire uninterruptedEmpire =
                uninterruptedWorld.Create<Empire>(item => item.Initialize(2));
            Planet uninterruptedPlanet =
                uninterruptedWorld.Create<Planet>(item => item.Initialize(10));
            uninterruptedWorld.AddRelation(
                uninterruptedEmpire, TestDefinitions.Owns, uninterruptedPlanet);
            uninterruptedWorld.ApplyModifier(modifier, uninterruptedEmpire, DetS64.FromLong(2));
            uninterruptedWorld.ContributeCapability(uninterruptedEmpire, capability,
                CapabilityContribution.Allow, sourceDescription: "Persistent technology");
            TypedCounter<Empire, int> uninterruptedCounter = uninterruptedWorld.Counter(
                counterKey, uninterruptedEmpire, 0, (left, right) => left + right);
            uninterruptedCounter.Increment(7);
            uninterruptedWorld.Variables.Set(
                variableKey, "alpha", VariableScope.EventChain, eventOwner);
            CapacityCollection<Empire, Planet> uninterruptedCapacity =
                uninterruptedWorld.CreateCapacity(
                    uninterruptedEmpire, capacityDefinition, 1);
            Assert.IsTrue(uninterruptedCapacity.TryAdd(uninterruptedPlanet));
            DeclarativeCollection<Empire, Planet> uninterruptedCollection =
                uninterruptedWorld.CreateCollection(
                    uninterruptedEmpire, collectionDefinition);
            Assert.IsTrue(uninterruptedCollection.AddBase(uninterruptedPlanet));
            ExpectEqual(EffectStatus.Succeeded, uninterruptedWorld.ExecuteEffect(
                new IncreaseSkillEffect(uninterruptedEmpire), DetS64.FromLong(3), completedExecution).Status);
            TriggerInstance<int> uninterruptedTrigger =
                uninterruptedWorld.RegisterTrigger(triggerDefinition, 4);
            uninterruptedWorld.TransitionHistory(historyDefinition, "peace");
            uninterruptedWorld.GetRandomStream(streamId).NextInt(0, 1000);
            uninterruptedWorld.AdvanceTo(4);
            uninterruptedWorld.TransitionHistory(historyDefinition, "war");

            var uninterruptedAdapter = new ReconstructingPersistenceAdapter(
                modifier, capability, capacityDefinition, collectionDefinition, counterKey,
                triggerDefinition, historyDefinition,
                uninterruptedEmpire, uninterruptedPlanet);
            SimulationSaveState save =
                uninterruptedWorld.CaptureSaveState(uninterruptedAdapter);
            var values = new SimulationValueCodecRegistry();
            RegisterFreshWorldDomainCodec(values);
            var codec = new SimulationSaveBinaryCodec(values);
            byte[] firstBytes = codec.Serialize(save);
            byte[] secondBytes = codec.Serialize(save);
            CollectionAssert.AreEqual(firstBytes, secondBytes);
            SimulationSaveState decoded = codec.Deserialize(firstBytes);
            CollectionAssert.AreEqual(firstBytes, codec.Serialize(decoded));

            EffectStatus uninterruptedDuplicate = uninterruptedWorld.ExecuteEffect(
                new IncreaseSkillEffect(uninterruptedEmpire), DetS64.FromLong(3), completedExecution).Status;
            int uninterruptedRandom =
                uninterruptedWorld.GetRandomStream(streamId).NextInt(0, 1000);
            uninterruptedWorld.AdvanceTo(10);
            BareEntity uninterruptedNext = uninterruptedWorld.Create<BareEntity>();

            var resumedWorld = new SimulationWorld(918273);
            var resumedAdapter = new ReconstructingPersistenceAdapter(
                modifier, capability, capacityDefinition, collectionDefinition, counterKey,
                triggerDefinition, historyDefinition);
            resumedWorld.RestoreSaveState(decoded, resumedAdapter);
            Empire resumedEmpire = resumedAdapter.Empire;
            Planet resumedPlanet = resumedAdapter.Planet;
            EffectStatus resumedDuplicate = resumedWorld.ExecuteEffect(
                new IncreaseSkillEffect(resumedEmpire), DetS64.FromLong(3), completedExecution).Status;
            int resumedRandom = resumedWorld.GetRandomStream(streamId).NextInt(0, 1000);

            ExpectEqual(4L, resumedWorld.CurrentTick);
            ExpectEqual(DetS64.FromLong(12), resumedPlanet.Stability.FinalValue);
            ExpectEqual(uninterruptedDuplicate, resumedDuplicate);
            ExpectEqual(EffectStatus.Duplicate, resumedDuplicate);
            ExpectEqual(uninterruptedRandom, resumedRandom);
            ExpectEqual(1, Query.Related(resumedEmpire, TestDefinitions.Owns).Count(resumedWorld));
            Assert.IsTrue(resumedWorld.EvaluateCapability(resumedEmpire, capability).IsAllowed);
            ExpectEqual(7, resumedWorld.Counter(
                counterKey, resumedEmpire, 0, (left, right) => left + right).Value);
            Assert.IsTrue(resumedWorld.Variables.TryGet(
                variableKey, out string resumedVariable, VariableScope.EventChain, eventOwner));
            ExpectEqual("alpha", resumedVariable);
            Assert.IsTrue(resumedWorld.GetCapacity(
                resumedEmpire, capacityDefinition).Items.Contains(resumedPlanet));
            Assert.IsTrue(resumedWorld.GetCollection(
                resumedEmpire, collectionDefinition).Contains(resumedPlanet));
            Assert.IsTrue(uninterruptedTrigger.IsActive);
            Assert.IsTrue(resumedAdapter.Trigger.IsActive);
            ExpectEqual(uninterruptedTrigger.State, resumedAdapter.Trigger.State);
            ExpectEqual("war", resumedWorld.GetHistory(historyDefinition).CurrentState);

            resumedWorld.AdvanceTo(10);
            BareEntity resumedNext = resumedWorld.Create<BareEntity>();
            ExpectEqual(uninterruptedWorld.CurrentTick, resumedWorld.CurrentTick);
            ExpectEqual(uninterruptedPlanet.Stability.FinalValue,
                resumedPlanet.Stability.FinalValue);
            ExpectEqual(DetS64.FromLong(10), resumedPlanet.Stability.FinalValue);
            ExpectEqual(uninterruptedNext.Id, resumedNext.Id);
            ExpectEqual(uninterruptedEmpire.Skill.BaseValue,
                resumedEmpire.Skill.BaseValue);
            ExpectEqual(uninterruptedPlanet.Stability.History.Count,
                resumedPlanet.Stability.History.Count);
            ExpectEqual(
                uninterruptedWorld.GetHistory(historyDefinition)
                    .TimeSpentInState("war", uninterruptedWorld.CurrentTick),
                resumedWorld.GetHistory(historyDefinition)
                    .TimeSpentInState("war", resumedWorld.CurrentTick));
        }

        [Test]
        public void BinarySave_RoundTripsPersistentTriggersAndHistoryStreams()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(4));
            var triggerDefinition = new TriggerDefinition<int>(
                "test.binary-trigger",
                (simulation, threshold) => new TriggerEvaluation(
                    empire.Skill.FinalValue >= threshold, $"Skill >= {threshold}"),
                dependencies: Query.DependsOn(empire, TestDefinitions.Skill));
            world.RegisterTrigger(triggerDefinition, 3);
            var historyDefinition = new HistoryStreamDefinition<string>(
                "test.binary-state-history",
                new HistoryPolicy(HistoryRecordMode.Exact, maximumRecords: 8));
            world.TransitionHistory(historyDefinition, "peace");
            world.AdvanceTo(5);
            world.TransitionHistory(historyDefinition, "war");
            world.AdvanceTo(9);

            SimulationSaveState save = world.CaptureSaveState(
                new PassthroughPersistenceAdapter());
            var codec = new SimulationSaveBinaryCodec();
            byte[] bytes = codec.Serialize(save);
            SimulationSaveState decoded = codec.Deserialize(bytes);

            ExpectEqual(1, decoded.Runtime.Triggers.Count);
            ExpectEqual("test.binary-trigger",
                decoded.Runtime.Triggers[0].DefinitionId);
            ExpectEqual(3, decoded.Runtime.Triggers[0].State);
            Assert.IsTrue(decoded.Runtime.Triggers[0].IsActive);
            ExpectEqual(1, decoded.Runtime.HistoryStreams.Count);
            HistoryStreamState history = decoded.Runtime.HistoryStreams[0];
            ExpectEqual("test.binary-state-history", history.DefinitionId);
            ExpectEqual("war", history.CurrentState);
            ExpectEqual(5L, history.StateDurations.Single().Ticks);
            CollectionAssert.AreEqual(bytes, codec.Serialize(decoded));
        }

        [Test]
        public void StableReadCaches_ReusesContributionAndCollectionViews()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            world.AddRelation(empire, TestDefinitions.Owns, planet);
            var shared = new ModifierDefinition<Empire, DetS64>("test.cached-shared-scope")
                .AffectsScope(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => DetS64.FromLong(2));
            world.ApplyModifier(shared, empire, DetS64.FromLong(0));

            IReadOnlyList<AttributeContribution<DetS64>> firstModifiers =
                planet.Stability.Modifiers;
            IReadOnlyList<AttributeContribution<DetS64>> secondModifiers =
                planet.Stability.Modifiers;
            Assert.AreSame(firstModifiers, secondModifiers);
            ExpectEqual(DetS64.FromLong(12), planet.Stability.FinalValue);
            ExpectEqual(DetS64.FromLong(12), planet.Stability.FinalValue);

            var definition = new CollectionDefinition<Empire, Planet>(
                "test.cached-declarative-collection");
            DeclarativeCollection<Empire, Planet> collection =
                world.CreateCollection(empire, definition, new[] { planet });
            IReadOnlyList<Planet> firstItems = collection.Items;
            long resolutions = collection.ResolutionCount;
            IReadOnlyList<Planet> secondItems = collection.Items;
            Assert.AreSame(firstItems, secondItems);
            Assert.IsTrue(collection.Contains(planet));
            ExpectEqual(resolutions, collection.ResolutionCount);

            Planet second = world.Create<Planet>(item => item.Initialize(20));
            collection.AddBase(second);
            Assert.IsFalse(ReferenceEquals(firstItems, collection.Items));
            ExpectEqual(resolutions + 1, collection.ResolutionCount);
        }

        [Test]
        public void RelationDerivedInvalidation_IsScopedToChangedSource()
        {
            var world = new SimulationWorld();
            Empire first = world.Create<Empire>(item => item.Initialize(1));
            Empire second = world.Create<Empire>(item => item.Initialize(1));
            int firstEvaluations = 0;
            int secondEvaluations = 0;
            var ownedCount = AttributeDefinition<Empire, DetS64>.Derived(
                    "test.incremental-owned-count", NumericValuePolicies.FixedS64(),
                    empire =>
                    {
                        if (ReferenceEquals(empire, first)) firstEvaluations++;
                        else if (ReferenceEquals(empire, second)) secondEvaluations++;
                        return empire.Related(TestDefinitions.Owns).Count;
                    }, Array.Empty<IAttributeDefinition>())
                .DependsOnRelation(TestDefinitions.Owns);
            GameAttribute<Empire, DetS64> firstCount = first.AddCalculated(ownedCount);
            GameAttribute<Empire, DetS64> secondCount = second.AddCalculated(ownedCount);
            _ = firstCount.FinalValue;
            _ = secondCount.FinalValue;

            Planet planet = world.Create<Planet>(item => item.Initialize(10));
            _ = firstCount.FinalValue;
            _ = secondCount.FinalValue;
            int beforeFirst = firstEvaluations;
            int beforeSecond = secondEvaluations;
            world.AddRelation(first, TestDefinitions.Owns, planet);
            ExpectEqual(DetS64.FromLong(1), firstCount.FinalValue);
            ExpectEqual(DetS64.FromLong(0), secondCount.FinalValue);
            ExpectEqual(beforeFirst + 1, firstEvaluations);
            ExpectEqual(beforeSecond, secondEvaluations);
        }

        [Test]
        public void MaterializedQuery_AppliesProducerDeltaWithoutFullExecution()
        {
            var world = new SimulationWorld();
            Planet first = world.Create<Planet>(item => item.Initialize(10));
            PreparedQuery<Planet> plan = Query.All<Planet>();
            MaterializedQuery<Planet, EntityId> view = plan.Materialize();
            view.Refresh(world);
            long executions = plan.ExecutionCount;
            Planet second = world.Create<Planet>(item => item.Initialize(20));
            QueryDelta<Planet, EntityId> added = view.ApplyDelta(world, new[] { second },
                Array.Empty<EntityId>());
            ExpectEqual(executions, plan.ExecutionCount);
            ExpectEqual(1, added.Added.Count);
            ExpectEqual(2, view.Current.Count);
            QueryDelta<Planet, EntityId> removed = view.ApplyDelta(world, Array.Empty<Planet>(),
                new[] { first.Id });
            ExpectEqual(1, removed.Removed.Count);
            ExpectEqual(second.Id, view.Current.Single().Id);
            ExpectEqual(2L, view.IncrementalUpdateCount);
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

        public static TimeSpan RunMillionContributionBenchmark()
        {
            var world = new SimulationWorld();
            Empire empire = world.Create<Empire>(item => item.Initialize(1));
            Planet planet = world.Create<Planet>(item => item.Initialize(0));
            world.AddRelation(empire, TestDefinitions.Owns, planet);
            var modifier = new ModifierDefinition<Empire, DetS64>("benchmark.million-contributions")
                .Affects(TestDefinitions.OwnedPlanets, TestDefinitions.Stability,
                    ModifierOperation.Add, context => DetS64.FromRaw(1));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using (world.BeginMutationBatch())
                for (int index = 0; index < 1000000; index++)
                    world.ApplyModifier(modifier, empire, DetS64.FromLong(0));
            DetS64 result = planet.Stability.FinalValue;
            watch.Stop();
            if (result != DetS64.FromLong(1010) || planet.Stability.Modifiers.Count != 1000000)
                throw new InvalidOperationException(
                    "Million-contribution benchmark produced an inconsistent final value.");
            return watch.Elapsed;
        }

        private static void ExpectEqual<T>(T expected, T actual)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException($"Expected {expected}, but found {actual}.");
        }

        private sealed class PartiallyFailingStepEffect : IGameEffect<int>
        {
            private readonly Action _mutate;
            private readonly Action _rollback;
            public StableId<EffectIdKind> Id { get; } =
                new StableId<EffectIdKind>("test.partially-failing-step");
            public bool IsIdempotent => true;
            public EffectAtomicity Atomicity => EffectAtomicity.Atomic;
            public EffectReversibility Reversibility => EffectReversibility.Rollback;
            public PartiallyFailingStepEffect(Action mutate, Action rollback)
            {
                _mutate = mutate;
                _rollback = rollback;
            }
            public EffectValidation Validate(SimulationWorld world, int parameters) =>
                EffectValidation.Valid();
            public EffectStatus Stage(EffectContext context, int parameters)
            {
                context.Stage(() =>
                {
                    _mutate();
                    throw new InvalidOperationException("Failure after mutation");
                }, _rollback);
                return EffectStatus.Succeeded;
            }
        }

        private sealed class CompositeObservableEffect : IGameEffect<int>
        {
            private readonly Empire _empire;
            private readonly Planet _planet;
            private readonly DeclarativeCollection<Empire, Planet> _collection;
            private readonly CapacityCollection<Empire, Planet> _capacity;
            private readonly CapabilityDefinition<Empire> _capability;
            private readonly bool _fail;
            public StableId<EffectIdKind> Id { get; } =
                new StableId<EffectIdKind>("test.composite-observable-effect");
            public bool IsIdempotent => true;
            public EffectAtomicity Atomicity => EffectAtomicity.Atomic;
            public EffectReversibility Reversibility => EffectReversibility.Rollback;

            public CompositeObservableEffect(Empire empire, Planet planet,
                DeclarativeCollection<Empire, Planet> collection,
                CapacityCollection<Empire, Planet> capacity,
                CapabilityDefinition<Empire> capability, bool fail)
            {
                _empire = empire;
                _planet = planet;
                _collection = collection;
                _capacity = capacity;
                _capability = capability;
                _fail = fail;
            }

            public EffectValidation Validate(SimulationWorld world, int parameters) =>
                EffectValidation.Valid();

            public EffectStatus Stage(EffectContext context, int parameters)
            {
                context.StageSetBaseValue(_planet.Stability, DetS64.FromLong(20));
                context.StageAddRelation(_empire, TestDefinitions.Owns, _planet);
                context.StageAddCollectionItem(_collection, _planet);
                context.StageAddCapacityItem(_capacity, _planet);
                context.StageContributeCapability(_empire, _capability,
                    CapabilityContribution.Allow, sourceDescription: "Committed effect");
                if (_fail)
                    context.Stage(() => throw new InvalidOperationException("Simulated failure"), () => { });
                return EffectStatus.Succeeded;
            }
        }

        private sealed class FailingCapacityReductionEffect : IGameEffect<int>
        {
            private readonly CapacityCollection<Empire, Planet> _capacity;
            public StableId<EffectIdKind> Id { get; } =
                new StableId<EffectIdKind>("test.failing-capacity-reduction");
            public bool IsIdempotent => true;
            public EffectAtomicity Atomicity => EffectAtomicity.Atomic;
            public EffectReversibility Reversibility => EffectReversibility.Rollback;
            public FailingCapacityReductionEffect(CapacityCollection<Empire, Planet> capacity) =>
                _capacity = capacity;
            public EffectValidation Validate(SimulationWorld world, int parameters) =>
                EffectValidation.Valid();
            public EffectStatus Stage(EffectContext context, int parameters)
            {
                context.StageSetCapacity(_capacity, parameters);
                context.Stage(() => throw new InvalidOperationException("Simulated failure"), () => { });
                return EffectStatus.Succeeded;
            }
        }

        private sealed class PartialAttributeEffect : IGameEffect<DetS64>
        {
            private readonly Planet _planet;
            public StableId<EffectIdKind> Id { get; } =
                new StableId<EffectIdKind>("test.partial-attribute");
            public bool IsIdempotent => true;
            public EffectAtomicity Atomicity => EffectAtomicity.PartialAllowed;
            public EffectReversibility Reversibility => EffectReversibility.None;
            public PartialAttributeEffect(Planet planet) => _planet = planet;
            public EffectValidation Validate(SimulationWorld world, DetS64 parameters) =>
                EffectValidation.Valid();
            public EffectStatus Stage(EffectContext context, DetS64 parameters)
            {
                context.StageSetBaseValue(_planet.Stability, parameters);
                context.Stage(() => throw new InvalidOperationException("Intentional partial failure"));
                return EffectStatus.Succeeded;
            }
        }

        private sealed class CapabilityRemovalEffect : IGameEffect<int>
        {
            private readonly CapabilityContributionHandle _contribution;
            private readonly bool _fail;
            public StableId<EffectIdKind> Id { get; } =
                new StableId<EffectIdKind>("test.capability-removal-effect");
            public bool IsIdempotent => true;
            public EffectAtomicity Atomicity => EffectAtomicity.Atomic;
            public EffectReversibility Reversibility => EffectReversibility.Rollback;
            public CapabilityRemovalEffect(CapabilityContributionHandle contribution, bool fail)
            {
                _contribution = contribution;
                _fail = fail;
            }
            public EffectValidation Validate(SimulationWorld world, int parameters) =>
                EffectValidation.Valid();
            public EffectStatus Stage(EffectContext context, int parameters)
            {
                context.StageRemoveCapability(_contribution);
                if (_fail)
                    context.Stage(() => throw new InvalidOperationException("Simulated failure"), () => { });
                return EffectStatus.Succeeded;
            }
        }

        private sealed class RuntimeStructureCreationEffect : IGameEffect<int>
        {
            private readonly Empire _owner;
            private readonly CollectionDefinition<Empire, Planet> _collection;
            private readonly CapacityDefinition<Empire, Planet> _capacity;
            private readonly bool _fail;
            public StableId<EffectIdKind> Id { get; } =
                new StableId<EffectIdKind>("test.runtime-structure-creation-effect");
            public bool IsIdempotent => true;
            public EffectAtomicity Atomicity => EffectAtomicity.Atomic;
            public EffectReversibility Reversibility => EffectReversibility.Rollback;
            public RuntimeStructureCreationEffect(Empire owner,
                CollectionDefinition<Empire, Planet> collection,
                CapacityDefinition<Empire, Planet> capacity, bool fail)
            {
                _owner = owner;
                _collection = collection;
                _capacity = capacity;
                _fail = fail;
            }
            public EffectValidation Validate(SimulationWorld world, int parameters) =>
                EffectValidation.Valid();
            public EffectStatus Stage(EffectContext context, int parameters)
            {
                context.StageCreateCollection(_owner, _collection);
                context.StageCreateCapacity(_owner, _capacity, 3);
                if (_fail)
                    context.Stage(() => throw new InvalidOperationException("Simulated failure"), () => { });
                return EffectStatus.Succeeded;
            }
        }

        private sealed class DirectMutationEffect : IGameEffect<DetS64>
        {
            private readonly Empire _empire;
            public StableId<EffectIdKind> Id { get; } =
                new StableId<EffectIdKind>("test.direct-mutation");
            public bool IsIdempotent => true;
            public EffectAtomicity Atomicity => EffectAtomicity.Atomic;
            public EffectReversibility Reversibility => EffectReversibility.Rollback;
            public DirectMutationEffect(Empire empire) => _empire = empire;
            public EffectValidation Validate(SimulationWorld world, DetS64 parameters) =>
                EffectValidation.Valid();
            public EffectStatus Stage(EffectContext context, DetS64 parameters)
            {
                _empire.Skill.SetBaseValue(parameters);
                return EffectStatus.Succeeded;
            }
        }

        private sealed class EventOnlyEffect : IGameEffect<int>, IEffectEventContract
        {
            public StableId<EffectIdKind> Id { get; } =
                new StableId<EffectIdKind>("test.event-only");
            public bool IsIdempotent => true;
            public EffectAtomicity Atomicity => EffectAtomicity.Atomic;
            public EffectReversibility Reversibility => EffectReversibility.Rollback;
            public IReadOnlyCollection<Type> EmittedEventTypes { get; } = new[] { typeof(string) };
            public EffectValidation Validate(SimulationWorld world, int parameters) =>
                EffectValidation.Valid();
            public EffectStatus Stage(EffectContext context, int parameters)
            {
                context.Emit("RecordedEvent");
                return EffectStatus.Succeeded;
            }
        }

        private sealed class CompensatingSkillEffect : ICompensatingGameEffect<DetS64>
        {
            private readonly Empire _empire;
            public StableId<EffectIdKind> Id { get; } =
                new StableId<EffectIdKind>("test.compensating-skill");
            public bool IsIdempotent => false;
            public EffectAtomicity Atomicity => EffectAtomicity.Atomic;
            public EffectReversibility Reversibility => EffectReversibility.Compensation;
            public CompensatingSkillEffect(Empire empire) => _empire = empire;
            public EffectValidation Validate(SimulationWorld world, DetS64 parameters) =>
                parameters > 0 ? EffectValidation.Valid() : EffectValidation.Rejected("Positive only");
            public EffectStatus Stage(EffectContext context, DetS64 parameters)
            {
                context.StageSetBaseValue(_empire.Skill, _empire.Skill.BaseValue + parameters);
                return EffectStatus.Succeeded;
            }
            public EffectValidation ValidateCompensation(SimulationWorld world, DetS64 parameters,
                Guid executionId) => EffectValidation.Valid();
            public EffectStatus StageCompensation(EffectContext context, DetS64 parameters,
                Guid executionId)
            {
                context.StageSetBaseValue(_empire.Skill, _empire.Skill.BaseValue - parameters);
                return EffectStatus.Succeeded;
            }
        }

        private sealed class SingleEmpirePersistenceAdapter : ISimulationPersistenceAdapter
        {
            private readonly Empire _empire;
            public SingleEmpirePersistenceAdapter(Empire empire) => _empire = empire;
            public object CaptureDomainState(SimulationWorld world) => _empire.Skill.BaseValue;
            public void RestoreDomainState(SimulationWorld world, object state) =>
                _empire.Skill.SetBaseValue((DetS64)state, "Restore domain checkpoint");
            public object SerializeModifierParameters(StableId<ModifierIdKind> definitionId,
                object parameters) => parameters;
            public object DeserializeModifierParameters(StableId<ModifierIdKind> definitionId,
                object state) => state;
        }

        private static void RegisterFreshWorldDomainCodec(
            SimulationValueCodecRegistry values)
        {
            values.Register<FreshWorldDomainState>("tests.fresh-world-domain.v1",
                (writer, state) =>
                {
                    writer.Write(state.EmpireSkill.Raw);
                    writer.Write(state.PlanetStability.Raw);
                },
                reader => new FreshWorldDomainState
                {
                    EmpireSkill = DetS64.FromRaw(reader.ReadInt64()),
                    PlanetStability = DetS64.FromRaw(reader.ReadInt64())
                });
        }

        private sealed class FreshWorldDomainState
        {
            public DetS64 EmpireSkill { get; set; }
            public DetS64 PlanetStability { get; set; }
        }

        private sealed class ReconstructingPersistenceAdapter :
            ISimulationPersistenceAdapter
        {
            private readonly ModifierDefinition<Empire, DetS64> _modifier;
            private readonly CapabilityDefinition<Empire> _capability;
            private readonly CapacityDefinition<Empire, Planet> _capacity;
            private readonly CollectionDefinition<Empire, Planet> _collection;
            private readonly CounterKey<Empire, int> _counter;
            private readonly TriggerDefinition<int> _trigger;
            private readonly HistoryStreamDefinition<string> _history;

            public Empire Empire { get; private set; }
            public Planet Planet { get; private set; }
            public TriggerInstance<int> Trigger { get; private set; }

            public ReconstructingPersistenceAdapter(
                ModifierDefinition<Empire, DetS64> modifier,
                CapabilityDefinition<Empire> capability,
                CapacityDefinition<Empire, Planet> capacity,
                CollectionDefinition<Empire, Planet> collection,
                CounterKey<Empire, int> counter,
                TriggerDefinition<int> trigger,
                HistoryStreamDefinition<string> history,
                Empire empire = null,
                Planet planet = null)
            {
                _modifier = modifier;
                _capability = capability;
                _capacity = capacity;
                _collection = collection;
                _counter = counter;
                _trigger = trigger;
                _history = history;
                Empire = empire;
                Planet = planet;
            }

            public object CaptureDomainState(SimulationWorld world)
            {
                if (Empire == null || Planet == null)
                    throw new InvalidOperationException("Domain entities are not initialized.");
                return new FreshWorldDomainState
                {
                    EmpireSkill = Empire.Skill.BaseValue,
                    PlanetStability = Planet.Stability.BaseValue
                };
            }

            public void RestoreDomainState(SimulationWorld world, object state)
            {
                if (world.Entities.Count != 0)
                    throw new InvalidOperationException(
                        "This adapter reconstructs only a fresh SimulationWorld.");
                var domain = (FreshWorldDomainState)state;
                world.RegisterModifier(_modifier);
                world.RegisterCapability(_capability);
                world.RegisterHistory(_history);
                Empire = world.Create<Empire>(
                    item => item.Initialize(domain.EmpireSkill));
                Planet = world.Create<Planet>(
                    item => item.Initialize(domain.PlanetStability));
                world.AddRelation(Empire, TestDefinitions.Owns, Planet);
                Trigger = world.RegisterTrigger(_trigger, 0);
                world.Counter(_counter, Empire, 0, (left, right) => left + right);
                world.CreateCapacity(Empire, _capacity, 0);
                world.CreateCollection(Empire, _collection);
            }

            public object SerializeModifierParameters(
                StableId<ModifierIdKind> definitionId, object parameters) =>
                parameters;

            public object DeserializeModifierParameters(
                StableId<ModifierIdKind> definitionId, object state) => state;
        }

        private sealed class IncreaseSkillEffect : IGameEffect<DetS64>, IEffectEventContract
        {
            private readonly Empire _empire;
            public StableId<EffectIdKind> Id { get; } = new StableId<EffectIdKind>("test.increase-skill");
            public bool IsIdempotent => false;
            public EffectAtomicity Atomicity => EffectAtomicity.Atomic;
            public EffectReversibility Reversibility => EffectReversibility.Rollback;
            public IReadOnlyCollection<Type> EmittedEventTypes { get; } = new[] { typeof(string) };
            public IncreaseSkillEffect(Empire empire) => _empire = empire;
            public EffectValidation Validate(SimulationWorld world, DetS64 parameters) =>
                parameters > 0 ? EffectValidation.Valid() : EffectValidation.Rejected("Amount must be positive.");
            public EffectStatus Stage(EffectContext context, DetS64 parameters)
            {
                context.StageSetBaseValue(_empire.Skill, _empire.Skill.BaseValue + parameters);
                context.Emit("SkillChanged");
                return EffectStatus.Succeeded;
            }
        }

        private sealed class MismatchedRandomEffect : IGameEffect<DetS64>, IRandomizedGameEffect
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
            public EffectValidation Validate(SimulationWorld world, DetS64 parameters) =>
                EffectValidation.Valid();
            public EffectStatus Stage(EffectContext context, DetS64 parameters)
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

        private sealed class TripleStabilityAggregator : IDomainAggregator<Planet, DetS64>
        {
            public IReadOnlyList<IAttributeDefinition> Dependencies { get; } =
                new IAttributeDefinition[] { TestDefinitions.Stability };
            public DetS64 Evaluate(Planet entity) => entity.Stability.FinalValue * DetS64.FromLong(3);
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
