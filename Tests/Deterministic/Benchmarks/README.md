# Benchmarks de operações determinísticas

Fixture Editor `OperationBenchmarks`, categoria `DeterministicBenchmark`, usando Unity Performance Testing 3.2.0. A fixture é `Explicit`: selecione-a no Test Runner e use **Run Selected**, ou execute pelo Unity MCP com `filter="OperationBenchmarks"`, `include_explicit=true`, `mode="editor"` e `timeout=600`.

## Matriz

| Operação | Casos | Carga |
| --- | ---: | --- |
| Merge/heapsort de comandos | 24 | 128/2048/16384 comandos; quatro produtores; ordem crescente, inversa, embaralhada ou mesmas chaves; arenas Managed e Native |
| Construção de SortedList | 18 | 128/1024/4096 inserções; ordem crescente, inversa e embaralhada; Managed e job Burst |
| Construção de AVL | 18 | Mesmas cargas da SortedList, permitindo comparar deslocamentos com manutenção da árvore |
| Inserção em dicionário | 12 | 128/512/2048 chaves; hash Int32 e hash constante; Managed e job Burst |
| Lookup de dicionário | 12 | 2N consultas, alternando hits e misses; inclui varredura de cadeias com colisões |
| Heap estável: enqueue + drain | 12 | 128/1024/4096 elementos; prioridades únicas ou oito prioridades com empates FIFO |
| XXH64 | 16 | Payloads de 64 B, 4 KiB, 64 KiB e 1 MiB; offsets 0/3; Managed e job Burst |
| Encode canônico | 6 | Arrays de 128/4096/65536 inteiros com varint; inclui passagem de medida e encode |
| Validação + import canônico | 6 | Mesmos arrays; inclui leitura em staging, validação e publicação |

São **124 casos**. O merge ocorre no coordenador host em ambas as arenas; `NativeHost` identifica memória Native. As outras operações Native usam jobs `[BurstCompile(CompileSynchronously = true)]` e verificam o marcador `BurstDiscard` em cada execução.

O wire de array dessa fixture não possui checksum próprio. Checksums do registry de mundo e do histórico são medidos no corpus de runtime/snapshots; o custo composto desses caminhos exige benchmark específico.

## Medição e validação

Cada caso faz oito aquecimentos e 31 medições. Preparação dos inputs, criação dos buffers, reset/clear, validação e escrita de relatórios ficam fora do cronômetro. As capacidades são fixas e o orçamento de snapshots é zero. Os inputs embaralhados usam a seed `0x514c973d` e Fisher–Yates reproduzível.

`BurstJob` mede `Schedule().Complete()` junto com o kernel; isso inclui despacho, sincronização e checks habilitados no Editor. O batch de hashing faz 64 hashes para payloads até 4 KiB e quatro para payloads maiores, com seeds crescentes. O fold de todos os resultados evita ignorar chamadas e é comparado com vetores independentes gerados por `python-xxhash` 3.6.0. Offset zero significa início do buffer, sem exigir um alinhamento específico do array Managed.

O merge é conferido contra `Array.Sort` com comparação independente dos campos do header. As coleções ordenadas são verificadas por rank e valor. O heap usa um oracle com prioridade + posição original para empates. Inserções/consultas verificam todos os valores ou digest e número de hits esperados. Encode compara todos os bytes com uma referência; import verifica todos os elementos.

Tempos usam `Stopwatch.GetTimestamp`. Os grupos `BatchTime`, `PerItemTime`, `MainThreadGcBytes` e, quando aplicável, `ThroughputMiBPerSecond` aparecem no Performance Testing. GC mede **bytes alocados na thread Managed chamadora**, incluindo o agendamento do job; não soma alocações de outras threads. A exportação inclui min, média, mediana, p95 por nearest-rank, ns por item, MiB/s, amostras brutas e GC por amostra. No heap, cada enqueue/dequeue conta como uma operação. No hash, cada hash conta como um item; bytes por amostra incluem todas as repetições.

Os testes falham por saída incorreta ou execução sem Burst nos casos que o exigem. Limites de tempo dependem da máquina e devem ser comparados com um baseline equivalente. Os tempos do Editor e o GC estável medido não incluem custos de inicialização/crescimento da aplicação.

## Relatórios

A comparação dos ajustes de CPU/memória com a execução anterior está em [OPTIMIZATION_RESULTS.md](../../../Tools/Deterministic/OPTIMIZATION_RESULTS.md). O comparador `Tools/Deterministic/compare_operation_benchmarks.py` valida fontes e parâmetros, preserva os casos mais lentos e gera CSV/JSON/Markdown.

O destino padrão é `<projeto>/Temp/DeterministicBenchmarks/operations.csv`, acompanhado de `operations.json`. Para escolher o destino, configure a variável de ambiente `LEGENDARY_DETERMINISTIC_BENCHMARK_OUTPUT` **no processo que executa os testes**, apontando para um CSV. Cada execução da fixture escreve o conjunto selecionado, substituindo o relatório anterior naquele destino.

O JSON inclui CPU, SO, versão Unity, versão das assemblies Burst/Performance Testing, estado dos checks, frequência do relógio e amostras individuais. Os relatórios de referência desta entrega são `Tools/Deterministic/operation-benchmark-unity.csv`, `operation-benchmark-unity.json` e `operation-benchmark-test-results.json`.

Para regenerar os vetores de hashing, use Python com `xxhash==3.6.0`:

```powershell
python Tools/Deterministic/generate_benchmark_hash_vectors.py
```

`--xxhash-path <diretório>` permite usar uma instalação local do módulo. O JSON gerado documenta payload, seeds e fold; atualize `HashOracle` se mudar as cargas.
