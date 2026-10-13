# Bounds/AABB determinísticos

`FixedBounds2` e `FixedBounds3` são valores mutáveis unmanaged de 32/48 bytes no namespace `LegendaryTools.FixedPoint`. Usam `FixedVector2/3` e componentes `Fixed64`, com o mesmo núcleo inteiro em Managed, Burst e standalone .NET Standard 2.1.

## Inicialização e representação

```csharp
FixedRuntime.Initialize(new FixedSettings(decimalPlaces32: 3, decimalPlaces64: 6));
FixedBoundsRuntime.Initialize(new FixedBoundsSettings(FixedBoundsRepresentation.MinMax));
// Alternativa global: FixedBoundsRepresentation.CenterExtents.
```

Inicialize no host Managed antes de threads, jobs, coleções registradas e uso dos valores. A configuração fica congelada por processo/domínio; repetir a mesma configuração é permitido, mudar retorna `ConfigurationFrozen`. Todos os mundos do domínio compartilham o modo. `FixedBoundsSettings.Default` e `default(FixedBoundsSettings)` selecionam MinMax. O modo não ocupa espaço em cada caixa.

| Representação | Primeiro vetor armazenado | Segundo vetor armazenado |
|---|---|---|
| MinMax, padrão | Limite mínimo | Limite máximo |
| CenterExtents | Centro | Extensões |

`default(FixedBoundsN)` é um ponto na origem, válido depois da inicialização. Não representa uma caixa vazia. `Invalid` é um sentinela com NaNs; `IsValid` rejeita NaN, infinitos, limites invertidos, extensões negativas e endpoints não representáveis. Uma largura total pode exceder Fixed64 mesmo com endpoints válidos: `TryGetSize` permite detectar esse overflow sem impedir consultas de contenção/interseção.

## API e arredondamento

```csharp
var box = new FixedBounds3(center: FixedVector3.zero, size: new FixedVector3(2, 4, 6));
bool inside = box.Contains(new FixedVector3(1, 2, 3));
box.TryEncapsulate(new FixedVector3(5, 0, 0), out FixedStatus status);
box.TryExpand(Fixed64.FromRatio(1, 2), out status);
FixedVector3 closest = box.ClosestPoint(new FixedVector3(8, 0, 0), out status);
Fixed64 squaredDistance = box.SqrDistance(new FixedVector3(8, 0, 0), out status);
```

Os dois tipos expõem `center`, `size`, `extents`, `min`, `max`, construtor `(center, size)`, `FromMinMax`, `FromCenterExtents`, `SetMinMax`, `Encapsulate`, `Expand`, `Contains`, `Intersects`, `ClosestPoint`, `SqrDistance` e `IntersectRay`. Factories, consultas e mutadores têm variantes `Try` com `FixedStatus`; propriedades têm `TryGet...`/`TrySet...`.

`Contains` e `Intersects` incluem as fronteiras; faces, arestas, pontos e dimensões degeneradas contam como contato. `ClosestPoint` preserva pontos internos. `Expand(amount)` altera o tamanho total por eixo, aplicando metade em cada lado. Uma redução que inverter os limites falha.

MinMax preserva exatamente os endpoints de `SetMinMax` e da união feita por `Encapsulate`. Centro e extensões derivados seguem o arredondamento numérico congelado. CenterExtents armazena centro/extensões diretamente; conversões de endpoints escolhem o centro arredondado e extensões que contêm ambos os limites.

Meios de raw não são representáveis. Construções, mudanças de tamanho/centro/extensões e expansão arredondam limites para fora, marcando `Inexact` quando necessário. Exemplo: na grade raw, construir centro 0 e tamanho 1 resulta em [-1, 1]. Converter [0, 1] para CenterExtents pode produzir [-1, 1] ou [0, 2], conforme o arredondamento do centro. A margem adicional é no máximo um raw por borda em cada conversão. Reduções de meio raw podem não diminuir a caixa. Alterar o centro de um MinMax de largura ímpar pode ampliar sua largura em um raw; getters não garantem todas as identidades algébricas contínuas da Unity nessa grade.

Reatribuir uma propriedade ao valor numérico que ela já expõe é uma operação sem alteração. Mutadores validam todas as saídas antes de publicar; falhas deixam a instância intacta, inclusive sob políticas escalares Saturate/WrapFinite. `Inexact` sozinho é sucesso. Métodos/propriedades sem status descartam esse diagnóstico; construtores que falham produzem `Invalid`. Use `Try` no gameplay que precisa distinguir erro de ausência de contato. Parsing, formatação e inicialização são Managed.

## Raios

```csharp
var ray = new FixedRay3(new FixedVector3(-4, 0, 0), new FixedVector3(2, 0, 0));
bool success = box.TryIntersectRay(ray, out bool hit, out Fixed64 t, out status);
FixedVector3 position = ray.GetPoint(t, out status);
```

`FixedRay2/3` preservam a direção; não a normalizam implicitamente. `t` é o parâmetro não negativo de `origin + direction * t`. Só é distância métrica para direção unitária. Origem interna retorna zero; tangências contam; contatos atrás da origem são rejeitados. Eixos paralelos não dividem por zero; direção totalmente zero consulta a contenção da origem.

O teste de contato compara frações inteiras exatas antes de arredondar `t`. O overload booleano pode confirmar contato quando `t` não cabe em Fixed64; o overload que exige a distância e `TryIntersectRay` reportam overflow nesse caso. Um miss válido tem `success=true`, `hit=false`, `t=0`.

## Rede

```csharp
var coordinate = new FixedNetworkProfile(FixedRuntime.Settings, FixedKind.Signed64,
    FixedCodec.BitPacked, lowerRaw: unchecked((ulong)Fixed64.FromInt(-1024).RawValue),
    upperRaw: (ulong)Fixed64.FromInt(1024).RawValue);
var fields = new FixedVectorNetworkProfile(coordinate, dimensions: 3);
var profile = new FixedBoundsNetworkProfile(fields);
Span<byte> packet = stackalloc byte[60];
int offset = 0;
bool written = FixedBoundsNetwork.TryWrite(packet, ref offset, box, profile, out status);
```

Limites são raws na precisão de transmissão; ajuste o exemplo à precisão configurada. `FixedBoundsNetworkProfile` negocia representação e dois `FixedVectorNetworkProfile`, com faixas/precisões independentes. Todos os campos compartilham configuração numérica e codec. Perfis/representações incompatíveis retornam `ProfileMismatch`.

| Codec | Bounds2 | Bounds3 |
|---|---:|---:|
| FixedWidth little-endian | 32 bytes | 48 bytes |
| VarInt | 4–40 bytes | 6–60 bytes |
| BitPacked | Soma dos bits dos quatro campos | Soma dos bits dos seis campos |

Os valores contêm primeiro X/Y[/Z], depois segundo X/Y[/Z]. O descritor `FPB1`, versão 1, tem cabeçalho de oito bytes (magic, versão, dimensão, representação, reservado zero), seguido dos dois descritores vetoriais existentes: 184 bytes em 2D e 264 em 3D. Use `TryWriteDescriptor`, `TryReadDescriptor` e `TryNegotiate` uma vez no handshake.

Os codecs são atômicos para a caixa inteira: falhas preservam bytes e cursor. Leituras validam a geometria depois de decodificar ambos os vetores. Overloads por ponteiro mantêm cursor contínuo em bits; overloads Span alinham uma vez por caixa e zeram padding escrito. Não há clamp silencioso de faixa.

`AllowLossy=false` é o padrão. `Canonicalize`/`TryCanonicalize` quantizam os campos armazenados sem reconstruí-los pelas propriedades e revalidam a geometria; grids diferentes que invertam min/max são rejeitados. A quantização de transmissão segue os perfis escalares e pode encolher/deslocar a caixa; não promete envelope conservador. A verificação de faixa BitPacked ocorre na escrita, como nos codecs vetoriais.

Checkpoints usados para rollback, replay e restauração são exatos. Se valores quantizados alimentarem a simulação, aplique a mesma Canonicalize em todos os participantes no boundary escolhido pelo protocolo. Quantizar somente no envio cria estados diferentes.

## ECS, checkpoints e runtime

Os tipos não têm IDs, ownership, caches, interfaces de ECS ou referências a UnityEngine. Use componentes por composição e mantenha IDs estáveis separadamente. Arrays e coleções Managed/Native armazenam o mesmo valor.

```csharp
using LegendaryTools.Deterministic;

components.TryGet(entity, out FixedBounds3 copy).ThrowIfFailure();
if (copy.TrySetCenter(newCenter, out status))
    components.TrySet(entity, copy).ThrowIfFailure(); // Atualiza o estado rastreado da coleção.

FixedBoundsDeterministicProfile.TryFingerprint(out ulong numeric).ThrowIfFailure();
var compatibility = new DeterministicCompatibilityProfile(1, numeric,
    FixedRandom.AlgorithmId, default(Int32Policy).SchemaId, 1);
// Registrar Cell/List/Array/SparseSet via os bindings genéricos existentes.
registry.TryRegister(ManagedStateBindings.Cell(1, cell, default(FixedBounds3Policy),
    FixedBoundsRuntime.Settings.Fingerprint, 256, 256)).ThrowIfFailure();
registry.TryFreeze().ThrowIfFailure();
```

`FixedBounds2Policy`/`FixedBounds3Policy` oferecem igualdade numérica, hash estável, ordenação lexicográfica dos campos armazenados e codec canônico de 32/48 bytes. ±0 são iguais e têm o mesmo hash; o codec preserva seus raws distintos. Schemas versão 1: MinMax2=0x4120, MinMax3=0x4121, CenterExtents2=0x4122, CenterExtents3=0x4123. Nenhum padding/endereço/configuração por instância é serializado. `DeterministicWriter.CreateMeasure` é suportado.

`FixedBoundsDeterministicProfile` combina o perfil numérico/RNG existente com representação e versão geométrica. Use-o no perfil de mundos que registram Bounds. O transporte, histórico, deltas, confirmação e ressimulação continuam sob os contratos do runtime. O sample `BoundsSimulation`/`NativeBoundsSimulation` demonstra a mesma transição Managed/Burst; o host Native completa jobs antes da publicação.

`FixedBoundsUnity` oferece conversões explícitas com Bounds/Rect e importação de Ray/Ray2D. Reserve a fronteira IEEE para apresentação/importação. Wrappers e drawers de Inspector não fazem parte desta versão.

## Validação

Os benchmarks de CPU/memória por operação, oportunidades e medições locais estão em `BOUNDS_BENCHMARKS.md`. As três fixtures explícitas da categoria `BoundsBenchmark` cobrem geometria, codecs, working sets, alocação, histórico e granularidade de jobs, com amostras e calibração de GC.

As otimizações aplicadas ao núcleo e à serialização, comparação completa com a linha de base e validação posterior estão em [BOUNDS_OPTIMIZATIONS.md](BOUNDS_OPTIMIZATIONS.md). Os dados anteriores permanecem preservados.

Veja `BOUNDS_VALIDATION.md`. O corpus original versionado mantém seus 5.760 casos e checksum. O corpus de sequências acrescenta 960 configurações × 64 passos, incluindo checkpoint/restauração e repetição dos 32 passos finais. Oráculos independentes com BigInteger verificam envelopes de mutações, distância, quantização e frações de raycast, incluindo diferenças de 65 bits e somas de quadrados maiores que 128 bits. Testes de Burst com marcador BurstDiscard comparam diretamente raws, statuses, cursores e bytes em cada passo, nas ordens normal/inversa/permutada e nos lotes 1/17/64. A integração verifica checkpoints e eventos durante jitter, reordenação, duplicação, reutilização do histórico e falhas após mutação, nas três políticas de snapshot.

```text
python Tools/FixedPoint/generate_bounds_api.py
python Tools/FixedPoint/sync_bounds_replay_corpus.py --check
dotnet run --project Standalone~/FixedPoint.Tests/LegendaryTools.FixedPoint.Tests.csproj -c Release -- --bounds-mode=min-max --where "class =~ FixedBounds" --noresult --workers=0
dotnet run --project Standalone~/FixedPoint.Tests/LegendaryTools.FixedPoint.Tests.csproj -c Release -- --bounds-mode=center-extents --where "class =~ FixedBounds" --noresult --workers=0
dotnet run --project Standalone~/Deterministic.Validation/LegendaryTools.Deterministic.Validation.csproj -c Release -- --bounds-mode=min-max --where "class =~ FixedBounds" --noresult --workers=0
```

Coloque artefatos fora de Assets usando `--artifacts-path`. Use `prepare_bounds_validation.py` para criar um projeto Unity isolado. Execute `unity test <projeto> --mode EditMode --filter FixedBounds -- --fixed-bounds-test-mode=min-max` e repita em um novo processo com `--fixed-bounds-test-mode=center-extents`; o bootstrap aplica o modo congelado antes das fixtures. Execute os builders `FixedBoundsValidationBuild.BuildMonoBatch`, `BuildIl2CppBatch` ou `BuildAndroidBatch` nesse projeto. O player Windows aceita `--fixed-bounds-validation`, `--fixed-bounds-mode=min-max|center-extents` e `--fixed-bounds-report=<arquivo>`. O APK ARM64 inicia automaticamente; o extra Android Intent `bounds-mode` seleciona o modo antes da inicialização. O relatório registra plataforma, arquitetura, backend, modo, contagens, checksums e quantidade de palavras comparadas diretamente, com prova de execução Burst.

`summarize_bounds_validation.py <artefatos> <projeto> [destino]` exige XML das suítes em ambos os modos, as duas regressões, dois XML EditMode (`tests.xml`/`center-extents-tests.xml`), quatro JSON de player e o APK. O verificador rejeita testes Bounds ignorados, modo incorreto, checksum divergente, comparações incompletas e fontes Bounds diferentes do projeto isolado; preserva SHA256 dos fontes e das dependências copiadas. Relatórios da ampliação de testes estão em `Tools/FixedPoint/Reports/BoundsRevalidation`.
