# FixedVector2 e FixedVector3

Vetores mutáveis de duas/três componentes `Fixed64`, no namespace `LegendaryTools.FixedPoint`. São structs unmanaged com layout sequencial: 16 e 24 bytes. O núcleo é independente de Unity, compila para .NET Standard 2.1 e usa os mesmos kernels inteiros em managed e Burst. Os adaptadores de Unity ficam na assembly `LegendaryTools.FixedPoint.Unity`.

## Inicialização e simulação

Inicialize uma vez antes de iniciar threads/jobs. Todos os participantes da simulação devem usar a mesma versão e configuração numérica, incluindo precisão, arredondamento, overflow e perfil matemático. A configuração fica congelada pelo contrato existente de `FixedRuntime`.

```csharp
using LegendaryTools.FixedPoint;

FixedRuntime.Initialize(new FixedSettings(decimalPlaces32: 3, decimalPlaces64: 6));

FixedVector3 position = new FixedVector3(1, 2, 3);
FixedVector3 direction = new FixedVector3(3, 0, 4).normalized;
Fixed64 speed = Fixed64.FromRatio(5, 2);
Fixed64 tick = Fixed64.FromRatio(1, 60);
position += direction * speed * tick;

FixedVector3 velocity = default;
position = FixedVector3.SmoothDamp(position, FixedVector3.zero, ref velocity,
    smoothTime: Fixed64.FromRatio(1, 2), maxSpeed: 10, deltaTime: tick,
    out FixedStatus status);
// Inexact sozinho é sucesso. As demais falhas devem ser tratadas pela simulação.
```

`deltaTime` é obrigatório; nenhuma operação consulta `Time`, frame rate ou estado de cena. `FromRaw` interpreta coordenadas na precisão **atual** de `Fixed64`. Literais fracionários devem usar `FromRatio`, `FromDecimal` ou parsing managed. Conversões IEEE preservam e quantizam o valor binário efetivamente recebido.

## API

| Disponível nos dois tipos | Uso |
|---|---|
| `x`, `y` e `z` em Vector3; construtores, `Set`, indexador | Componentes públicas `Fixed64`, mutação semelhante à API Unity |
| `TryGetComponent`, `TrySetComponent` | Acesso com índice validado sem exceções |
| `zero`, `one`, `right`, `left`, `up`, `down`, infinities e `NaN` | Constantes calculadas na escala do runtime; Vector3 também tem `forward` e `back` |
| `magnitude`, `sqrMagnitude`, `normalized`, `Normalize` | Normas e normalização; variantes com status |
| `+`, `-`, `*`/`/` por escalar, `Scale` | Aritmética de componentes; Vector2 também admite produto/divisão componente a componente |
| `Dot`, `Distance`, `Min`, `Max`, `Reflect`, `ClampMagnitude` | Geometria e limites |
| `Lerp`, `LerpUnclamped`, `MoveTowards`, `SmoothDamp` | Interpolação e movimento com tempo explícito |
| `Angle`, `SignedAngle`, `Approximately`, `IsFinite`, `IsNaN` | Consultas; Vector3 SignedAngle exige eixo |
| `Equals`, `GetHashCode`, `ToString`/`IFormattable` | Igualdade/hash definidos; formatação managed |

Vector2 acrescenta `Perpendicular`. Vector3 acrescenta `Cross`, `Project`, `ProjectOnPlane`, `Slerp`, `SlerpUnclamped`, `RotateTowards` e `OrthoNormalize` com dois ou três argumentos `ref`.

As operações numéricas principais oferecem overloads `out FixedStatus`. `TryNormalize`, `TryDivide`, `TrySmoothDamp` e `TryOrthoNormalize` permitem tratar falhas. Os argumentos `ref` de SmoothDamp/OrthoNormalize só são atualizados quando todas as saídas têm status de sucesso.

Vector2 → Vector3 é conversão implícita, preenchendo Z com zero. Vector3 → Vector2 é explícita e descarta Z. Conversões para os vetores IEEE de Unity são chamadas explícitas de adaptadores.

### Igualdade, precisão e degenerações

`==` é igualdade numérica exata por componente, conforme `Fixed64`: +0/−0 são iguais, NaN não é igual a si mesmo. `Equals` considera NaN igual a NaN e o hash segue essa semântica. `Approximately` é uma comparação separada, com tolerância absoluta de um raw por padrão ou valor fornecido. O hash é estável entre backends para os mesmos valores; não use hashes para substituir comparações de estado.

Dot/Cross, normas, normalização, projeção e reflexão usam intermediários inteiros largos. Produtos que excederiam `Fixed64` podem cancelar antes da quantização final. Normalize divide pela norma não quantizada, permitindo normalizar um vetor cujo único componente vale um raw. Os kernels não usam float/double para resolver geometria.

| Caso | Resultado definido |
|---|---|
| Normalize de zero | Zero |
| Angle/SignedAngle com um vetor zero | Zero |
| Project sobre normal zero | Zero; ProjectOnPlane devolve o vetor original |
| Reflect | Assume normal unitária, como Unity; não normaliza a normal automaticamente |
| Lerp/Slerp | T limitado a [0,1]; as variantes Unclamped permitem extrapolar |
| Slerp com um dos vetores zero | Lerp entre as entradas |
| Slerp/RotateTowards entre direções opostas | Perpendicular obtida por `direção × eixo` de menor componente absoluta; empates X, depois Y, depois Z |
| OrthoNormalize com normal zero | Normal `right`, depois perpendicular determinística se a tangente for degenerada |
| OrthoNormalize com três vetores | Preserva o sinal da projeção do binormal original quando ela é não zero |
| MoveTowards com delta negativo | Afasta-se do destino; delta zero preserva posição salvo entradas coincidentes |
| RotateTowards com delta angular negativo | Afasta-se até direções opostas; delta de magnitude negativo é inválido |
| SmoothDamp com deltaTime zero | Preserva posição e velocidade |
| SmoothDamp com deltaTime/maxSpeed negativo | InvalidDomain; velocidade preservada |
| SmoothDamp com maxSpeed +Infinity | Sem limite de velocidade |
| SmoothDamp com smoothTime pequeno/negativo | Mínimo `max(Epsilon, 0.0001)` |

Operações geométricas que exigem direção finita rejeitam NaN/infinities com `InvalidDomain`. Aritmética componente a componente e Dot seguem os especiais escalares. Magnitude de um componente infinito é +Infinity, salvo presença de NaN. Refine o tratamento usando os overloads com status.

Angle/SignedAngle retornam **graus**; RotateTowards recebe **radianos**. A escala também limita a faixa: p=18 só permite valores finitos de aproximadamente ±9,22, portanto 90°/180° estouram. Para representar toda a faixa de Angle, use p≤16. Precisão baixa também limita a qualidade de uma direção quantizada; uma normalização não garante norma exatamente um depois de arredondar seus componentes.

## Burst e NativeArray

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

[BurstCompile]
public struct MoveFixedJob : IJobParallelFor
{
    public NativeArray<FixedVector3> Positions;
    public FixedVector3 Velocity;
    public Fixed64 DeltaTime;
    public void Execute(int index) => Positions[index] += Velocity * DeltaTime;
}
```

Inicialize o runtime no código managed antes de agendar. Use `TryGetComponent`/`TrySetComponent` para índices não confiáveis em jobs. Parsing, formatação, interfaces que causam boxing e os acessores managed dos wrappers serializados pertencem à camada managed. O núcleo e os codecs por ponteiro não alocam heap managed após aquecimento.

## Rede

`FixedVectorNetworkProfile` reúne dois ou três `FixedNetworkProfile` de `Signed64`. Os eixos compartilham configuração numérica e codec; podem ter limites e precisões de transmissão distintos. O descriptor versionado informa dimensão e perfis uma vez no handshake. Cada valor contém somente X/Y[/Z], nessa ordem.

```csharp
// Exemplo: simulação inicializada com seis casas; snapshot com três casas.
var axisProfile = new FixedNetworkProfile(FixedRuntime.Settings, FixedKind.Signed64,
    FixedCodec.BitPacked, wireDecimalPlaces: 3, allowLossy: true,
    lowerRaw: unchecked((ulong)-1_024_000L), upperRaw: 1_024_000UL);
var profile = new FixedVectorNetworkProfile(axisProfile, dimensions: 3);

Span<byte> packet = stackalloc byte[30];
int offset = 0;
bool written = FixedVectorNetwork.TryWrite(packet, ref offset, position, profile, out status);
int readOffset = 0;
bool read = FixedVectorNetwork.TryRead(packet[..offset], ref readOffset, profile,
    out FixedVector3 received, out status);
```

Limites são valores raw **na precisão de transmissão**. Esse exemplo cobre [−1024,+1024], usa 21 bits por eixo e 63 bits/8 bytes para Vector3, incluindo os códigos especiais. Com ToEven, a quantização de seis para três casas tem erro de até 0,0005 por componente. `AllowLossy=false` é o padrão; reduzir precisão exige opt-in.

| Codec | Vector2 | Vector3 |
|---|---:|---:|
| FixedWidth little-endian, sem perda | 16 bytes | 24 bytes |
| VarInt ZigZag | 2–20 bytes | 3–30 bytes |
| BitPacked | Soma dos bits dos eixos | Soma dos bits dos eixos |

Os overloads Span alinham ao byte após cada **vetor**, preenchendo padding com zero. Os overloads por ponteiro com `FixedBufferWriter/Reader` preservam um cursor contínuo em bits: BitPacked permite concatenar vetores/campos sem padding intermediário. VarInt/FixedWidth e descriptors exigem início alinhado ao byte.

`TryWrite` prepara todo o vetor antes de tocar o destino; falha de faixa, perfil ou espaço não altera bytes nem cursor. `TryRead` só avança o cursor após decodificar todos os eixos; falha devolve vetor NaN. Os descriptors também são atômicos: 88 bytes para Vector2, 128 para Vector3, cabeçalho `FPV1`, versão 1, dimensão e os descriptors escalares existentes.

Use `TryWriteDescriptor`, `TryReadDescriptor` e `TryNegotiate` no handshake. Perfis incompatíveis falham; serializers rejeitam coordenadas fora da faixa em vez de aplicar clamp silencioso. `Canonicalize` quantiza para a grade de rede e volta à escala da simulação, sem modificar a entrada; teste o status e depois os limites do perfil ao serializar. Pode ser usado explicitamente como parte da simulação ou apenas no snapshot, conforme o protocolo escolhido. A biblioteca não define transporte, rollback nem sincronização de ticks.

## Unity e Inspector

```csharp
using LegendaryTools.FixedPoint;
using UnityEngine;

public class FixedMovementSettings : MonoBehaviour
{
    [SerializeField] private SerializedFixedVector3 initialPosition;
    private FixedVector3 position;
    private void Start() => position = initialPosition.Value; // Runtime já inicializado.
    private void LateUpdate() => transform.position = position.ToUnityVector3();
}
```

Os wrappers `SerializedFixedVector2/3` armazenam inteiros raw e precisão declarada. Isso permite persistir corretamente valores de `Fixed64`, cujo campo readonly não é serializado pelo serializer de Unity. O valor default do wrapper declara três casas. `Value` lê e reescala para a configuração atual; `TryGetValue`/`TrySetValue` oferecem status. O construtor `(raw..., decimalPlaces)` não exige inicializar o runtime.

Os drawers IMGUI e UI Toolkit editam texto decimal com ponto, sem passar por float/double. Oferecem seleção de precisão, múltiplos objetos, Undo e overrides de prefab. Alterar a precisão reescala todas as coordenadas atomicamente e mostra erros; o drawer não inicializa nem congela o runtime. `TrySetDecimalPlaces` oferece o mesmo ajuste no código managed.

`ToUnityVector2/3` e `ToFixedVector2/3` são adaptadores explícitos; a entrada IEEE quantiza segundo o contrato escalar. Reserve essa fronteira para renderização/importação; produzir decisões da simulação a partir de coordenadas IEEE perde o contrato de execução inteira.

## Verificação e custo

Veja [VECTORS_VALIDATION.md](VECTORS_VALIDATION.md) para resultados, checksum, limitações e comandos de reprodução. Os testes incluem referências independentes com 160 dígitos, políticas de arredondamento, valores extremos/degenerados, bytes de rede esperados, atomicidade, alocações, persistência e execução Burst nativa.

O perfil Accurate usa 256 bits fracionários internos para geometria trigonométrica, Fast usa 128. As referências selecionadas verificam até um raw de erro em Accurate e quatro em Fast; essa amostragem não prova todo o domínio. A aritmética inteira larga prioriza comportamento robusto: operações como Slerp custam mais que soma de componentes. Meça os jobs reais no dispositivo alvo.
