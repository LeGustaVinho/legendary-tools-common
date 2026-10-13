# Validação de Bounds/AABB

Ampliação e revalidação em 12/10/2026. Unity 6000.3.20f1, Burst 1.8.29, Collections 2.6.6; standalone .NET 8 em Windows x64. O projeto isolado `Temp/BoundsRevalidation-20261012` usa os mesmos fontes de Bounds e testes da biblioteca. As dependências compartilhadas foram sincronizadas antes da última execução e dos builds.

Foram acrescentados **37 casos NUnit de Bounds**, contando parametrizações: a suíte Unity passou de 31 para 68 casos. A implementação geométrica manteve o checksum original; a ampliação não encontrou divergências nos ambientes executados.

## Evidência executada

| Verificação | Resultado |
|---|---|
| Standalone Bounds, MinMax | 36 testes, todos passaram |
| Standalone Bounds, CenterExtents, processo separado | 36 testes, todos passaram |
| Integração Managed: codecs, ECS, rollback, lockstep, replay/seek, falhas e eventos | 14 testes por modo, todos passaram |
| Unity EditMode: geometria, rede, converters, Native/Burst e integração Managed/Native | 68 testes por modo, todos passaram; nenhum ignorado |
| Regressão standalone FixedPoint completa | 609 testes, todos passaram |
| Regressão standalone coleções/runtime completa | 585 testes, todos passaram |
| Player Windows x64 Mono, MinMax e CenterExtents | Ambos passaram; corpora original e de sequências, com três agendamentos Burst |
| Player Windows x64 IL2CPP, MinMax e CenterExtents | Ambos passaram; corpora original e de sequências, com três agendamentos Burst |
| Android ARM64 IL2CPP + Burst | APK recompilado com os testes de sequências; **execução em dispositivo pendente** |

O usuário aceitou concluir com a execução ARM64 pendente. A consulta ADB não encontrou dispositivos. A compilação ARM64 foi verificada por bibliotecas ELF64 AArch64 no APK, exclusivamente em `lib/arm64-v8a/`, incluindo `lib_burst_generated.so` e `libil2cpp.so`. A evidência de paridade executada cobre Windows x64; ARM64 ainda requer execução dos dois modos.

## Corpus e cobertura

- Corpus original versão 1: 5.760 casos; checksum preservado **14973702704214226896** em .NET, Mono, IL2CPP e Burst x64.
- Corpus de sequências versão 1: **960 configurações × 64 passos = 61.440 observações**, com restauração do checkpoint após 32 passos e repetição dos 32 passos finais. Checksum **11524040837634330506** em todos os ambientes executados.
- Ambos cobrem dimensões 2/3, MinMax/CenterExtents, precisões 0/3/9/18, cinco arredondamentos, quatro políticas de overflow e Auto/Portable/Intrinsic. O gerador usa operações inteiras e uma semente fixa.
- O corpus de sequências compara diretamente armazenamento raw, resultados, statuses, cursores e bytes completos dos três codecs em cada passo. Cada player verifica **11.796.480 palavras** em três agendamentos: ordem normal, inversa e permutação bijetiva; lotes 1/17/64. O marcador `BurstDiscard` exige execução nativa. O checksum complementa essas comparações e os oráculos independentes.
- Oráculos com BigInteger verificam **46.080 mutações**, **23.040 casos de distância/raycast** e **11.520 quantizações**. Incluem diferenças de 65 bits, somas de quadrados maiores que 128 bits, todas as políticas de overflow e arredondamento, envelopes conservadores, deslocamentos e permutações de eixos.
- Rede: **12.288 payloads adversariais** com sementes fixas, varints não canônicos/excessivos, especiais no último campo, truncamentos, descritor inválido no segundo vetor, cursores negativos/extremos, offsets não nulos, faixas e grades independentes. Falhas preservam cursor e todas as sentinelas do destino; leituras válidas reencodificam os mesmos bits consumidos.
- Integração Managed/Native: 96 ticks com jitter, reordenação e duplicação, incluindo reutilização de um histórico de 64 ticks. Cada checkpoint confirmado coincide byte a byte e por hash com lockstep; os eventos confirmados coincidem em identidade, ordem e payload. Falhas depois de escrever Bounds são repetidas três vezes, restaurando estado e abandonando eventos antes da tentativa bem-sucedida. Todas as três políticas de snapshot são exercitadas.
- Replay/seek, ciclos de captura/restauração ECS, remoção/reutilização de geração e atualizações futuras mantêm estado reproduzível. As 64 combinações de ±0 em Bounds3 e as combinações de Bounds2 preservam raws nos codecs, igualdade, comparação e hash numéricos.
- Layouts confirmados com Marshal/UnsafeUtility: Bounds2=32 bytes, Bounds3=48 bytes; codecs canônicos suportam CreateMeasure.

Esses resultados validam a paridade nos ambientes e nas entradas descritos. O conjunto combina oráculos independentes, propriedades geométricas, comparação direta e regressão versionada; não constitui uma prova formal para todo input possível ou uma execução ARM64.

## Alocações e custo

Após aquecimento, 10.000 iterações de um lote que reúne Contains, Intersects, ClosestPoint, SqrDistance, raycast, mutadores sem alteração e codecs por ponteiro alocaram **zero bytes de GC**, nos dois modos.

O teste registra ticks e frequência nos XML para reprodução. As medições locais dependem da carga da máquina e do backend; o lote reúne várias operações e codecs.

## Reprodução

As suítes, geradores e comandos de player estão em `BOUNDS.md`. Execute fachadas globais em processos separados por modo; não resete SharedStatic/FixedRuntime durante a simulação. A matriz dos kernels usa estados explícitos e não altera os globals.

Relatórios desta revalidação ficam em `Temp/FixedBoundsRevalidationArtifacts`: XML standalone e JSON/logs de quatro execuções de player x64. Os XML/logs do Unity e builds ficam em `Temp/BoundsRevalidation-20261012`. O verificador `Tools/FixedPoint/summarize_bounds_validation.py` preserva os resultados, SHA256 dos fontes Bounds/testes, fontes completos do projeto isolado, dependências UPM e SHA256 do APK em **`Tools/FixedPoint/Reports/BoundsRevalidation/summary.json`**. A evidência da implementação inicial permanece em `Tools/FixedPoint/Reports/Bounds`.

Para completar ARM64, instalar `Temp/BoundsRevalidation-20261012/Builds/FixedBoundsValidation/Android/FixedBoundsValidation.apk`, iniciar o package `com.legendarytools.fixedbounds.validation`, colher `fixed-bounds-validation.json` e repetir em um novo processo com o extra Intent `bounds-mode=center-extents`. Conferir Passed, Architecture, Backend, NativeCases, os dois checksums, StressSchedules=3, StressReplayedSteps=30720 e StressComparedWords=11796480.
