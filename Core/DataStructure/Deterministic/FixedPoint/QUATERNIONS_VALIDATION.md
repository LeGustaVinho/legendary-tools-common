# Validação de FixedQuaternion

Revalidação das otimizações em 12/10/2026: **sete testes adicionais à rodada de 86; 93 testes Unity/Burst passaram**, sem falhas nem skips. A rodada anterior havia acrescentado 32 testes. Escopo: API de rotação, kernels inteiros, perfis de normalização, codecs/patches, storage, rollback/replay, Inspector e backends. Esta evidência não constitui prova de exatidão para todo input representável.

## Referências e determinismo

- 600 referências independentes com 160 dígitos (`mpmath==1.3.0`), verificadas em Accurate (até um raw) e Fast (até quatro raws): normalize, Hamilton, inverse, dot, rotação de vetor, angle, lerp/slerp, Euler, angle-axis, from-to, look rotation, extração Euler/eixo e rotate-towards.
- Normalização também comparada a um oráculo BigInteger de raízes e limites de midpoint, em todas as precisões 0–18 e políticas de arredondamento, incluindo valores finitos extremos e quantização degenerada. Produto Hamilton comparado a racionais inteiros independentes.
- Novos oráculos BigInteger verificam **41.040 operações** de inversão, produto Hamilton e rotação (fórmula independente com produtos vetoriais). Cobrem 19 precisões, cinco arredondamentos, quatro políticas de overflow e três backends, incluindo entradas minúsculas, valores finitos extremos, wrap e cancelamento antes do arredondamento. Status e componentes são comparados; o sinal dos zeros de cancelamento é verificado pelos testes de representação e paridade entre backends, enquanto o oráculo racional verifica seu valor numérico.
- Novos casos de Accurate/Fast verificam ângulos de uma unidade raw próximos de zero e de pi, nas precisões 6/9/18, evitando perda do complemento de ângulos quase paralelos.
- Corpus versão 1: **17.100 casos**, cobrindo 19 precisões, três backends aritméticos, dois perfis matemáticos, duas políticas de normalização, cinco arredondamentos e 15 operações. Compara cada componente raw, scalar/vector, status, cursor, descritor e os 40 bytes de payload.
- Novo corpus encadeado versão 1: **1.140 sequências × 32 passos = 36.480 passos**. Usa entradas variadas geradas por PRNG inteiro versionado, todas as precisões/arredondamentos/backends/perfis matemáticos/políticas de normalização e distribuição das quatro políticas de overflow. Mistura as 15 operações, seis codecs e feedback do estado recebido; registra quaternion acumulado, PRNG, hash e tick. Checkpoint raw + estado do PRNG reproduzem cada passo seguinte, incluindo falhas de domínio e quantização grossa.
- `[BurstDiscard]` exige marca managed=1 na referência e managed=0 no job. A suíte executou batches 1/17, sequências em ordem direta/invertida e API pública contra a configuração congelada real. Cada resultado é comparado campo a campo antes do checksum.
- As otimizações acrescentam oráculo BigInteger para divisão/resto de 0–512 bits, comparação com os dois caminhos anteriores, verificação exata das constantes de compactação em 128/256 bits e falhas de capacidade nas 19 precisões, cinco arredondamentos e dois perfis matemáticos. A entrada preparada é comparada à transição anterior, incluindo raws/status de entradas inválidas, entrada/saída na mesma variável, rollback e execução Burst nativa.

Checksum do corpus: `3988754440199593305`.

Checksum das sequências: `8229647155990362304`.

SHA-256 das referências: `b383f58ebdd4acb9952ece12227db05703715645ad7263ea2666473da6aa247d`.

SHA-256 do trace canônico (UTF-8, LF): `8dbab9bdc5e67f844034f63bef9241ceb8496870052412e57eeea65e28008fc9`.

SHA-256 do trace encadeado canônico: `0704867a399b9c692fdaa4f9fe860c74482032bb40ea4f992193add448c39db6`.

## Plataformas verificadas

| Ambiente | Evidência |
|---|---|
| .NET 8.0.29, Windows x64 | 50 testes portáveis e 29 de integração de quaternion passaram; codecs/patches e runtime também passaram |
| .NET 8.0.31, Linux ARM64 via emulação Docker | 50 testes portáveis de quaternion passaram; mesmos dois corpora/traces que x64 |
| Unity 6000.3.20f1, Burst 1.8.29, Editor x64 | 93 testes isolados passaram, sem skips: Burst nativo, API, codecs/patches, storage, 10.000 ticks, sessões e Inspector |
| Windows Player Mono + Burst nativo | 17.100 casos + 36.480 passos; API pública e políticas None/NormalizeResult passaram |
| Windows Player IL2CPP + Burst AOT | Mesmos dois corpora; API pública e políticas None/NormalizeResult passaram |
| Android ARM64 IL2CPP + Burst AOT | APK compilado; execução em dispositivo ARM64 pendente |

Os quatro ambientes (.NET x64, .NET ARM64 emulado, Mono e IL2CPP) coincidiram em todos os campos dos dois traces: **53.580 casos/passos por ambiente**, incluindo cada componente raw, status e byte de rede. Os resultados otimizados também coincidem campo a campo com os traces históricos. O comparador aceita as quebras de linha históricas de cada plataforma e calcula um hash canônico; exports novos usam LF. Divisão/resto, cópia alinhada e constantes exatas mudam o custo da execução; semântica numérica, algoritmo, codecs e corpora permanecem na versão 1.

Nenhum dispositivo apareceu em `adb devices`. Build AOT e execução .NET emulada **não substituem** a execução de IL2CPP/Burst em hardware ARM64. Não se afirma essa validação de runtime.

Regressões standalone do workspace: **637 testes FixedPoint** e **601 testes de coleções/runtime**, zero falhas. As rodadas gerais deixam de executar 18 e 12 casos Explicit, respectivamente, incluindo benchmarks executados separadamente. Os 50 testes portáveis e os 29 testes de integração de quaternion passaram sem skips. Totais gerais incluem outras funcionalidades que já existem no workspace.

## Estado, rede e Inspector

- Patches forward/reversíveis, tamanhos variáveis, fallback Full, bytes compactos, ID/hash de baseline errado, corrupção, truncamento e atomicidade foram exercitados.
- Novos testes de delta exercitam 768 transições nas seis codificações e modos forward/reversível, medida vs tamanho escrito, aplicação no próprio buffer, prefixo/sufixo preservados, ausência de mudanças, padding inválido e destino sobreposto ao pacote. Um pacote recebe todas as 440 mutações de um bit: falhas preservam buffer/cursor; mudanças válidas de modo/ID de destino mantêm o payload idêntico (esses metadados não são autenticados pelos hashes de payload).
- Sequências de 10.000 ticks verificam rollback contra lockstep nas três políticas de histórico; replay reload/seek reproduz estados e hashes. Mundos Native/Burst usam a mesma transição e completam jobs antes da publicação.
- Sessões Managed/Native verificam confirmação dos mesmos checkpoints com atraso, perda e duplicação.
- Persistência raw não normaliza valores durante importação, incluindo NaN, infinities, zeros com sinal e quaternions não unitários.
- Inspector inclui reescala, quarta componente, texto decimal com 18 casas, múltiplos objetos, Undo, assets e overrides de prefab; conversões Unity não normalizam.

As primeiras execuções no Editor compartilhado tiveram falhas de instrumentação por logs de timeout da Pipeline durante testes longos. A revalidação usa um projeto isolado sem Pipeline e passou os 93 testes, sem suprimir logs. `create_quaternion_test_project.py` reproduz esse projeto e `unity test <projeto> --mode EditMode --filter FixedQuaternion` executa a suíte.

## Custo e compressão

Após aquecimento, o teste de kernels e codecs por ponteiro mediu **zero alocação managed**. Uma medição histórica, anterior às otimizações, em .NET x64 Release registrou 68,7 ms/10.000 composições None, 133,5 ms/10.000 composições NormalizeResult e 467,4 ms/1.000 Slerps. São medições locais de uma amostra, não orçamento garantido nem comparação com Unity Quaternion. A comparação das otimizações usa as suítes com aquecimento e nove amostras descritas em [QUATERNIONS.md](QUATERNIONS.md).

Maior erro angular observado em 96 rotações × cinco arredondamentos, por precisão/preset:

| Casas decimais | 32 bits | 48 bits | 64 bits |
|---:|---:|---:|---:|
| 3 | 0,516° | 0,229° | 0,229° |
| 6 | 0,374° | 0,01335° | 0,000459° |
| 9 | 0,374° | 0,01349° | 0,000369° |
| 16/18 | 0,374° | 0,01349° | 0,000369° |

Esses erros incluem normalização e quantização para Fixed64 no decode. O número de bits não compensa uma escala decimal muito grossa; os valores medidos não são limites universais. Payloads compactos têm 4/6/8 bytes, exato FixedWidth tem 32; envelopes/metadata são adicionais. VarInt e patch nem sempre economizam: seus testes medem os bytes efetivos e o patch escolhe Full quando menor.

## Reprodução

As fontes portáveis são compartilhadas pelos testes e pelo assembly de validação; `sync_quaternion_replay_corpus.py` mantém as duas cópias idênticas. `generate_quaternion_reference.py` reproduz as referências sem carregar código Unity/runtime. `compare_quaternion_traces.py` verifica todos os campos dos 17.100 casos; `--cases 36480` verifica o corpus encadeado. Testes usam `FIXED_QUATERNION_TRACE` e `FIXED_QUATERNION_SEQUENCE_TRACE`; players exportam por `--fixed-quaternion-replay-trace` e `--fixed-quaternion-sequence-trace`.

Relatórios NUnit/players e hashes das fontes otimizadas estão em [QuaternionOptimizedValidation](../../../../Tools/FixedPoint/Reports/QuaternionOptimizedValidation/summary.json); a evidência anterior permanece em [QuaternionRevalidation](../../../../Tools/FixedPoint/Reports/QuaternionRevalidation/summary.json). `summarize_quaternion_validation.py` valida os quatro ambientes, confere todas as linhas/colunas dos traces, rejeita fontes de quaternion desatualizadas e grava esse resumo. Na rodada histórica, uma adição concorrente de `InternalsVisibleTo("LegendaryTools.Deterministic.Spatial")` foi registrada com ambos os hashes; as fontes de quaternion verificadas na rodada otimizada coincidem com o workspace. Mudanças concorrentes posteriores em WorldHistory/StateRegistry não integram esta rodada: os snapshots medidos nos benchmarks estão identificados em [QuaternionOptimizedBenchmarks](../../../../Tools/FixedPoint/Reports/QuaternionOptimizedBenchmarks/summary.json). Traces completos permanecem nos artifacts do projeto temporário; podem ser regenerados pelas mesmas fontes/versionamentos.

Para builds, use `create_validation_project.py` fora do pacote. Se um harness Android adicional precisar de JNI, execute `FixedValidationPackages.EnsureAndroidJniBatch` no Editor em batch, sem `-quit`; o helper usa UPM assíncrono e encerra após resolução. Depois execute `FixedValidationBuild.BuildAndroidBatch`. Os helpers não executam automaticamente nem entram em builds comuns.
