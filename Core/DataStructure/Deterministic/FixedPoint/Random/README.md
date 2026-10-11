# Random determinístico para jogos

`FixedRandom` usa Philox4x32-10, inteiros e os tipos `Fixed32`, `UFixed32`, `Fixed64` e `UFixed64`. O mesmo seed, identidade de stream, perfil numérico e sequência de chamadas reproduzem os mesmos valores raw em managed e Burst. O estado pertence ao chamador: não há um gerador global.

## Começar: crítico, loot e spawn

```csharp
using System;
using LegendaryTools.FixedPoint;

FixedRuntime.Initialize(); // Uma vez, antes da simulação e dos jobs.
var random = FixedRandom.Create(seed: 1234, streamId: 7);

bool critical = random.Chance(Fixed64.FromRatio(1, 4));
int damage = random.RollDice(2, 6, modifier: 3);
Fixed32 spread = random.Range(Fixed32.FromInt(-2), Fixed32.FromInt(2));
UFixed64 factor = random.NextUFixed64(); // [0, 1)

Fixed64[] weights = { Fixed64.FromInt(70), Fixed64.FromInt(25), Fixed64.FromInt(5) };
ulong[] prefix = new ulong[weights.Length];
if (!FixedRandomWeights.TryBuild(weights.AsSpan(), prefix.AsSpan(),
    out FixedRandomWeightView lootTable, out FixedStatus status))
    throw new InvalidOperationException(status.ToString());
int lootIndex = random.WeightedIndex(lootTable);

var center = new FixedPointVector3(Fixed64.FromInt(20), Fixed64.Zero, Fixed64.Zero);
FixedPointVector3 spawn = random.InsideSphere(center, Fixed64.FromInt(5));
FixedPointVector3 direction = random.OnUnitSphere();
```

`FixedPointVector2` e `FixedPointVector3` são structs imutáveis, com campos `X`, `Y` e `Z` de `Fixed64`, de 16 e 24 bytes respectivamente. As amostras espaciais usam esses tipos. Conversões para representação visual podem ocorrer depois da simulação.

Os exemplos managed de gameplay, loot, eventos, rollback e codecs também estão no programa compilável [Examples.cs](../../../../../Standalone~/FixedPoint.RandomValidation/Examples.cs).

## API e intervalos

| Uso | Métodos |
|---|---|
| Bits e inteiros | `NextUInt32`, `NextUInt64`, `NextInt32`, `NextInt64` |
| Booleano e sinal | `NextBool`, `NextSign` |
| Unidade fixed | `NextFixed32`, `NextUFixed32`, `NextFixed64`, `NextUFixed64` |
| Intervalos | `Range(min, max)`, `RangeInclusive(min, max)` para os quatro inteiros nativos e os quatro fixed |
| Probabilidade | `Chance(probability)` para os quatro fixed, `ChanceRatio(numerator, denominator)` |
| Dados | `RollDie(sides)`, `RollDice(count, sides, modifier)` |
| Seleção | `PickIndex(count)`, `Choose(values)`, `Shuffle(values)`, `SampleWithoutReplacement(populationCount, output, scratch)` |
| Pesos | `WeightedIndex(weights)`, `WeightedIndex(table)`, `FixedRandomWeights.TryBuild` |
| Espaço 2D | `InsideUnitCircle`, `OnUnitCircle`, `InsideCircle`, `OnCircle`, `InsideRectangle`, `InsideTriangle` |
| Espaço 3D | `InsideUnitSphere`, `OnUnitSphere`, `InsideSphere`, `OnSphere`, `InsideBox`, `InsideTriangle` |
| Ângulo | `NextAngleRadians` em `[0, Fixed64.Tau)` |
| Lotes | `Fill` para os quatro fixed, `FillBytes` |
| Estado | `CaptureState`, `RestoreState`, `Seek`, `Advance`, `Position`, `IsExhausted` |

Os métodos numéricos e espaciais possuem variantes `Try...`, com `out FixedStatus`, e `...Checked`. Suas variantes simples descartam o status; use `Try...` para dados que precisam de validação. Em falha, retornos fixed são `NaN`, vetores são `default`, índices são −1 e os demais resultados são zero/false ou `default(T)` na escolha. `Checked` lança uma exceção e destina-se a managed. As conveniências managed de spans (`Choose`, `Shuffle`, `Fill`, `SampleWithoutReplacement`, `WeightedIndex`) também lançam em falha; oferecem `Try...` para tratar status. Nos jobs, use as variantes `Try...` ou com status.

`Range` é `[min, max)` e `RangeInclusive` é `[min, max]`. Para `min == max`, ambos retornam a constante sem consumir bits. `min > max`, limites especiais e probabilidades fora de `[0, 1]` são inválidos. `Chance(0)` e `Chance(1)`, dado de uma face, raio zero e lotes vazios também não consomem bits. Na precisão zero, `[0, 1)` contém apenas zero.

Fixed é sorteado diretamente na grade raw, sem passar por `float` ou `double`. Uma amostra unitária tem probabilidade igual para cada raw de zero até `10^p - 1`. A redução de inteiros usa rejeição de resíduos incompletos, evitando viés de módulo. Nenhum sampler válido gera códigos reservados ou zero negativo.

Chamadas com falha preservam o estado. Fill, shuffle e amostragem sem reposição verificam a operação antes de alterar os buffers; isso pode exigir duas passagens. Buffers/scratch sobrepostos são rejeitados quando a operação exige regiões distintas. A API de ponteiros exige memória válida fornecida pelo chamador.

## Streams, eventos e rollback

```csharp
// Uma stream contínua por entidade/canal. IDs devem permanecer estáveis no replay.
var combat = FixedRandom.Create(matchSeed, entityCombatStreamId);
FixedRandomState checkpoint = combat.CaptureState();
int firstDamage = combat.RollDice(2, 6);
combat.RestoreState(checkpoint);
int replayDamage = combat.RollDice(2, 6); // Mesmo resultado.

// A ordem global de processamento não altera o resultado de um evento.
var eventRandom = FixedRandom.ForEvent(matchSeed, lootStreamId, eventId);
int eventLoot = eventRandom.PickIndex(10);
```

Defina uma convenção estável para `streamId`, por exemplo um ID persistente atribuído a cada par entidade/canal. `eventId` deve identificar o evento de gameplay independentemente da ordem de jobs. Não use endereço, hash de string de runtime, horário ou índice de worker como identidade. Em `ForEvent`, chamadas dentro do mesmo evento ainda precisam manter sua ordem.

`FixedRandom` é uma struct mutável de 48 bytes em memória; copiar a struct copia a posição atual. Passe-a por `ref` quando a função precisa atualizar a stream do chamador. Não compartilhe uma instância mutável entre threads. `default(FixedRandom)` equivale a `Create(0, 0)`.

`Position` conta palavras raw de 32 bits, não chamadas de gameplay. `NextUInt64` consome duas palavras, com a primeira nos 32 bits inferiores. Rejeição pode consumir várias tentativas; `Fill` produz a mesma sequência das chamadas escalares correspondentes e `FillBytes` consome uma palavra por byte, usando os oito bits inferiores. `Seek` e `Advance` operam nessa posição raw. `ulong.MaxValue` indica exaustão: não há wrap silencioso nem um novo ciclo automático.

## Managed, Burst e buffers

O núcleo pertence à assembly `LegendaryTools.FixedPoint`, sem dependência de engine. Spans dão conveniência em managed, inclusive escolha de objetos/referências. Ponteiros, tipos unmanaged e status são o caminho dos kernels Burst. O núcleo não aloca arrays, listas, delegates ou memória managed durante a geração. Inicialize `FixedRuntime` no thread principal antes dos jobs; APIs raw de inteiros não dependem da configuração fixed.

`LegendaryTools.FixedPoint.Unity` oferece `FixedRandomNative` para `NativeArray<T>`: fill, bytes, escolha, shuffle, amostragem sem reposição, pesos diretos e construção/uso de tabela. O chamador cria, agenda, mantém vivo e descarta os containers.

```csharp
using LegendaryTools.FixedPoint;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

[BurstCompile]
public struct SpawnJob : IJobParallelFor
{
    public NativeArray<FixedRandom> Streams;
    [WriteOnly] public NativeArray<FixedPointVector3> Spawns;
    [WriteOnly] public NativeArray<FixedStatus> Statuses;

    public void Execute(int index)
    {
        var random = Streams[index];
        random.TryInsideUnitSphere(out FixedPointVector3 spawn, out FixedStatus status);
        Spawns[index] = spawn;
        Statuses[index] = status;
        Streams[index] = random; // Persiste o avanço da stream da entidade.
    }
}
```

Preencha `Streams` com `Create(seed, stableStreamId)` antes de agendar. Cada índice possui uma stream independente, portanto alterar o tamanho dos lotes ou a ordem de workers preserva seus resultados. Para eventos independentes, use `ForEvent` com IDs estáveis dentro do job.

Seleção por pesos diretos valida e percorre os pesos em O(n). Tabelas prefixadas custam O(n) para construir e O(log n) por seleção, e produzem o mesmo resultado da seleção direta. Pesos precisam ser finitos e não negativos; total zero ou overflow de `ulong` são erros. Pesos zero nunca são selecionados. Não modifique o buffer prefixado durante o uso da tabela. `FixedRandomWeightView` mantém um span; `FixedRandomWeightedTable` mantém um ponteiro; `FixedRandomNativeWeights` mantém o `NativeArray`, reconstruindo a visão de ponteiro dentro do kernel.

`SampleWithoutReplacement` devolve índices únicos em ordem aleatória, com scratch de pelo menos `populationCount` inteiros. Os buffers pertencem ao chamador; valores repetidos na população original podem continuar sendo iguais apesar de seus índices serem distintos.

## Distribuições espaciais

Interiores de disco e esfera usam rejeição com teste exato de distância em inteiros largos: cada ponto aceito da grade tem a mesma probabilidade, incluindo a borda. Retângulos e caixas usam intervalos independentes `[min, max)` por eixo; eixo com limites iguais é constante. Raio negativo, limites não finitos e `center ± radius` fora do domínio finito são rejeitados antes de consumir a stream.

Superfícies usam ângulo uniforme para círculo e altura/azimute uniformes para esfera. Triângulos usam transformação baricêntrica com raiz, uniforme em área antes da quantização; triângulos degenerados são rejeitados. Trigonometria e raiz usam kernels inteiros com precisão interna adicional, seguida do arredondamento configurado. O vetor de superfície quantizado pode ter comprimento ligeiramente diferente do raio. Precisões baixas agrupam amostras no mesmo ponto; a distribuição de superfície/triângulo não é uniforme sobre os pontos da grade final. Testes verificam tolerâncias, simetria e momentos, além de reprodução raw.

## Rede: negociação, snapshot e posição

Negocie `FixedRandomNetworkProfile` ao entrar na sessão. Seu descritor tem 16 bytes, little-endian, e incorpora o fingerprint numérico de FixedPoint. Protocolos/algoritmos/samplers desconhecidos e configurações incompatíveis são rejeitados. O handshake da aplicação também define seed, stream/event IDs e versão das regras de gameplay.

| Offset | Descritor v1 |
|---:|---|
| 0–3 | ASCII `FRNG` |
| 4 | Protocolo: 1 |
| 5 | Algoritmo: 1, Philox4x32-10 |
| 6–7 | Versão dos samplers: 1 |
| 8–15 | `FixedSettings.NumericFingerprint` das configurações |

O snapshot canônico é `FixedRandomState.Bytes == 24`: `Key` nos bytes 0–7, `SequenceId` em 8–15 e `WordPosition` em 16–23. O cache de quatro palavras é derivado e nunca trafega. Use os codecs para serialização; o layout em memória não define o pacote.

```csharp
var localProfile = new FixedRandomNetworkProfile(FixedRuntime.Settings);
byte[] packet = new byte[FixedRandomNetworkProfile.DescriptorBytes + FixedRandomState.Bytes];
int writeOffset = 0;
if (!FixedRandomNetwork.TryWriteDescriptor(packet.AsSpan(), ref writeOffset, localProfile, out var status) ||
    !FixedRandomNetwork.TryWriteState(packet.AsSpan(), ref writeOffset, random.CaptureState(), localProfile, out status))
    throw new InvalidOperationException(status.ToString());

int readOffset = 0;
if (!FixedRandomNetwork.TryReadDescriptor(packet.AsSpan(), ref readOffset, out var remoteProfile, out status) ||
    !localProfile.IsCompatible(remoteProfile) ||
    !FixedRandomNetwork.TryReadState(packet.AsSpan(), ref readOffset, remoteProfile, out var state, out status))
    throw new InvalidOperationException("Pacote ou perfil incompatível.");
var restored = default(FixedRandom);
restored.RestoreState(state);
```

Em uma sessão já negociada, envie o seed uma vez e transmita eventos/IDs estáveis em vez dos resultados quando a simulação permitir. Um checkpoint apenas de posição usa VarUInt canônico de 1–10 bytes, ou `FixedCodec.FixedWidth` de oito bytes. `TryReadPosition` aplica `Seek` à stream existente e mantém sua chave/sequence; remetente e destinatário precisam já ter a mesma identidade. O checkpoint é uma posição absoluta, permitindo recuperação após perda ou reordenação quando a aplicação identifica o tick correspondente.

Há overloads com `FixedBufferWriter`/`FixedBufferReader` e ponteiros, utilizáveis em Burst, e com span/offset para managed. Falhas preservam cursor e estado; escrita insuficiente não deixa pacote parcial. VarUInt truncado, redundante ou com overflow é rejeitado. Os IDs/ticks e cabeçalhos da aplicação são custos adicionais aos tamanhos citados.

Seed e snapshot são exatos. Para enviar resultados fixed com precisão reduzida, use os perfis de `FixedNetwork` e aplique `FixedNetwork.Canonicalize` em todos os participantes antes de usar o valor na simulação. As políticas de precisão, arredondamento, overflow e matemática fazem parte da compatibilidade; backend de execução/multiplicação não altera o contrato numérico.

## Contrato v1 e manutenção

Philox calcula blocos de quatro `uint` com counter `(blockLow, blockHigh, sequenceLow, sequenceHigh)` e key `(keyLow, keyHigh)`, por dez rounds. A chave de stream é `Mix64(seed ^ Mix64(streamId))`; eventos usam `Mix64(seed ^ Mix64(streamId) ^ 1)` e `SequenceId = eventId`. `Mix64` é o finalizador SplitMix64. A ordem das quatro palavras, a montagem de `ulong`, o consumo e os samplers fazem parte do contrato v1.

Alterar algoritmo, ordem de palavras, redução, transformação geométrica ou consumo exige uma versão explícita e novos fixtures revisados. Não regenere referências para esconder uma divergência. Os wrappers públicos são reproduzíveis por `Tools/FixedPoint/generate_random_api.py`; `generate_random_reference.py` copia o corpus congelado para o validador de players, sem atualizar seus hashes.

As referências, plataformas realmente executadas, benchmarks e comandos de reprodução estão em [VALIDATION.md](VALIDATION.md). Philox segue [Random123](https://github.com/DEShawResearch/random123) e sua licença BSD de três cláusulas está em [Random123-LICENSE.txt](Random123-LICENSE.txt); o finalizador [SplitMix64](https://prng.di.unimi.it/splitmix64.c) é de domínio público.
