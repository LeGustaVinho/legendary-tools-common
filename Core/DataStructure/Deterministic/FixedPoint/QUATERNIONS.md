# FixedQuaternion

`FixedQuaternion` é uma struct mutável unmanaged de 32 bytes, com quatro componentes `Fixed64` (`x,y,z,w`). O núcleo, incluindo trigonometria, usa somente operações inteiras compartilhadas entre managed, Burst e .NET Standard 2.1. UnityEngine, formatação e persistência no Inspector ficam nas camadas correspondentes.

## Configuração e composição

```csharp
FixedRuntime.Initialize(new FixedSettings(decimalPlaces64: 6,
    quaternionNormalization: FixedQuaternionNormalizationMode.NormalizeResult));

FixedQuaternion rotation = FixedQuaternion.Euler(10, 20, 30);
FixedQuaternion step = FixedQuaternion.AngleAxis(5, FixedVector3.up);
rotation = rotation * step;
FixedVector3 direction = rotation * FixedVector3.forward;
```

Inicialize antes de threads/jobs. `QuaternionNormalization` pertence à configuração congelada: `None` (padrão) faz produto de Hamilton com uma quantização final; `NormalizeResult` normaliza o produto largo antes dessa quantização. Alterar o modo depois da inicialização falha com `ConfigurationFrozen`. Construtores, `Set`, `FromRaw` e codecs exatos não normalizam implicitamente.

`QuaternionFingerprint` inclui configuração numérica, algoritmo de quaternion e política de composição. Os fingerprints/descriptors escalares existentes permanecem compatíveis. Mundos que usam rotações devem empregar `FixedDeterministicProfile.TryQuaternionFingerprint` na assembly opcional de adapters.

`default(FixedQuaternion)` é zero. Use `identity` para inicializar um componente de rotação. Normalize de zero retorna identidade; quantização que zera uma rotação não zero retorna NaN/`InvalidDomain`. Precisão baixa pode produzir norma diferente de um após arredondar componentes. Seis casas são o ponto de partida dos exemplos; a biblioteca respeita a precisão escolhida pela aplicação.

## API e convenções

API semelhante à Unity: componentes/indexador/Set, `identity`, `normalized`, `eulerAngles`, `Euler`, `AngleAxis`, `LookRotation`, `FromToRotation`, setters equivalentes, `Normalize`, `Inverse`, `Dot`, `Angle`, `ToAngleAxis`, `Lerp`/`Slerp` com variantes Unclamped, `RotateTowards` e operadores quaternion×quaternion/quaternion×vetor. Há overloads com `FixedStatus` e variantes `Try`; `Inexact` sozinho é sucesso. Mutadores `Try` preservam o destino em falha.

- Euler aplica Z, depois X, depois Y (`Ry * Rx * Rz`). Extração retorna [0,360), com Y=0 na singularidade. As variantes `EulerRadians`, `ToEulerRadians`, `AngleAxisRadians`, `AngleRadians`, `ToAngleAxisRadians` e `RotateTowardsRadians` explicitam radianos.
- `Angle`, `AngleAxis`, `ToAngleAxis` e `RotateTowards` usam graus. Em p=17/18, parte da faixa de graus excede o intervalo de Fixed64: use radianos ou trate `Overflow`.
- Interpolação escolhe o menor caminho e normaliza a saída. `RotateTowards` aceita delta negativo, afastando-se até a orientação oposta; orientações coincidentes permanecem coincidentes.
- Rotação de vetor exige quaternion unitário. Composição não é comutativa: `a*b` aplicado a um vetor corresponde a `a*(b*v)` para entradas unitárias, sujeito à quantização.
- Direções opostas escolhem perpendicular a partir do menor módulo de componente, com desempate X/Y/Z. LookRotation com up colinear/zero usa FromToRotation do eixo forward. Direção obrigatória zero retorna identidade com `InvalidDomain`; entrada não finita e inverse de zero falham.
- `==` compara componentes exatamente conforme Fixed64. `Equals`/hash conservam suas regras para NaN e zeros com sinal. `q` e `-q` são valores distintos; `ApproximatelyRotation` compara orientação com tolerância angular explícita. `CanonicalizeSign` é opt-in, usando W/X/Y/Z para escolher sinal e zeros positivos.

## Rede e patches

Negocie `FixedQuaternionNetworkProfile` uma vez. Descriptors versionados incluem a política de composição; incompatibilidade falha antes de alterar buffers/cursores.

| Perfil | Payload | Contrato |
|---|---:|---|
| Components/FixedWidth (padrão) | 32 bytes | Todos os bits, little-endian |
| Components/VarInt | 4–40 bytes | Codificação escalar existente |
| Components/BitPacked | Soma dos bits dos campos | Limites raw/precisão negociados |
| SmallestThree | 4/6/8 bytes | 32/48/64 bits, perda explícita |

```csharp
var exact = new FixedQuaternionNetworkProfile(FixedRuntime.Settings);
var compact = FixedQuaternionNetworkProfile.SmallestThree(
    FixedRuntime.Settings, totalBits: 48, allowLossy: true);
int offset = 0;
FixedQuaternionNetwork.TryWrite(packet.AsSpan(), ref offset, rotation, compact, out status);
```

SmallestThree normaliza internamente, omite a componente de maior módulo (empates X/Y/Z/W), fixa seu sinal positivo e quantiza as outras três no intervalo ±1/√2. Zero é representável; o código signed mínimo é reservado. Decode rejeita esfera inválida, domínio dominante incompatível e padding não zero. Não renormaliza após quantizar a saída. Zero/NaN/infinities não são rotações compactáveis.

Overloads Span alinham ao byte, verificando padding; pointer writers/readers preservam cursores em bits. Falhas não publicam bytes parciais. `Canonicalize` projeta uma vez por encode/decode. Para inputs compactos, também decodifique o input local antes de simular: todos os participantes precisam consumir a mesma representação. Checkpoints autoritativos/replay usam o codec exato.

`FixedQuaternionDelta` fica em `LegendaryTools.Deterministic` (assembly `LegendaryTools.Deterministic.FixedPointAdapters`). `TryCreateCheckpoint` usa buffer por ponteiro do chamador; a view empresta seus bytes. `TryWrite` seleciona XOR com máscara de bytes alterados ou Full, conforme o custo total. Comprimentos diferentes exigem Full. `Forward` é padrão; `Reversible` também restaura antes/depois. Envelope de 50 bytes identifica IDs, perfil, versão e hashes: esse custo se soma ao payload. `TryApply` valida baseline e resultado integralmente antes de publicar e permite substituir a baseline in-place.

Guarde **os bytes codificados** das baselines compactas. Reversão recupera esses bytes, sem recriar o quaternion original anterior à perda. A retenção, confirmação da baseline e reenvio pertencem ao protocolo da aplicação; o histórico do mundo já mantém seus deltas exatos independentemente deste codec.

## ECS, Burst e Inspector

`FixedQuaternionPolicy` oferece codec de 32 bytes, igualdade/hash e ordem lexicográfica para coleções. Pode ser usado nos bindings Cell/List/SparseSet Managed/Native. Importar/restaurar não normaliza nem modifica componentes. Não há dependência de Entities; completar jobs antes de publicar/restaurar o estado.

`QuaternionSimulationTransition`, `QuaternionSimulation` e `NativeQuaternionSimulation` demonstram a mesma regra de gameplay em storage Managed/Native, com tempo/inputs externos e publicação após os jobs.

`ToUnityQuaternion`/`ToFixedQuaternion` são conversões explícitas para apresentação/importação; não normalizam. `SerializedFixedQuaternion` armazena raws e precisão declarada. Default do wrapper é zero com três casas, como os vetores. Os drawers IMGUI/UI Toolkit editam quatro componentes em texto decimal sem passagem por float/double, com múltiplos objetos, Undo, reescala atômica e overrides de prefab. O drawer não inicializa o runtime.

## Verificação

### Benchmarks de CPU e memória

#### Otimizações aplicadas

A rodada de otimização usa `FixedBigUInt.DivRem` no arredondamento racional: quociente/resto em uma passada, com divisão por palavra para denominadores de até 64 bits. A política de arredondamento, flags, zeros com sinal e overflow permanecem iguais. O caminho compartilhado beneficia também operações de vetores; a validação cobre o assembly FixedPoint completo.

O codec de quaternion mantém o staging atômico e copia bytes quando início e comprimento estiverem alinhados. Escritas em offsets de bits usam o caminho anterior. O limite de compactação usa palavras inteiras que reproduzem exatamente a expressão da versão 1 nos guardas 128/256, sem cache global mutável ou inicialização dinâmica. Buffers compactos sem capacidade rejeitam cedo apenas entradas finitas não zero, mantendo `BufferTooSmall | Inexact` e a precedência dos erros de domínio.

`QuaternionRotationStep` é uma entrada imutável preparada com graus, quaternion, status de construção e fingerprint. Compartilhe-a entre entidades/ticks sob o mesmo runtime congelado. O overload preparado rejeita valor default/perfil incompatível; preserva a acumulação de status e suporta usar a mesma variável como entrada/saída. As simulações Managed/Native retêm a última entrada por instância, um cache derivado que não precisa entrar no snapshot: restaurar o estado raw não altera seu cálculo.

```csharp
var turn = QuaternionSimulationTransition.Prepare(7, out var construction);
// Dentro do loop: preserva a transição original, incluindo a normalização configurada.
QuaternionSimulationTransition.TryRotate(rotation, turn, out rotation, out var status);
```

O valor `FixedQuaternion` continua com 32 bytes e o perfil com 224. O cache de 680 bytes por orientação não foi incorporado; seu custo/benefício depende da carga. Reversão, codecs e fingerprints mantêm a versão 1. A comparação antes/depois usa cenários correspondentes, incluindo loop/digest, sem somar ganhos de microbenchmarks como se fossem aceleração da aplicação inteira.

#### Comparação das otimizações com o histórico

A rodada otimizada passou **7 + 5 testes em .NET e 13 em Unity/Burst**, sem falhas ou skips nos benchmarks selecionados. São **1.854 medições**, com todas as **1.851 linhas históricas correspondentes** e três novas linhas de `TickPrepared`. A primeira rodada completa está preservada, junto a uma repetição de três testes Q5/Q7 para investigar variações desfavoráveis. Os tempos abaixo são medianas Burst no mesmo i9-13900KF/Windows x64, com três casas decimais e perfil Accurate; incluem loop/digest e uma chamada por lote.

| Operação/cenário | Histórico (ns/item) | Otimizado, primeira rodada (ns/item) | Razão histórico/otimizado |
|---|---:|---:|---:|
| Q1 arredondamento racional, divisores mistos | 472,66 | 44,53 | 10,61× |
| Q2 inverse, entradas finitas variadas | 2.175,00 | 443,75 | 4,90× |
| Q2 rotate, entradas finitas variadas | 2.578,13 | 1.021,88 | 2,52× |
| Q5 write FixedWidth exato, alinhado | 1.000,00 | 631,25 | 1,58× |
| Q5 write compacto 32 bits, alinhado | 42.612,50 | 30.343,75 | 1,40× |
| Q5 read compacto 32 bits, alinhado | 15.393,75 | 7.284,38 | 2,11× |
| Q5 falha de write compacto 32 bits, buffer insuficiente | 42.568,75 | 175,00 | 243,25× |
| Q7 `Tick` histórico → `TickPrepared`, 128 entidades/quatro entradas | 57.021,09 | 3.657,81 | 15,59× |

Q1 também passou de 1.042,97 para 95,31 ns em nove casas (10,94×) e de 1.889,06 para 647,66 ns em 18 (2,92×). O ganho da falha de capacidade remove a matemática de um pacote que seria rejeitado; não descreve um encode bem-sucedido.

`TickPrepared` exige preparar as entradas antes do trecho medido. O valor tem **64 bytes**; quatro entradas compartilhadas usam **256 bytes lógicos**, contra os 144 do protótipo anterior sem metadados de validade/perfil. O ganho frente ao `Tick` da mesma rodada é 16,88× no Burst e 10,32× em .NET Managed. Na repetição, o preparado ficou em 3.475,78 ns e o não preparado em 56.135,16 ns (16,15× entre eles). A simulação reutiliza sua última entrada por instância; quando os graus mudam, paga novamente o preparo.

Há operações sem ganho relevante. Normalize ficou em 2.609,38 → 2.612,50 ns; Slerp genérico em 140.359,38 → 143.531,25 ns. `Tick` sem preparo ficou 8,31% mais lento na primeira rodada e 1,55% mais rápido que o histórico na repetição. Read FixedWidth exato variou de 221,88 ns históricos para 256,25 e depois 225,00 ns. A falha BitPacked com offset 5 subiu 66,47% na primeira rodada e 4,79% na repetição. Esses resultados não sustentam prometer aceleração uniforme; as duas rodadas, p95 e amostras individuais permanecem disponíveis.

Os cenários correspondentes mantêm bytes de payload, reservas e páginas vivas iguais. Todos mediram zero bytes GC nas plataformas com contador funcional; Unity usou o contador calibrado de eventos e registrou zero `GC.Alloc` na thread chamadora. O tamanho de quaternion/perfil e o staging de 40 bytes não aumentaram. Q9 conserva o `WorldHistory` histórico como controle sobre frames pré-codificados; suas fontes capturadas estão arquivadas. Alterações concorrentes de histórico local no workspace estão fora desses tempos, e `StateRegistry` não é exercitado nesse benchmark.

Dados e fontes: [rodada otimizada](../../../../Tools/FixedPoint/Reports/QuaternionOptimizedBenchmarks/summary.json), [comparação completa](../../../../Tools/FixedPoint/Reports/QuaternionOptimizationComparison/comparison.json), [repetição Q5/Q7](../../../../Tools/FixedPoint/Reports/QuaternionOptimizedBenchmarks/Repeat/summary.json), [repetição contra o histórico](../../../../Tools/FixedPoint/Reports/QuaternionOptimizationComparison/HistoricalRepeat/comparison.json). `compare_quaternion_benchmarks.py` confere cenário, backend, escopo, precisão, itens e payload; rótulos históricos repetidos de descritor usam a ordem da fixture, registrada como `occurrence`. Não há assert de tempo absoluto.

#### Oportunidades medidas na rodada histórica

`QuaternionCpuBenchmarks` e `QuaternionRuntimeBenchmarks` são fixtures **Explicit**. A rodada histórica local em 12/10/2026 passou 7 + 4 testes em .NET x64 Release e 12 em Unity 6000.3.20f1/Burst 1.8.29, registrando 1.851 linhas de medição, antes de incorporar as otimizações acima. Os protótipos experimentais restantes ficam no assembly de testes. Cada protótipo verifica paridade de raw/status/cursores/bytes antes de medir; Burst exige marcador `[BurstDiscard]` sem fallback managed. `TickPrepared` mede a nova API de produção, separadamente do cache experimental anterior.

| ID | Oportunidade | Cobertura e implicação |
|---|---|---|
| Q1 | Reunir divisão e resto do arredondamento; especializar divisor de até 64 bits | Precisões 3/9/18, largura 0–512 bits, oráculo BigInteger de quociente/resto, cinco arredondamentos, quatro overflows e zeros com sinal. No Burst: uma passada ~1,42–1,48×; caminho por palavra ~10,3–10,4× em 3/9 e ~2,76× na mistura de divisores de 18. É um benchmark do arredondador, sem aplicar o ganho ao quaternion inteiro. |
| Q2 | Mapear o custo de geometria e da precisão de guarda | Normalize, inverse, rotate, angle, Euler e extração, eixo/extração, from-to, look, lerp, slerp e rotate-towards; Accurate/Fast, 3/18 casas, quase paralelos, hemisfério oposto, domínio zero e argumentos enormes. Evidencia raízes/divisões/CORDIC e caminhos especiais; não propõe substituir Slerp por Lerp nem alterar sua semântica. |
| Q3 | Preparar coeficientes exatos para muitos vetores da mesma orientação | 1/8/32 vetores por quaternion, com cache pronto e construção incluída. Cache usa **680 bytes/orientação**, contra 32 do valor. No Burst, 3 casas/8 vetores: ~1,30× com cache pronto e ~1,22× incluindo preparo. Com uma consulta, o preparo pode eliminar o ganho; mais precisão também muda o retorno. Invalidações dependem do quaternion e do contexto numérico. |
| Q4 | Reutilizar fingerprint/validação de perfil e limite ±1/√2 | Perfis mistos e guardas 128/256; caches só valem para descritores imutáveis/contexto compatível. Fingerprint e validação custam dezenas de ns; recomputar o limite custa µs. `LimitCached` mede apenas a constante pronta, sem implementar um codec preparado nem prometer ganho equivalente no encode/decode completo. |
| Q5 | Copiar bytes alinhados e rejeitar compactação sem capacidade antes da matemática | FixedWidth/VarInt/BitPacked e compactos 32/48/64, precisão wire reduzida, offsets 0/5, write/read/canonicalize, descritores e falhas. Cópia por byte em FixedWidth exato/alinhado: ~1,75× no Burst, preservando staging atômico. O controle `WriteStageControl` mantém o staging já existente; o candidato muda a cópia final. Rejeição antecipada só se aplica ao caso compacto validado, preservando status/precedência. |
| Q6 | Separar tamanho constante, measure validado e codec canônico | 128/4.096 quaternions, payload exato de 32 bytes, Measure/Write/Read e custo de validação. Medida por writer ainda percorre os dados; chamar Measure não é equivalente a obter o tamanho constante. |
| Q7 | Reutilizar AngleAxis de entradas compartilhadas entre entidades | Mesmas transições públicas e status para entradas 0/7/-14/21. Cache de quatro deltas ocupa **144 bytes lógicos**. No Burst, ~10,9× no cenário de 128 entidades; inclui multiplicação/normalização, exclui construir o cache. Só reutilizar entradas iguais sob a configuração congelada. |
| Q8 | Mapear decodificação, hashes, medida e apply de deltas | Seis codificações, Forward/Reversible, sem mudanças/esparso/denso, baseline ID errado, hash calculado/cache. O hash cached exige bytes imutáveis: o teste modifica a view emprestada e comprova que o cache fica inválido. Mede custos e bytes efetivos, sem retirar as validações do protocolo. |
| Q9 | Comparar políticas de histórico por densidade, reserva e retenção | 128/512 entidades × 16 frames, 0/1/100% alterações agrupadas, FullPerTick/KeyframesAndDeltas/CopyOnWrite, páginas 256/4.096, restore com idades 0/7/15. Distingue **reserva de memória** de dados/páginas vivos; retenção menor pode usar reserva maior. `NativeHost` executa C# sobre memória nativa e é identificado separadamente do Burst. |
| Q10 | Medir agendamento/completion e granularidade de jobs | 1/64/1.024 entidades; IJob e IJobParallelFor com batches 1/16/128, com scheduler e completion dentro do tempo. Resultados raw/status são verificados para cada entidade. O melhor batch varia com o volume/custo; execução por entidade tem sobrecusto observável para lotes pequenos. |

Resultados completos, CSVs com median/p95/amostras, ambiente e hashes de 61 fontes: [QuaternionBenchmarks](../../../../Tools/FixedPoint/Reports/QuaternionBenchmarks/summary.json). São comparações locais no i9-13900KF/Windows x64; não são limites de desempenho de outras plataformas. Não há assert de tempo absoluto.

Cada cenário usa três aquecimentos e nove amostras, com preparação, validação, descarte e reporting fora do tempo. O percentil usa nearest rank sobre as nove amostras. .NET roda com `DOTNET_TieredCompilation=0` para evitar troca de tier durante a comparação. Tempo inclui loop/digest e, no Burst, uma transição por lote. Casos de jobs incluem scheduling/completion. Inputs do harness têm 496 bytes/amostra; o dataset contém campos auxiliares de todas as operações, portanto não representa o layout de componentes ECS. Bytes extras expressam o orçamento lógico do cache/política, não devem ser somados ao harness como uma estimativa do consumo de gameplay.

Alocação managed é calibrada com uma alocação conhecida. Quando o contador de bytes não funciona, registra -1 e verifica eventos `GC.Alloc` com controle vazio em execuções separadas. Conta a thread chamadora; não estima alocações de worker threads. `sizeof(FixedQ)=68`, quaternion de guarda=272, perfil=224, staging de codec=40, descritor máximo=172 bytes. Esses números são layouts/scratch explícito; não são pico de stack, spills do compilador ou RSS. Reserve/live bytes do histórico são registrados separadamente de overhead de objetos/allocator.

```powershell
$env:DOTNET_TieredCompilation='0'
$env:LEGENDARY_QUATERNION_BENCHMARK_DIR='Temp/QuaternionBenchmarks/managed'
dotnet run --project Standalone~/FixedPoint.Tests/LegendaryTools.FixedPoint.Tests.csproj -c Release -- --where "class =~ QuaternionCpuBenchmarks" --workers=0
dotnet run --project Standalone~/Deterministic.Validation/LegendaryTools.Deterministic.Validation.csproj -c Release -- --where "class =~ QuaternionRuntimeBenchmarks" --workers=0
python Tools/FixedPoint/create_quaternion_test_project.py <projeto-temporario-fora-do-pacote>
$env:LEGENDARY_QUATERNION_BENCHMARK_DIR='<pasta-dos-csvs-unity>'
unity test <projeto-temporario> --mode EditMode --filter 'QuaternionCpuBenchmarks;QuaternionRuntimeBenchmarks' --timeout 1800 --output <relatorio.xml> -- -nographics
```

Execute os processos de medição em sequência para evitar competição entre as próprias suítes. `summarize_quaternion_benchmarks.py` reúne os relatórios, compara cenários correspondentes, verifica fontes da cópia isolada e salva hashes; ele exige as duas execuções .NET e a execução Unity completas.

Consulte [QUATERNIONS_VALIDATION.md](QUATERNIONS_VALIDATION.md) para corpus, plataformas, erros medidos, custos e limitações. Os geradores de referência precisam de `mpmath==1.3.0`; consumidores usam os arquivos gerados.

```text
dotnet run --project Standalone~/FixedPoint.Tests/LegendaryTools.FixedPoint.Tests.csproj -c Release -- --where "class =~ FixedQuaternion" --noresult --workers=0
dotnet run --project Standalone~/Deterministic.Validation/LegendaryTools.Deterministic.Validation.csproj -c Release -- --where "class =~ FixedQuaternion" --noresult --workers=0
python Tools/FixedPoint/generate_quaternion_reference.py
python Tools/FixedPoint/sync_quaternion_replay_corpus.py
python Tools/FixedPoint/compare_quaternion_traces.py x64.csv arm64.csv mono.csv il2cpp.csv
python Tools/FixedPoint/compare_quaternion_traces.py --cases 36480 x64-sequence.csv arm64-sequence.csv mono-sequence.csv il2cpp-sequence.csv
```

No Unity, rode os testes FixedQuaternion em EditMode com Burst habilitado. Builds de validação incluem flags `--fixed-point-validation`, `--fixed-quaternion-replay-trace=<arquivo>`, `--fixed-quaternion-sequence-trace=<arquivo>` e `--fixed-quaternion-normalize-composition` (modo NormalizeResult em processo novo). Use o projeto temporário criado por `create_validation_project.py` para os builders Mono, IL2CPP e Android ARM64.
