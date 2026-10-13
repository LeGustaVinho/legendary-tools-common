# Benchmarks H8–H13: caminhos ainda sem medição

`ExtendedHotpathBenchmarks` acrescenta **120 cenários** à mesma assembly de testes de performance. Os arquivos e resultados dos benchmarks anteriores permanecem preservados. A categoria nova é `DeterministicExtendedHotpathBenchmark`; a fixture é `Explicit` e deve ser selecionada intencionalmente.

| Grupo | Operação medida | Variações | Casos |
|---|---|---|---:|
| H8 | `EventLedger.TryPreparePublish` | 256/2.048 eventos; ordenados/inversos/embaralhados; payload 4/64 bytes; Managed/NativeHost | 24 |
| H9 | busca de 32 inputs no fim do replay, busca de 32 checkpoints, importação, rejeição de checksum final | 256/8.192 ticks; checkpoint a cada 64 ticks; 1 jogador; Managed/NativeHost | 16 |
| H10 | `MultiMap.RemoveAll` e remoção por valor no fim da cadeia | 256/4.096 pares; 8 chaves; pares agrupados/intercalados; Full/COW; Managed/Burst | 32 |
| H11 | `SlotMap.Clear` depois de fragmentação | 1.024/65.536 slots já usados; todos ativos/apenas 64 ativos; Full/COW; Managed/Burst | 16 |
| H12 | validação, publicação e reconstrução dos buckets na importação do dicionário | 256/2.048 entradas; hash normal/constante; Full/COW; Managed/Burst | 16 |
| H13 | remoção de um nó de alto grau | 256/2.048 nós; estrela dirigida/arestas paralelas e self edges não dirigidas; Full/COW; Managed/Burst | 16 |

São 60 casos Managed, 20 NativeHost e 40 BurstJob. `NativeHost` executa métodos managed sobre arenas nativas. `BurstJob` executa `IJob` compilado sincronicamente e exige o marcador `BurstDiscard` igual a zero em **cada** execução.

## Medição e validação

- 8 aquecimentos, 31 amostras de CPU e 9 execuções separadas para observar `GC.Alloc`.
- Cronometra somente a operação. Burst inclui `Schedule + Complete`; emissão, criação, restauração, validação e descarte ficam fora do tempo.
- As mutações começam no mesmo snapshot a cada amostra. Buffers são Fixed, com página de 4.096 bytes e um snapshot retido.
- H8 verifica IDs ordenados, todos os campos dos payloads e entrega/retração. O embaralhamento usa semente fixa.
- H9 verifica valores, hashes e offsets esperados; a reexportação após importação é comparada byte a byte. A rejeição deve preservar um arquivo anterior diferente e o cursor zero.
- H10/H11 verificam pares/handles, ordem, conteúdo restante e snapshots. H12 verifica ordem, lookup pelos buckets e snapshot com valores diferentes. H13 verifica todos os nós/arestas retidos e o desligamento das incidências.
- Não há limite de tempo absoluto que dependa do hardware. Asserções de conteúdo e Burst são obrigatórias.

O relatório conserva todas as amostras, mediana, mínimo, média, p95 e ns por item. `reservedPayloadBytes`/`livePayloadBytes` são os contadores do storage; `payloadGrowthBytes` é o payload adicional referenciado após a operação com snapshot retido. Não equivale a uma nova reserva no allocator: o pool Fixed já pode estar reservado. Os contadores excluem headers de objetos, overhead dos allocators e safety handles; não são RSS.

H8 mede a arena do ledger. H9 registra a arena agregada dos dois arquivos de replay e do registry; `scratchBytes` informa os dois buffers externos de exportação/validação. H12 registra scratch efetivamente consumido; H13 registra workspace do owner. A estimativa de metadata nativa de H13 cobre os três storages da coleção e exclui metadata do workspace interno. Os campos opcionais de contagem de chamadas de codec não são usados nesta matriz.

O contador de bytes de GC é calibrado com uma alocação positiva conhecida. Se estiver indisponível, retorna `-1`; eventos de `GC.Alloc` não são bytes. O recorder fica desligado durante as amostras de tempo, usa controle de ação vazia e volta ao estado original ao terminar. GC observado é apenas o da thread chamadora.

## Reexecutar

No Test Runner, selecione a fixture `LegendaryTools.Deterministic.Tests.HotpathBenchmarks.ExtendedHotpathBenchmarks` em EditMode e inclua testes explícitos. Execute somente essa fixture, sem outros trabalhos de CPU em paralelo. Pela ferramenta Unity `run_tests`, use:

```json
{
  "mode": "editor",
  "filter_type": "testName",
  "filter": "ExtendedHotpathBenchmarks",
  "include_explicit": true,
  "async_tests": true,
  "timeout": 600
}
```

Para escolher a saída, defina no processo do Editor `LEGENDARY_DETERMINISTIC_EXTENDED_BENCHMARK_OUTPUT` como caminho absoluto terminado em `.csv`. A fixture cria CSV e JSON. Sem a variável, usa `Temp/DeterministicBenchmarks/extended-hotpath-opportunities.csv` do projeto Unity.

Baseline e análise: `Tools/Deterministic/extended-hotpath-opportunity-benchmark.{json,csv}` e `Tools/Deterministic/EXTENDED_HOTPATH_OPPORTUNITIES.md`, a partir da raiz deste pacote. O JSON de testes mantém os resultados do Test Runner.

Valide o relatório a partir da raiz do pacote:

```powershell
python -B Tools/Deterministic/validate_extended_hotpath_benchmarks.py Tools/Deterministic/extended-hotpath-opportunity-benchmark.json --tests Tools/Deterministic/extended-hotpath-opportunity-test-results.json --before Tools/Deterministic/extended-hotpath-before-sources.json --sources Tools/Deterministic/extended-hotpath-benchmark-sources.json --output Tools/Deterministic/extended-hotpath-validation.json
```

O verificador exige a matriz completa, confere as estatísticas contra as amostras brutas, a calibração de GC, a contabilidade do payload, CSV/JSON, todos os resultados do NUnit e SHA-256 das fontes. Em uma futura otimização, mantenha a matriz e as fontes dos benchmarks iguais; registre separadamente a mudança das fontes de produção para não apresentar um workload alterado como ganho de desempenho.
