using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicHotpathOptimization")]
    public sealed class InputIndexOptimizationTests
    {
        private static int Value(ulong player, long tick) => unchecked((int)(tick * 65537 + (long)player * 101));
        private static void Expected(SortedDictionary<long, int> actual, long boundary, out int value, out long tick)
        {
            tick = -1; value = -999;
            foreach (var pair in actual) { if (pair.Key >= boundary) break; tick = pair.Key; value = pair.Value; }
        }
        [TestCase(2, DeterministicBackend.Managed)] [TestCase(3, DeterministicBackend.Managed)]
        [TestCase(31, DeterministicBackend.Managed)] [TestCase(32, DeterministicBackend.Managed)]
        [TestCase(33, DeterministicBackend.Managed)] [TestCase(65, DeterministicBackend.Managed)]
#if UNITY_5_3_OR_NEWER
        [TestCase(2, DeterministicBackend.BurstCompiler)] [TestCase(33, DeterministicBackend.BurstCompiler)]
        [TestCase(65, DeterministicBackend.BurstCompiler)] [TestCase(1025, DeterministicBackend.BurstCompiler)]
#endif
        public void IndexedPredictionsMatchOrderedHistoryAcrossWrapRewindRosterInsertionAndFutureForget(int history, DeterministicBackend backend)
        {
#if UNITY_5_3_OR_NEWER
            var context = new DeterministicRuntimeContext(backend, backend == DeterministicBackend.Managed ? null : new NativeDeterministicMemoryFactory());
#else
            var context = new DeterministicRuntimeContext(backend);
#endif
            using (var inputs = new DeterministicInputHistory<int, Int32Policy>(context, 4, history, PredictionPolicy.HoldLast, -999, initialTick: 11))
            {
                var model = new SortedDictionary<ulong, SortedDictionary<long, int>>();
                foreach (ulong p in new ulong[] { 6, 4, 2 })
                { inputs.TryRegisterPlayer(new PlayerId(p), 11).ThrowIfFailure(); inputs.TrySeedLastInput(new PlayerId(p), 10, Value(p, 10)).ThrowIfFailure(); model[p] = new SortedDictionary<long, int> { [10] = Value(p, 10) }; }
                for (int step = 0; step < Math.Max(180, history * 3); step++)
                {
                    long tick = inputs.NextTick;
                    if (step == 71)
                    {
                        inputs.TryRegisterPlayer(new PlayerId(1), tick).ThrowIfFailure(); long seed = inputs.OldestTick - 1;
                        inputs.TrySeedLastInput(new PlayerId(1), seed, Value(1, seed)).ThrowIfFailure(); model[1] = new SortedDictionary<long, int> { [seed] = Value(1, seed) };
                    }
                    foreach (var player in model)
                    {
                        if ((step + (int)player.Key) % 5 == 0) Submit(inputs, player.Key, tick, player.Value);
                        if ((step + (int)player.Key) % 11 == 0) Submit(inputs, player.Key, tick + history - 1, player.Value);
                        long late = Math.Max(inputs.OldestTick, tick - Math.Min(3, history - 1));
                        if (player.Key != 1 && step % 7 == 0) Submit(inputs, player.Key, late, player.Value);
                        if (step % 17 == 0)
                        {
                            inputs.TryForgetFutureInputs(new PlayerId(player.Key), tick + 1).ThrowIfFailure();
                            var remove = new List<long>(); foreach (var pair in player.Value) if (pair.Key >= tick + 1) remove.Add(pair.Key);
                            foreach (long at in remove) player.Value.Remove(at);
                        }
                        foreach (long boundary in new[] { inputs.OldestTick, tick, tick + 1, tick + history })
                        {
                            Expected(player.Value, boundary, out int expected, out long at);
                            inputs.TryGetLastActual(new PlayerId(player.Key), boundary, out int actual, out long actualTick).ThrowIfFailure();
                            Assert.AreEqual(expected, actual); Assert.AreEqual(at, actualTick);
                        }
                    }
                    CheckFrame(inputs, model); inputs.TryAdvance(tick).ThrowIfFailure();
                    if (step % 23 == 0 && inputs.NextTick > inputs.OldestTick + 1)
                    {
                        long target = inputs.NextTick; inputs.TryRewind(Math.Max(inputs.OldestTick, target - 2)).ThrowIfFailure();
                        while (inputs.NextTick < target) { CheckFrame(inputs, model); inputs.TryAdvance(inputs.NextTick).ThrowIfFailure(); }
                    }
                }
            }
        }
        private static void Submit(DeterministicInputHistory<int, Int32Policy> inputs, ulong player, long tick, SortedDictionary<long, int> model)
        { int value = Value(player, tick); inputs.TrySubmit(new PlayerId(player), tick, value, out _).ThrowIfFailure(); model[tick] = value; }
        private static void CheckFrame(DeterministicInputHistory<int, Int32Policy> inputs, SortedDictionary<ulong, SortedDictionary<long, int>> model)
        {
            inputs.TryResolve(inputs.NextTick, true, out var frame).ThrowIfFailure(); Assert.AreEqual(model.Count, frame.Count); int index = 0;
            foreach (var player in model)
            {
                bool present = player.Value.TryGetValue(inputs.NextTick, out int expected);
                if (!present) Expected(player.Value, inputs.NextTick, out expected, out _);
                Assert.AreEqual(player.Key, frame[index].Player.Value); Assert.AreEqual(expected, frame[index].Value); Assert.AreEqual(!present, frame[index].Predicted); index++;
            }
        }
    }
}
