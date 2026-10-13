# Benchmarks de oportunidades em hotpaths

Os grafos têm uma fixture adicional explícita, `GraphHotpathBenchmarks`, com 216 casos
de inserção, algoritmos, CSR, COW, canonical/deltas e versões persistentes. Matriz,
oportunidades, contrato de medição e reprodução estão em
[GRAPH_HOTPATH_BENCHMARKS.md](../../../Tools/Deterministic/GRAPH_HOTPATH_BENCHMARKS.md).

A comparação das otimizações usa o mesmo corpus e está em
[GRAPH_OPTIMIZATION_COMPARISON.md](../../../Tools/Deterministic/GRAPH_OPTIMIZATION_COMPARISON.md).

A fixture adicional `GraphExtendedHotpathBenchmarks` cobre 186 casos de lifecycle,
limpeza, leituras, DFS/ciclos não direcionados, deltas estruturais e rejeições de
transferência. A matriz, resultados e reprodução estão em
[GRAPH_EXTENDED_HOTPATH_BENCHMARKS.md](../../../Tools/Deterministic/GRAPH_EXTENDED_HOTPATH_BENCHMARKS.md).

A comparação das otimizações G9–G14 está em
[GRAPH_EXTENDED_OPTIMIZATION_COMPARISON.md](../../../Tools/Deterministic/GRAPH_EXTENDED_OPTIMIZATION_COMPARISON.md).

Fixture: `LegendaryTools.Deterministic.Tests.HotpathBenchmarks.HotpathOpportunityBenchmarks`.
Categoria: `DeterministicHotpathBenchmark`. Os testes são explícitos: selecione a fixture
no Unity Test Runner e execute **Run Selected**, ou use um executor com
`include_explicit=true` e filtro pelo nome da fixture.

| ID | Carga | Casos |
| --- | --- | ---: |
| H1 | Encode de mundo: 512/8192 valores, 1/16 bindings | 8 |
| H2 | Restore do mesmo mundo com Full, keyframe/delta e COW | 24 |
| H3 | 32 pares remove/add em heap/AVL com quatro snapshots retidos | 16 |
| H4 | Enumeração densa/ordenada: 64/512 ativos em 1024/65536/1048576 slots | 24 |
| H5 | Count/enumeração de bitset: 4096/65536 bits, três densidades | 24 |
| H6 | Predição de 1/64 players após 0/128/4096 ticks sem input real | 24 |
| H7 | Construção, 64 inserções e 0/4 primeiras capturas: Fixed/Grow, Full/COW | 16 |
| Total | Managed: 68; NativeHost: 36; BurstJob: 32 | 136 |

## Como interpretar

- Operações estáveis: oito aquecimentos e 31 amostras. Construção: dois aquecimentos
  e nove amostras; cada amostra cria um proprietário novo. Não representa um processo
  frio ou um alocador de sistema operacional frio.
- O tempo inclui somente a ação. Setup, restore de preparação, validação, probes,
  descarte da amostra anterior e escrita dos relatórios ficam fora dele.
- Cada job mede `Schedule + Complete + kernel`, com compilação Burst síncrona e
  marcador `BurstDiscard` verificado após cada execução. Não interprete esses
  tempos como custo isolado do kernel.
- H3 restaura a raiz original antes de **cada** amostra. Assim, as páginas
  compartilhadas precisam atravessar novamente o write barrier. As quatro raízes
  são verificadas integralmente após a mutação.
- H4 usa uma permutação de inserção reproduzível e identidades espalhadas até o
  último slot. As duas views produzem os mesmos elementos em ordens diferentes.
  H4/H5 incluem a escrita em um buffer de saída pré-alocado durante enumeração.
- H5 Count normaliza por palavra de 64 bits; enumeração normaliza por bit emitido.
  H3 normaliza por 64 operações, H6 por player, H1/H2 por valor, H7 por construção.
- H6 ativa os players no tick medido. O prefixo com roster vazio e o carry anterior
  são preparados fora do tempo. `HoldLast` precisa procurar input real no prefixo;
  `Neutral` serve como referência de custo sem essa busca, com semântica diferente.
- H2 altera um valor de cada estado antes do restore. O frame 16 é a base e o 17
  mantém o payload, alterando o boundary/header/checksum. A cadeia de deltas é curta.
- H1/H2 contam visitas ao codec em um segundo mundo **fora** do tempo; a carga
  medida usa `Int32Policy`, sem instrumentação nos codecs.
- `NativeHost` usa memória nativa com execução no host. Registry/history/inputs
  são orquestração de host, inclusive quando o contexto se chama BurstCompiler.

## Memória e calibração

`reservedPayloadBytes` mede backing reservado; `livePayloadBytes` mede backing
referenciado pelos buffers/raízes, incluindo capacidade vazia. Não equivale a
`Count * sizeof(T)`. COW Fixed pré-aloca seu pool, portanto compartilhar raízes
pode reduzir backing referenciado sem reduzir o pool reservado.

`reservedArenaBytes` soma solicitações ao memory factory de registry/history/input.
`reservedNativeMetadataBytes` calcula o tamanho dos headers e tabelas nativos a
partir dos tipos/capacidades reais. Nenhum desses contadores inclui overhead do
alocador, objetos Managed ou safety handles; eles não medem RSS ou pico transitório.

O contador `GC.GetAllocatedBytesForCurrentThread` é calibrado com uma alocação
conhecida de 8192 bytes. Se não responde, valores de bytes ficam em **-1**
(indisponível), e essa série não é submetida ao Performance Testing.
O marcador `GC.Alloc` também é calibrado e filtrado para a thread atual. Seus
eventos são medidos em nove execuções adicionais, com o gravador habilitado
somente durante a ação. Essas execuções não integram os tempos. Eventos são
blocos do marcador, não bytes ou contagem garantida de objetos.
O relatório também registra nove execuções de uma ação vazia: esse controle
revelou eventos ocasionais do próprio mecanismo de gravação no Editor.
Não atribua eventos esporádicos de 0/1 exclusivamente à coleção. Use as
distribuições cruas e o controle ao avaliar pressão de GC.
O recorder permanece desabilitado durante as medições de CPU; seu estado de
habilitação original é restaurado no teardown da fixture. O relatório registra
essa condição para impedir a comparação de tempos com gravação ativa.

## Arquivos e reprodução

Por padrão, CSV e JSON vão para
`Temp/DeterministicBenchmarks/hotpath-opportunities.csv/.json` no projeto.
Para mudar o destino, configure no processo do Editor:

```csharp
Environment.SetEnvironmentVariable(
    "LEGENDARY_DETERMINISTIC_HOTPATH_BENCHMARK_OUTPUT",
    "D:/caminho/hotpath-opportunities.csv");
```

O JSON contém ambiente, calibração, amostras cruas, escopo e diagnósticos.
Valide o relatório completo no diretório raiz do pacote:

```powershell
python Tools/Deterministic/summarize_hotpath_benchmarks.py Tools/Deterministic/hotpath-opportunity-benchmark.json --expect-complete --verify-original-sources
```

Compare futuras implementações com o mesmo corpus, ambiente, checks e escopo,
mantendo o relatório atual como baseline. A fixture possui assembly próprio e
preserva os quatro arquivos de benchmark anteriores. Não há limite de tempo
absoluto que faça o teste falhar em outro hardware. Falhas de conteúdo, ordem,
status, snapshots ou execução Burst fazem o teste falhar.
