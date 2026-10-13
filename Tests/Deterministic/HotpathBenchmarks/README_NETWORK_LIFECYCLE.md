# Benchmarks de inputs, rede, comandos grandes e listas encadeadas

Fixture: `NetworkLifecycleHotpathBenchmarks`. Categoria: `DeterministicRuntimeHotpathBenchmark`.
São **160 casos explícitos**, sem thresholds de tempo dependentes da máquina.

| Grupo | Operação e dimensões | Casos | Execução |
|---|---|---:|---|
| H14 | Registrar jogador no início/fim de um roster com 16/63 jogadores; history 256/4.096; linhas circulares com passado e futuro preenchidos | 16 | Managed / NativeHost |
| H15 | Rewind de 1 tick ou history−1 ticks; esquecer inputs de todos os jogadores sem futuros ou com 32 ticks futuros; 1/64 jogadores; history 256/4.096 | 32 | Managed / NativeHost |
| H16 | Escrever, ler, rejeitar checksum final e rejeitar perfil; 16 envelopes; payload 64/4.096/65.536 B; cursor inicial 0/3 | 48 | Managed / BurstJob |
| H17 | Merge de 256/2.048 comandos; 4/64 produtores; payload 64/256 B; partições concatenadas em ordem ou chaves embaralhadas | 32 | Managed / NativeHost |
| H18 | Percorrer lista ou executar 64 pares remove-first/add-last; 256/2.048 ativos; slots densos ou universo 8× maior; Full/COW; um snapshot retido | 32 | Managed / BurstJob |

Cada caso tem oito aquecimentos, 31 amostras de tempo e nove execuções separadas para eventos de GC. Setup, replay dos inputs, restore, alocação, descarte, validação e escrita de relatórios ficam fora da região cronometrada. Não execute outra carga de CPU junto com a coleta.

`BurstJob` inclui `Schedule().Complete()` e verifica um marcador `BurstDiscard` em todos os aquecimentos, amostras e probes. `NativeHost` usa arena nativa com execução no host: as classes de runtime medidas em H14/H15/H17 não são jobs Burst.

## Reprodução

1. No Unity Test Runner, execute a fixture explicitamente em EditMode. Com Unity MCP, use `run_tests` com `filter: "NetworkLifecycleHotpathBenchmarks"`, `filter_type: "testName"`, `include_explicit: true`, `mode: "editor"` e `timeout: 900`.
2. A saída padrão é `Temp/DeterministicBenchmarks/runtime-hotpath-opportunities.csv` e o JSON correspondente. Para outra saída, configure `LEGENDARY_DETERMINISTIC_RUNTIME_BENCHMARK_OUTPUT` **dentro do processo do Editor** antes da execução; uma variável definida em um terminal externo não muda o ambiente de um Editor já aberto. O caminho precisa terminar em `.csv`.
3. Preserve os relatórios completos. Para repetir os probes investigados de GC, filtre `H15_InputInvalidation_NativeHost_History256`, usando outro caminho de saída; são oito casos.
4. O baseline versionável fica em `Tools/Deterministic/runtime-hotpath-opportunity-benchmark.json/.csv`, e o resultado retornado pelo Unity MCP em `runtime-hotpath-opportunity-test-results.json`. O repeat fica em `runtime-hotpath-gc-probe-repeat.*`. Restaure a variável de ambiente após terminar.
5. Para auditar os artefatos salvos, a partir da raiz do pacote:

```powershell
python -B Tools/Deterministic/validate_runtime_hotpath_benchmarks.py
```

O verificador compara os 160 nomes com a matriz esperada, as estatísticas com as amostras brutas, CSV com JSON, resultados do Test Runner, hashes das fontes, diagnósticos de memória, GC e os digests de 80 pares entre backends. Ao renovar os artefatos de uma implementação, salve também o manifesto SHA-256 da fixture usada. Mudanças nas ações cronometradas exigem um novo baseline; não misture resultados parciais com a matriz completa.

## Verificações de conteúdo

- H14 confere a ordem dos IDs, inputs reais de todas as linhas preenchidas do passado/futuro, participação historicamente preservada e predição neutra do jogador que entrou. Cada setup constrói uma história equivalente; somente a admissão é cronometrada.
- H15 confere todos os inputs reais retidos, `NextTick`, `OldestTick`, `CompleteThrough`, a exigência de resolver novamente após rewind e a aceitação de valores diferentes depois de esquecer o futuro. O setup do rewind reproduz os ticks pelo API público; o setup de forget repõe os futuros.
- H16 monta um oracle com campos little-endian explícitos, compara todos os bytes e guards, verifica todos os campos do header e o endereço do payload emprestado. Falhas preservam o cursor inicial e publicam header/payload vazios. A checksum usa XXH64, já coberto pelo corpus anterior de vetores de hash.
- H17 constrói o oracle por rank final independente do comparator de produção. Confere todos os campos dos headers e todos os bytes dos payloads, além da contagem do merge. A mesma ordem é exigida para todos os produtores e backends.
- H18 cria uma free list com permutação de semente fixa e confere os dois sentidos da cadeia, valores, handles, gerações, FIFO, invalidação dos handles removidos e os links/valores de todos os nós do snapshot retido. Restore prepara cada amostra a partir da mesma raiz.

Toda execução deve produzir o mesmo digest dentro do caso; o verificador externo exige igualdade entre os dois backends. As comparações exatas dos oracles também são obrigatórias, além dos digests.

## Memória e escopo dos contadores

`reservedArenaBytes` é a soma dos pedidos ao memory factory da história ou command buffer atualmente vivo. O factory é reiniciado com a história em H14; não soma todas as reconstruções do setup. Em H17, `scratchBytes` identifica a arena de saída do merge, que já está incluída em `reservedArenaBytes`; não some esses valores.

`reservedPayloadBytes` e `livePayloadBytes` vêm do storage da lista. O valor vivo inclui capacidade referenciada por estado atual e snapshot, não apenas os elementos ativos. `payloadGrowthBytes` é a diferença para o baseline com o snapshot retido. COW reserva o pool antes da operação; growth mostra páginas adicionais referenciadas, sem implicar novas alocações de heap naquele momento.

`externalBufferBytes` conta arrays de conteúdo/scratch/output da fixture; exclui objetos, delegates e alguns temporários do setup. H16 pertence a uma API que recebe buffers do chamador e não possui arena própria. `reservedNativeMetadataBytes` estima as tabelas acessíveis do storage nativo da lista; headers de objetos, allocators, safety e RSS não estão incluídos.

`historyRows` descreve o domínio físico da história. `scanRows` e `touchedItems` são diagnósticos derivados da carga/implementação de referência, não contadores instrumentados de acesso à memória. Em H17, `bytesPerSample` representa o lote físico de comandos; os movimentos adicionais do heapsort não são contados como tráfego de memória medido.

Bytes de GC só são publicados quando a alocação positiva de calibração é detectada. Caso contrário são `-1`. Eventos `GC.Alloc` usam controle positivo e nove ações vazias na thread chamadora; o recorder fica desligado durante as amostras de tempo e volta ao estado original no teardown. Eventos esporádicos precisam ser preservados e investigados; ausência de eventos não demonstra zero bytes quando o contador de bytes está indisponível.

Os resultados caracterizam este corpus no Editor. O plano de otimizações e os limites da medição estão em `Tools/Deterministic/RUNTIME_HOTPATH_OPPORTUNITIES.md`.
