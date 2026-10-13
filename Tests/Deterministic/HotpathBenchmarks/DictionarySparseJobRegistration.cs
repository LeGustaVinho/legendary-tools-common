using Unity.Jobs;
using Benchmark = LegendaryTools.Deterministic.Tests.HotpathBenchmarks.DictionarySparseHotpathBenchmarks;

// Schedule sites are generic. Register every closed job so Burst discovers both payload widths.
[assembly: RegisterGenericJobType(typeof(Benchmark.DictionaryOperationJob<Benchmark.Payload64>))]
[assembly: RegisterGenericJobType(typeof(Benchmark.DictionaryOperationJob<Benchmark.Payload256>))]
[assembly: RegisterGenericJobType(typeof(Benchmark.DictionarySnapshotReadJob<Benchmark.Payload64>))]
[assembly: RegisterGenericJobType(typeof(Benchmark.DictionarySnapshotReadJob<Benchmark.Payload256>))]
[assembly: RegisterGenericJobType(typeof(Benchmark.SparseOperationJob<Benchmark.Payload64>))]
[assembly: RegisterGenericJobType(typeof(Benchmark.SparseOperationJob<Benchmark.Payload256>))]

