# Otimizações de bounds — 2026-10-12

As otimizações foram aplicadas ao código de produção. Os benchmarks anteriores permanecem em `Tools/FixedPoint/Reports/BoundsBenchmarks`; os novos resultados, comparação completa e validação estão em `Tools/FixedPoint/Reports/BoundsOptimizations`.

## Alterações

- Validação, comparação de coordenadas e cálculo de corners usam inteiros de 64 bits, normalizando zero negativo apenas para comparação. A validação de CenterExtents verifica as distâncias até os limites finitos com diferenças unsigned, antes de calcular endpoints. Todos os componentes continuam sendo verificados quanto a NaN/infinito antes de erros geométricos.
- Construção, getters derivados, setters e expansão usam `FixedBoundsInteger`, com magnitude de 128 bits, em lugar de intermediários de 512 bits. Seus intermediários geométricos precisam de no máximo 66 bits. A divisão calcula quociente e resto uma única vez. O sinal de zero, os cinco arredondamentos, as quatro políticas de overflow numérico e o arredondamento conservador dos endpoints são preservados.
- Distância acumula quadrados em 128 bits mais um contador de carry, preservando até 130 bits. O arredondamento usa o caminho de 128 bits quando possível e mantém o caminho largo para somas maiores. Frações de raios usam produtos cruzados exatos de 128 bits para diferenças de endpoints de até 64 bits; o comparador mantém fallback para numeradores maiores. O parâmetro do raio usa produto e divisão de 128 bits.
- Escrita de rede prepara os quatro/seis escalares diretamente em um único buffer de 60 bytes, eliminando os buffers e cópias intermediárias de vetores. A publicação copia bytes quando cursor e comprimento estão alinhados; campos parciais continuam usando cópia de bits. Canonicalização, validação conjunta da geometria, prioridades de erro e publicação atômica são preservadas. Descritores também usam a publicação por bytes quando alinhados.

A API pública, os formatos canônicos, a versão geométrica, os fingerprints e o armazenamento de `FixedBounds2`/`FixedBounds3` permanecem compatíveis. As variantes de cache de corners e granularidade de jobs continuam sendo experimentos das fixtures. A melhora do benchmark de transição em lote vem do núcleo geométrico usado pela transição existente.

## CPU: comparação com a linha de base

Mesmo host Windows x64, i9-13900KF, Unity 6000.3.20f1, Burst 1.8.29, safety checks ativos; três aquecimentos e nove medições. Os cinco arquivos de workload/harness são byte a byte iguais aos da linha de base. A tabela usa medianas da primeira rodada, preservada integralmente, em microssegundos por lote. Microkernels são 128 bounds × duas repetições; consultas repetidas são 256 bounds × duas repetições × oito consultas; rede são 64 bounds × duas repetições. Precisão: três casas, dimensão: 3D. Os microkernels usam configurações explícitas de representação; foram selecionados do processo global MinMax. A transição usa o modo global indicado e inclui um Schedule/Complete para 256 entidades.

| Operação Burst | MinMax antes → depois | Ganho | CenterExtents antes → depois | Ganho |
| --- | ---: | ---: | ---: | ---: |
| Validação | 6,4 → 4,3 | 1,49× | 38,0 → 3,0 | 12,67× |
| Cinco getters | 425,1 → 44,3 | 9,60× | 370,8 → 22,9 | 16,19× |
| Alteração do centro | 249,5 → 18,2 | 13,71× | 153,3 → 10,8 | 14,19× |
| Distância quadrada | 78,0 → 13,8 | 5,65× | 130,5 → 9,7 | 13,45× |
| Parâmetro do raio | 144,6 → 39,5 | 3,66× | 198,3 → 39,6 | 5,01× |
| Consultas repetidas | 192,5 → 58,0 | 3,32× | 1008,2 → 73,0 | 13,81× |
| Escrita FixedWidth sem perda, alinhada | 302,9 → 130,5 | 2,32× | 345,1 → 219,5 | 1,57× |
| Transição, um job para 256 entidades | 919,1 → 37,5 | 24,51× | 222,7 → 24,8 | 8,98× |

Foram pareados todos os **3.618 resultados**, incluindo .NET 8, Mono, Burst, as duas representações globais, precisões 3/18, dimensões 2D/3D, codecs, working sets, histórico e alocações frias. `comparison.csv` inclui medianas, p95, deltas de CPU, GC e payload de memória de cada par. Duas comparações com mediana zero foram excluídas do cálculo de ganho por estarem abaixo da resolução do timer.

Quatro cenários de produção Burst ficaram mais de 10% lentos na rodada inicial. Por isso, as duas execuções das fixtures de CPU foram repetidas, sem substituir a primeira rodada: **1.856 resultados adicionais**, retidos em `Repeat`. Os cenários são:

| Processo / cenário | Antes | Primeira rodada | Repetição |
| --- | ---: | ---: | ---: |
| CenterExtents: escrita BitPacked 2D, sem perda, offset 5 | 132,8 | 184,0 | 98,3 |
| MinMax: leitura BitPacked 2D, geometria CenterExtents, com perda | 55,4 | 73,9 | 43,3 |
| MinMax: leitura VarInt 3D, geometria MinMax, com perda | 36,4 | 46,1 | 36,8 |
| MinMax: escrita em buffer curto BitPacked 3D, offset 5 | 153,0 | 238,1 | 122,7 |

Essas lentidões superiores a 10% não se repetiram. A leitura VarInt permaneceu próxima da linha de base; no processo global CenterExtents, sua repetição mediu 41,0 µs contra 37,5 µs anteriores. Os dados mostram variação entre processos e rodadas; CPU é evidência descritiva local, sem limiar universal de aprovação ou conclusão de significância estatística. Não atribuímos os ganhos deste host ao ARM64.

## Memória

| Recurso | Antes | Depois |
| --- | ---: | ---: |
| Intermediário assinado nas mutações/getters | `FixedQ`: 68 bytes | `FixedBoundsInteger`: 24 bytes |
| Buffer de preparação de bounds | 60 bytes | 60 bytes |
| Buffer intermediário de vetor na composição da escrita | 30 bytes | eliminado |
| Bounds2 persistente | 32 bytes | 32 bytes |
| Bounds3 persistente | 48 bytes | 48 bytes |

O intermediário passou a ocupar 44 bytes a menos, redução de 64,7% nesse tipo temporário. Os tamanhos de buffers são declarações no código; não representam uma medição do pico da pilha gerada pelo compilador. Resident payload, cache adicional, bytes de rede e payload vivo de histórico ficaram idênticos nos 3.618 pares. Não houve nova alocação em hotpaths: zero bytes nas medições .NET disponíveis e zero eventos de GC.Alloc nas medições Unity calibradas. O contador de bytes Mono é indisponível e continua registrado como `-1`. RSS, overhead de objetos/alocadores e reserva real de pilha não são medidos por essas fixtures.

## Determinismo e regressão

Oito novos casos NUnit verificam limites finitos por BigInteger, precedência de erros, signed zero, arredondamento/overflow de ratios, carries acima de 128 bits, frações de largura completa/fallback e equivalência da escrita anterior por vetores com a nova composição em todos os offsets 0–15 e capacidades 0–70. Bytes de canários e cursores são comparados inclusive nas falhas.

- Núcleo managed: **44/44 em cada representação**; integração: **14/14 em cada representação**.
- Regressão managed: **633/633** no núcleo e **599/599** em coleções/runtime.
- Unity EditMode: **76/76 em cada representação**, incluindo Burst, conversões e integração com rollback/replay.
- Benchmarks: **11/11** núcleo e **7/7** runtime por modo em .NET; **18/18** por modo em Unity; repetição de CPU **7/7** por modo.
- Players Mono e IL2CPP x64: **quatro execuções aprovadas**, com marcadores de execução Burst, 5.760 casos originais e 960 sequências × 64 passos, três tamanhos/ordens de lote e **11.796.480 palavras comparadas por execução**.
- Checksums preservados: corpus original `14973702704214226896`, sequências `11524040837634330506`.
- APK Android recompilado com bibliotecas Burst/IL2CPP ELF64 AArch64 verificadas. **Execução ARM64 pendente**, conforme aceite anterior do usuário.
- Gerador de API e sincronização dos corpora passam em `--check`.

Uma verificação adicional do consumidor Spatial passou **20/20** tanto com os kernels anteriores quanto com os otimizados, no mesmo snapshot dos demais fontes. A primeira execução fora desse snapshot encontrou quatro erros de configuração na serialização Spatial enquanto fontes compartilhados estavam mudando; a repetição congelada passou nos dois lados. Esses XML são mantidos separadamente da validação principal.

`Validation/summary.json` exige os dois corpora, modos congelados, comparações diretas Burst, fontes bounds idênticos ao projeto isolado, os quatro relatórios de player e arquitetura das bibliotecas do APK. Inclui fingerprints SHA256 dos fontes e dependências. `comparison.json` registra hashes dos CSVs, condições por execução, alterações de fontes e a repetição dos cenários mais lentos.

Após a coleta, alterações paralelas em `FixedBigUInt`, `FixedVectorKernel` e `FixedQuaternionNetworkKernel` foram incorporadas ao projeto de validação. A verificação final dessas dependências está em `Validation/Current`: 44 testes managed e 76 Unity por representação, quatro execuções de player x64 e novo APK ARM64. As medições da tabela continuam vinculadas ao snapshot anterior, preservado integralmente em `Temp/BoundsOptimizationArtifacts/FrozenBenchmarkSources`, com hashes já registrados; não atribuem às alterações paralelas os ganhos desta otimização.

## Reprodução

Use `prepare_bounds_validation.py` para um projeto Unity isolado. Execute os mesmos comandos de `BOUNDS_BENCHMARKS.md`, usando `LEGENDARY_BOUNDS_BENCHMARK_DIR` apontando para um diretório novo; rode os dois modos em processos separados e mantenha execuções de benchmarks sequenciais. Para consolidar e comparar:

```text
python Tools/FixedPoint/summarize_bounds_benchmarks.py <artefatos> <projeto> Tools/FixedPoint/Reports/BoundsOptimizations --functional-count 76
python Tools/FixedPoint/compare_bounds_benchmarks.py Tools/FixedPoint/Reports/BoundsBenchmarks Tools/FixedPoint/Reports/BoundsOptimizations --repeat <artefatos-de-repeticao>
python Tools/FixedPoint/summarize_bounds_validation.py <artefatos-de-validacao> <projeto> Tools/FixedPoint/Reports/BoundsOptimizations/Validation
```

Os relatórios anteriores não são sobrescritos. Os resultados de cache, SoA, políticas de histórico e granularidade de jobs da linha de base continuam disponíveis para orientar integrações específicas; o núcleo otimizado não acrescenta cache por entidade nem altera a reserva de histórico.
