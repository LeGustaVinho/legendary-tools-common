# Bounds: oportunidades de CPU e memória

Este documento preserva a análise e as medições anteriores à otimização. A implementação e a comparação posterior estão em [BOUNDS_OPTIMIZATIONS.md](BOUNDS_OPTIMIZATIONS.md).

Medições de 12/10/2026, Windows x64, Intel i9-13900KF, .NET 8.0.29 e Unity 6000.3.20f1/Burst 1.8.29. A análise foi limitada aos Bounds/AABB e à sua integração determinística. Foram adicionadas três fixtures explícitas, com **18 casos NUnit por execução e 3.618 linhas de medições** nos dois modos. O benchmark anterior reunia várias operações em um único lote de 10.000 iterações; ele não atribuía custo a cada operação, distribuição de entradas ou tamanho do estado.

## Oportunidades identificadas e cobertas

| ID | Custo no código atual | Benchmark novo e direção de otimização |
|---|---|---|
| B1 | `Validate` decodifica ambos os vetores; CenterExtents verifica `center ± extents` com temporários largos. Cada consulta repete essas etapas. | Validate, Contains interior/rejeição no primeiro ou último eixo, Intersects, getter direto, cinco views e ClosestPoint, separados por dimensão, representação e precisão. Avaliar validação/decode compartilhados em uma operação de lote. |
| B2 | `SqrDistance` e frações de `IntersectRay` usam `FixedQ`/`FixedBigUInt` de 512 bits. Comparações de frações multiplicam temporários largos; distância divide e calcula resto. | Distância normal/extrema, raycast booleano/com parâmetro, rejeição no último eixo e direção de um raw. Avaliar aritmética de 64/128 bits com fallback para os limites e uma divisão que produza quociente/resto juntos. Preservar arredondamento, signed zero e overflow. |
| B3 | Setters validam o original, calculam a view atual/cantos, constroem e validam o candidato. Mutações alteradas custam mais que as operações sem alteração do benchmark anterior. | Construção MinMax, mudança/self-assignment de center, Expand par/ímpar/rejeitado e Encapsulate alterado. Avaliar reutilização de cantos/validação do original dentro da operação, mantendo commit atômico. |
| B4 | Muitas consultas sobre a mesma caixa reconstruem os mesmos cantos. | 1/8/32 consultas por caixa: API atual, cantos preparados e construção do cache seguida das consultas. O protótipo só aceita dados finitos preparados e uma caixa imutável durante o lote; um futuro cache precisa invalidar quando a caixa/configuração mudar. |
| B5 | Write valida/canonicaliza o perfil e a geometria, depois passa por dois writers de vetor. Há staging do Bounds (60 bytes), staging de vetor (30 bytes por chamada) e cópias por bits em várias etapas. | Write/Read/Canonicalize, três codecs, lossless/lossy, offset 0/5 bits, falha de capacidade, payload inválido completo e descriptors. Protótipos: um único staging com scalars e cópia por bytes quando ambos os cursores/tamanhos estão alinhados. |
| B6 | Bounds2/3 ocupam 32/48 bytes por entidade. Cópia de structs, validação e acesso aos campos podem dominar varreduras grandes. | Arrays reais com 64/4.096/65.536 entidades, ordem sequencial/permutada, AoS/API e cantos em SoA preparados. A comparação reúne diferenças de validação, preparação e layout; não isola um ganho exclusivamente de SoA. |
| B7 | Construção e zeragem de arrays grandes fazem alocação e podem envolver LOH/allocator nativo. | Alocação fria de arrays Managed e NativeArray Persistent, nas duas dimensões e três tamanhos. Disposição/liberação fica fora do tempo. Registrar payload e GC em vez de apenas a ausência de GC nos kernels aquecidos. |
| B8 | Codecs de Bounds validam geometria mesmo ao usar `DeterministicWriter.CreateMeasure`; sizing seguido de write pode repetir esse custo. | Tamanho constante, writer de medida validado, escrita e leitura canônica de 128/4.096 Bounds3, Managed/Burst. Avaliar sizing fixo por schema e validação compartilhada em serializações de lote. A validação necessária à escrita continua obrigatória. |
| B9 | Históricos reservam arenas para o pior caso. Deltas/páginas reduzem bytes vivos, mas exigem hash, comparação e reconstrução. | 32 frames de 128/4.096 Bounds3; alterações agrupadas de 0/1/100%; FullPerTick, KeyframesAndDeltas, CopyOnWrite; páginas 256/4.096; restauração em três idades. Medir CPU, arena reservada e payload vivo separadamente. |
| B10 | `NativeBoundsSimulation.TryStep` agenda e completa um job para uma célula. Muitos componentes podem repetir sincronização/validação. | 1/256/2.048 entidades: Managed direto, job vazio de controle, um job por entidade e um job com o lote inteiro. Mesmas transições e estado final, com marcador que exige Burst nativo. |

As oportunidades B1/B2/B3/B8 são hipóteses de implementação apoiadas na separação de custos. B4/B5/B6/B10 têm alternativas de benchmark para avaliar uma mudança antes de incorporá-la à biblioteca. A API e os kernels de produção não foram alterados nesta etapa.

## Experimentos locais

Medianas em Burst, com safety checks habilitados. As comparações de B4 usam 256 caixas 3D, duas repetições e oito consultas por caixa; o candidato inclui a construção das duas views em cada repetição. B5 usa 64 caixas 3D, duas repetições e FixedWidth lossless alinhado. B10 aplica uma transição por entidade a 256 entidades.

| Experimento | Baseline | Alternativa | Relação observada |
|---|---:|---:|---:|
| B4 MinMax: consultas repetidas → construir cantos e consultar | 192,5 µs | 34,8 µs | 5,53× |
| B4 CenterExtents | 1.008,2 µs | 242,7 µs | 4,15× |
| B5 MinMax: staging atual → um staging e cópia por bytes | 302,9 µs | 126,0 µs | 2,40× |
| B5 CenterExtents | 345,1 µs | 151,1 µs | 2,28× |
| B10 MinMax: 256 jobs → um lote | 1.537,2 µs | 919,1 µs | 1,67× |
| B10 CenterExtents | 850,8 µs | 222,7 µs | 3,82× |

O cache B4 acrescentaria **32 bytes por Bounds2 / 48 bytes por Bounds3** se os cantos fossem guardados além do valor original. Para 65.536 Bounds3, isso representa mais 3 MiB de payload. O custo é amortizado pelo número de consultas e precisa ser confrontado com a frequência de mutação.

Para 4.096 Bounds3 × 32 frames sem alterações, o histórico FullPerTick reteve **6 MiB** de payload e CopyOnWrite **192 KiB**. As arenas reservadas continuaram em aproximadamente **6,19 MiB e 6,39 MiB**, respectivamente. Com 1% de mudanças agrupadas, CopyOnWrite reteve 260.096 bytes em páginas de 256 e 323.584 bytes em páginas de 4.096; com 100% de mudanças ambos retiveram 6 MiB. A escolha da política/página envolve CPU, reserva e densidade/localidade das alterações.

Os protótipos B5 comparam resultado, status, cursor e **todos os 300 bytes**, incluindo sentinelas, para buffers com capacidade de 0 a 60 e offsets negociados, antes de medir. B4/B6 comparam os resultados das consultas; B8/B10 comparam estado/digests/status; B9 restaura e compara byte a byte os 32 frames. Jobs/function pointers usam `BurstDiscard` para rejeitar fallback Managed.

## Método e limites de leitura

- Três aquecimentos, nove amostras cruas de tempo e mediana/p95/ns por item. Preparação, verificações, escrita do relatório e descarte ficam fora do intervalo medido. As operações de alocação fria incluem construção/zeragem.
- B1–B6 usam batches e function pointers com uma transição nativa por batch; incluem loop, dispatch e digest. Existe controle de loop. B8/B10 incluem Schedule+Complete explicitamente. B9 é execução no host com memória Managed ou NativeHost, sem afirmar execução Burst do histórico.
- .NET calibrou o contador de bytes do thread e registrou **zero bytes de GC nas operações aquecidas**. O contador de bytes do Mono não passou na calibração: seus valores estão como **-1**. No Unity, três execuções adicionais fora do tempo usam `GC.Alloc`, filtrado para o thread chamador; a calibração registrou uma alocação, o controle vazio 0/0/0 e as operações aquecidas **zero eventos**. O recorder fica desabilitado nos tempos e seu estado é restaurado.
- Payload/reserva são contagens lógicas; não incluem cabeçalhos de objetos, allocator, safety handles ou RSS. `resident_payload_bytes` representa os dados base do cenário, `extra_payload_bytes` a arena/cache estimado, e `live_payload_bytes` os bytes de registros retidos ou páginas vivas. Os buffers temporários relatados são tamanhos declarados, não uma medida do pico real da pilha.
- Uma amostra de alocação pequena (64 Bounds2 Managed) teve mediana abaixo da resolução do timer e está identificada em `summary.json`; esse valor não sustenta uma comparação de CPU. CPU não tem limite absoluto de aprovação dependente de hardware. Os resultados são locais de Editor/.NET x64; builds de jogador e ARM64 exigem medições próprias.

## Arquivos e reprodução

- `Tests/FixedPoint/FixedBoundsCpuBenchmarks.cs`: B1–B5 e footprints.
- `Tests/FixedPoint/FixedBoundsMemoryBenchmarks.cs`: B6–B7.
- `Tests/Deterministic/Integration/FixedBoundsRuntimeBenchmarks.cs`: B8–B10.
- `Tests/FixedPoint/FixedBoundsBenchmarkWorkload.cs`: entradas determinísticas e protótipos usados apenas nos testes.
- `Tests/FixedPoint/FixedBoundsBenchmarkSupport.cs`: recorder/calibração/CSV/ambiente.
- `Tools/FixedPoint/Reports/BoundsBenchmarks`: 12 CSV, amostras individuais, ambiente, sete XML, comparações e SHA256 dos fontes/snapshot.

As fixtures têm `[Explicit]` e a categoria `BoundsBenchmark`. Selecioná-las pelo nome executa os benchmarks sem incluí-los automaticamente nas regressões funcionais. Coloque outputs fora de Assets.

```powershell
$env:LEGENDARY_BOUNDS_BENCHMARK_DIR = 'D:/.../Temp/BoundsBenchmarkArtifacts'
dotnet run --project Standalone~/FixedPoint.Tests/LegendaryTools.FixedPoint.Tests.csproj -c Release --artifacts-path 'D:/.../Temp/BoundsBenchmarkArtifacts' -- --bounds-mode=min-max --test=LegendaryTools.FixedPoint.Tests.Benchmarks.BoundsCpuBenchmarks,LegendaryTools.FixedPoint.Tests.Benchmarks.BoundsMemoryBenchmarks --workers=0
dotnet run --project Standalone~/Deterministic.Validation/LegendaryTools.Deterministic.Validation.csproj -c Release --artifacts-path 'D:/.../Temp/BoundsBenchmarkArtifacts' -- --bounds-mode=min-max --test=LegendaryTools.Deterministic.Tests.Benchmarks.BoundsRuntimeBenchmarks --workers=0
```

Repita em processos novos com `--bounds-mode=center-extents`. No projeto isolado criado por `prepare_bounds_validation.py`, use:

```text
unity test <projeto> --editor-path E:/Unity/6000.3.20f1/Editor/Unity.exe --mode EditMode --filter "BoundsCpuBenchmarks;BoundsMemoryBenchmarks;BoundsRuntimeBenchmarks" --output <artefatos>/unity-min-max.xml -- -nographics --fixed-bounds-test-mode=min-max
```

Repita com CenterExtents em outro processo. `summarize_bounds_benchmarks.py <artefatos> <projeto>` exige todos os resultados, contagens, amostras, calibração e fontes correspondentes; preserva os dados e calcula as comparações. Os 18 casos passaram em cada execução .NET e Unity, e a regressão funcional adicional de Bounds passou com 68 casos. Priorize B4/B5/B10 conforme a carga real, e use B9 para escolher memória/CPU do histórico antes de mudar a reserva.
