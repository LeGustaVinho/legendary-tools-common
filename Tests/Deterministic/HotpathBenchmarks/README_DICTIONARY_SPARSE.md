# Benchmarks adicionais de Dictionary e SparseSet

A fixture `DictionarySparseHotpathBenchmarks` mede dez grupos que não estavam nas matrizes anteriores de Dictionary/SparseSet. É `Explicit`, usa a categoria `DeterministicDictionarySparseBenchmark` e tem **264 casos: 132 Managed e 132 BurstJob**.

| Grupo | Workload novo | Casos |
|---|---|---:|
| DS1 | Atualizar 32 valores existentes de 64/256 B, agrupados ou espalhados, com uma raiz retida | 32 |
| DS2 | Remover o mais antigo, meio, mais novo ou ausente; adicionar duplicata antiga, com hash constante e payload 256 B | 40 |
| DS3 | Clear com 64 itens vivos, capacidades 1.024/65.536 e Used baixo/alto | 16 |
| DS4 | Uma inserção que cresce/rehash versus reutilização de slot livre, com proprietário recriado | 16 |
| DS5 | Enumeração na ordem de inserção, armazenamento compacto ou com um item vivo a cada oito slots | 32 |
| DS6 | Snapshot.TryGetAt nos últimos 32 ordinais ou em todos os ordinais, 128/512 itens | 32 |
| DS7 | 64 consultas SparseSet: Get, ContainsKey, slot vazio ou geração antiga, payload 64/256 B | 32 |
| DS8 | Remover o último item denso versus swap removal do primeiro, payload 64/256 B e uma raiz retida | 32 |
| DS9 | Clear de 64 componentes agrupados/espalhados, universo 1.024/1.048.576, uma raiz retida | 16 |
| DS10 | Inserção no último índice permitido: crescer só sparse ou dense+sparse, universo 65.536/1.048.576 | 16 |

DS1–DS6 e DS8–DS10 incluem FullPerTick e CopyOnWrite. DS5 e DS7 têm zero raízes; os demais têm uma raiz. Os buffers têm páginas configuradas de 4.096 bytes; o payload real da página pode ser menor devido ao número inteiro de elementos por página.

A cobertura anterior já incluía inserção e hit/miss de Dictionary<int,int>, importação canônica/rebuild de buckets e scans denso/ordenado de SparseSet<int>. Os novos grupos acrescentam mutações, rejeições com snapshot, ciclo de capacidade, valores grandes, fragmentação e leitura ordinal de snapshots.

O benchmark usa oito warmups, 31 medições de tempo e nove observações separadas de GC.Alloc, com controle vazio de nove execuções. Toda execução confere o conteúdo. Não há limiares de tempo dependentes do hardware.

O tempo inclui a operação/batch e os stores de saída da fixture. BurstJob inclui Schedule+Complete+kernel. Preparação, população inicial, captura/restauração de snapshots, validação, diagnósticos e descarte não entram no tempo. DS4/DS10 recriam o proprietário antes de cada execução, para medir crescimento físico real. As demais mutações restauram a raiz retida. Os read-only workloads repetem o mesmo estado sem reset.

Os oracles verificam todas as palavras explícitas dos payloads, ordem, status, Count, links e buckets do Dictionary, correspondência dense/sparse e gerações, ausência de payload em lookup que falha e conteúdo dos snapshots. O digest é calculado fora do tempo. Ele deve permanecer igual entre execuções equivalentes e entre Managed/Burst. Jobs genéricos são registrados em `DictionarySparseJobRegistration.cs`; cada execução verifica um marcador BurstDiscard dentro do job.

`reservedPayloadBytes` e `livePayloadBytes` vêm de MemoryStats e incluem capacidade física/retida, não apenas itens vivos. `payloadGrowthBytes` e `pageGrowth` são diferenças observadas em relação ao estado preparado. `primaryCapacity` é entries/dense; `secondaryCapacity` é buckets/sparse; `used` aplica-se ao high-water de Dictionary. `retainedLeases` conta as duas leases de buffers por raiz. O diagnóstico de metadata nativa estima tabelas acessíveis; metadata managed é indisponível (−1). `fixtureNativeBytes` inclui só NativeArrays da fixture, excluindo oracles managed. Os valores não incluem overhead de objeto, pinning, allocator, safety ou RSS.

O contador GC de bytes precisa observar uma alocação positiva de 8 KiB na calibração. Se falhar, os campos ficam em −1. Eventos GC.Alloc são observados só na thread chamadora. Native/Burst pode alocar memória nativa mesmo com zero eventos GC. O recorder fica desligado durante os tempos e é restaurado no teardown.

Para executar no Editor conectado, a partir da raiz do projeto:

```powershell
unity command eval 'System.Environment.SetEnvironmentVariable("LEGENDARY_DETERMINISTIC_DICTIONARY_SPARSE_BENCHMARK_OUTPUT", "D:/Entidades/Gustavo/Projects/LegendaryTools/Temp/DeterministicBenchmarks/dictionary-sparse-hotpath.csv"); return true;' --project-path 'D:/Entidades/Gustavo/Projects/LegendaryTools' --format json
unity command run_tests --mode editor --filter DeterministicDictionarySparseBenchmark --filter_type category --include_explicit true --async_tests true --project-path 'D:/Entidades/Gustavo/Projects/LegendaryTools' --format json
```

Leia o resultado final em `Temp/pipeline_test_status.json`. O destino deve terminar em `.csv`; o JSON é escrito ao lado. Confirme sucesso do comando de configuração antes de iniciar. Mantenha resultados temporários fora de Assets durante a coleta e copie os artefatos após o término. Não execute builds ou outros benchmarks simultaneamente na mesma máquina.

Restaure a variável de destino ao terminar:

```powershell
unity command eval 'System.Environment.SetEnvironmentVariable("LEGENDARY_DETERMINISTIC_DICTIONARY_SPARSE_BENCHMARK_OUTPUT", null); return true;' --project-path 'D:/Entidades/Gustavo/Projects/LegendaryTools' --format json
```

O baseline desta rodada e os resultados dos testes estão em [Tools/Deterministic](D:/Entidades/Gustavo/Projects/LegendaryTools/Assets/legendary-tools-common/Tools/Deterministic/DICTIONARY_SPARSE_HOTPATH_OPPORTUNITIES.md). Valide os artefatos e hashes com:

```powershell
python -B Tools/Deterministic/validate_dictionary_sparse_benchmarks.py
```

Preserve as fontes em `dictionary-sparse-benchmark-sources.json` e o baseline para comparar futuras otimizações. Editor/Burst nesta máquina não substitui validação de Player/IL2CPP ou entre arquiteturas.

