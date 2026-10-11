# Fixed-point determinístico

`LegendaryTools.FixedPoint` representa `RawValue / 10^p` com uma escala decimal por família. Os quatro tipos são `readonly struct`, sem referências, com um único campo inteiro. A configuração pertence ao runtime e ao perfil negociado de rede.

Vetores de componentes `Fixed64`: [FixedVector2/FixedVector3, Burst, rede e Inspector](VECTORS.md), com [evidência de validação vetorial](VECTORS_VALIDATION.md).

[FixedRandom](Random/README.md) oferece geração determinística com esses quatro tipos, amostras espaciais, loot por pesos, jobs Burst, snapshots de 24 bytes e checkpoints compactos para rede/replay.

## Começar

```csharp
using LegendaryTools.FixedPoint;

// Uma vez, antes dos jobs, da simulação e da leitura de números.
FixedRuntime.Initialize(); // 3 casas em ambas as famílias

Fixed32 velocidade = 12;
Fixed32 tempo = Fixed32.FromRatio(1, 60);
Fixed32 distancia = velocidade * tempo;
Fixed64 raiz = FixedMath.Sqrt(Fixed64.FromInt(2));
UFixed64 contador = UFixed64.FromUInt(100);

bool ok = Fixed32.TryDivide(distancia, Fixed32.Zero,
    out Fixed32 resultado, out FixedStatus status);
// ok=false; resultado=+Infinity; status=DivisionByZero
```

Para escrever um literal decimal exato, use `FromDecimal(1.234m)`, `FromRatio(1234, 1000)` ou `Parse("1.234")`. `FromRaw(1234)` já está na escala configurada. `FromSingle` e `FromDouble` quantizam o valor binário efetivamente recebido; um literal `1.234f` pode estar acima ou abaixo do decimal escrito.

`Parse`/`TryParse` aceitam cultura e `NumberStyles`. Sinais, moeda e separadores respeitam os estilos escolhidos; nomes NaN/Infinity têm regras explícitas para conservar o comportamento entre .NET, Mono e IL2CPP. Texto vazio e `null` são inválidos. Os casos comuns, limites e símbolos personalizados estão documentados em [EDGE_CASE_VALIDATION.md](EDGE_CASE_VALIDATION.md).

```csharp
FixedRuntime.Initialize(new FixedSettings(
    decimalPlaces32: 3, decimalPlaces64: 6,
    rounding: FixedRounding.ToEven,
    overflow: FixedOverflow.Saturate,
    unsignedNegative: FixedUnsignedNegative.NaN,
    mathProfile: FixedMathProfile.Accurate));
```

Inicializar novamente com a mesma configuração é permitido. Uma configuração diferente retorna `ConfigurationFrozen` ou lança `InvalidOperationException`; a alteração exige um novo domínio/processo. Não inicialize enquanto outros threads/jobs acessam o runtime. `FixedSettings.Default` define os padrões; `default(FixedSettings)` é a configuração zero de uma struct C#, com precisão zero.

## Tipos, precisão e valores especiais

| Tipo | Campo | Bytes | Casas permitidas | Finito em raw |
|---|---|---:|---:|---|
| `Fixed32` | `int` | 4 | 0–9 | −2.147.483.646 … 2.147.483.645 |
| `UFixed32` | `uint` | 4 | 0–9 | 0 … 4.294.967.291 |
| `Fixed64` | `long` | 8 | 0–18 | −9.223.372.036.854.775.806 … 9.223.372.036.854.775.805 |
| `UFixed64` | `ulong` | 8 | 0–18 | 0 … 18.446.744.073.709.551.611 |

Divida os limites por `10^p` para obter o intervalo numérico. Com três casas, `Fixed32.MaxValue` vale `2147483.645`. Aumentar a precisão reduz o intervalo. Signed e unsigned da mesma largura compartilham a precisão. Conversões entre larguras ajustam a escala.

Todos os tipos incluem `NaN`, `PositiveInfinity`, `NegativeInfinity` e `NegativeZero`; o domínio **finito** de unsigned permanece não negativo. Há quatro códigos reservados por tipo:

| Código raw | Signed | Unsigned |
|---|---|---|
| NaN | mínimo nativo | máximo nativo |
| −Infinity | mínimo nativo + 1 | máximo nativo − 1 |
| +Infinity | máximo nativo | máximo nativo − 2 |
| −0 | máximo nativo − 1 | máximo nativo − 3 |

`Epsilon` é uma unidade raw, `10^-p`. Não há subnormais. `default(T)` é +0. `++`/`--` acrescentam/removem uma unidade numérica. Conversões entre os quatro tipos são explícitas. Conversões para inteiros truncam em direção a zero, saturam nos limites do destino e convertem NaN em zero; as variantes com status permitem detectar a perda/erro. `IConvertible` usa arredondamento para o inteiro mais próximo, com empates para par, como `Convert`.

`NaN == NaN` é falso; `NaN.Equals(NaN)` é verdadeiro. +0 e −0 são iguais e têm o mesmo hash. `CompareTo` ordena NaN, −Infinity, finitos, +Infinity. Operadores relacionais com NaN retornam falso. O hash usa somente bits definidos e é estável entre backends.

## Políticas

| Decisão | Padrão | Alternativas |
|---|---|---|
| Arredondamento | `ToEven` | Empate para longe de zero, truncar, +Infinity, −Infinity |
| Overflow | `Infinity` | `Saturate`, `NaN`, `WrapFinite` |
| Divisão de não zero por zero | `Infinity` | `Saturate`, `NaN` |
| Resultado unsigned negativo | `NaN` + `NegativeResult` | `SaturateZero`, `WrapFinite` |
| Matemática | `Accurate` | `Fast`, com mais guardas nos casos difíceis |
| Multiplicação intermediária | `Auto` | `Portable`, `Intrinsic` |
| Lotes Unity | `Auto` | `Managed`, `Burst` |

As operações finitas aplicam arredondamento antes das políticas de intervalo. `WrapFinite` usa módulo euclidiano do intervalo **finito**, sem gerar códigos reservados por acidente. A política unsigned atua no resultado final quantizado, permitindo intermediários negativos em `Lerp`, `FusedMultiplyAdd` e outras funções.

`FixedStatus.Inexact` sozinho é sucesso. As funções com `out FixedStatus`, os métodos `Try…` e os métodos `…Checked` permitem escolher como tratar erros. As versões `Checked`, parsing, formatting, `decimal`, `IConvertible`, boxing e sincronização da inicialização são managed. Aritmética, classificação, conversões IEEE, matemática e codecs por ponteiro são compatíveis com Burst.

## API matemática

`FixedMath` oferece overloads para os quatro tipos: `Abs`, `Ceiling`, `Floor`, `Truncate`, `Round`, `Sqrt`, `Cbrt`, `Sin`, `Cos`, `SinCos`, `Tan`, `Asin`, `Acos`, `Atan`, `Atan2`, `Sinh`, `Cosh`, `Tanh`, `Asinh`, `Acosh`, `Atanh`, `Exp`, `Log` (natural e base explícita), `Log2`, `Log10`, `Pow`, `Min`, `Max`, variantes `Number`/`Magnitude`, `Sign`, `CopySign`, `BitIncrement`, `BitDecrement`, `ScaleB`, `ILogB`, `IEEERemainder`, `FusedMultiplyAdd`, `ReciprocalEstimate` e `ReciprocalSqrtEstimate`.

Também inclui `Clamp`, `Clamp01`, `Lerp`, `LerpUnclamped`, `InverseLerp`, `MoveTowards`, `Repeat`, `PingPong`, `SmoothStep`, `DeltaAngle`, `LerpAngle`, `MoveTowardsAngle`, `DegreesToRadians`, `RadiansToDegrees` e `Approximately`. Trigonometria usa radianos; as funções de ângulo com nome `Angle` usam graus. `Approximately` usa tolerância absoluta de um raw por padrão; há overload com tolerância explícita. `Pi`, `Tau` e `E` pertencem a cada tipo e respeitam sua escala.

As raízes usam algoritmos inteiros. Trigonometria usa CORDIC com redução de argumento; exponenciais/logaritmos usam normalização e séries. A precisão interna tem 256 bits fracionários em Accurate e 128 em Fast, com fallback de guarda para argumentos grandes e tangentes condicionadas. As tabelas inteiras têm gerador reproduzível e versão. FMA faz uma única quantização após o produto e a soma, inclusive quando o produto isolado estouraria.

O alvo de erro para um resultado finito representável é até um raw em Accurate e quatro em Fast. Esses limites foram verificados contra 13.340 referências geradas com 160 dígitos decimais, por perfil, incluindo limites de 64 bits, precisões 0/3/9/18 e proximidades de singularidades. Isso é evidência de teste, não uma prova de todos os inputs.

`float` e fixed-point têm intervalos diferentes. Nenhum número fixo de bits combina simultaneamente todo o intervalo de `float`, passo decimal constante e precisão arbitrária. Em transcendentes cujo resultado ultrapassa a capacidade do acumulador de guarda, `Infinity`/`Saturate`/`NaN` seguem a política; `WrapFinite` retorna NaN com `Overflow` em vez de inventar um resíduo. A aritmética racional, conversões IEEE, FMA e `ScaleB` preservam o módulo de overflow completo.

## Managed, Burst e standalone

```csharp
// NativeArrays são fornecidos pelo chamador. A execução é síncrona.
FixedBatch.Execute(input, output, FixedBatchOperation.Sin,
    mode: FixedExecutionMode.Auto, statuses: statuses);
```

`Auto` usa Burst quando disponível e managed quando Burst está desativado. `Burst` explícito falha se o backend estiver indisponível. Os arrays precisam ter comprimentos iguais; operações binárias recebem `b`, e FMA recebe `b` e `c`. As operações têm o mesmo contrato escalar. Use arrays distintos nos jobs; a execução managed permite operar no mesmo array.

No Unity, os assemblies são `LegendaryTools.FixedPoint` e `LegendaryTools.FixedPoint.Unity`, com Burst/Collections. A biblioteca standalone em `Standalone~/FixedPoint` usa .NET Standard 2.1, define `FIXEDPOINT_STANDALONE` e compila os mesmos fontes sem referências ao Unity. O til impede o Unity de importar projetos/binários auxiliares. O teste standalone usa .NET 8.

## Rede

Negocie um `FixedNetworkProfile` por campo/schema antes de enviar valores. O descritor de 40 bytes contém versão, tipo, políticas numéricas, precisão e limites; o valor individual não carrega configuração.

| Codec | Custo | Uso |
|---|---|---|
| `FixedWidth` | 4/8 bytes, little-endian | Distribuição ampla e tamanho constante |
| `VarInt` (padrão) | 1–5/1–10 bytes | Pequenos valores absolutos; signed usa ZigZag |
| `BitPacked` | Bits calculados a partir da faixa + 4 especiais | Faixa conhecida; vários campos compartilham bytes |

VarInt pode ser maior que o formato fixo nos extremos. Os códigos compactos são 0=NaN, 1=−Infinity, 2=+Infinity, 3=−0; números finitos começam em 4. Bits são escritos do menos significativo ao mais significativo. `BitPacked` recebe `LowerRaw`/`UpperRaw` na **escala de rede**, valida a faixa e rejeita valores externos. Limites unsigned de 64 bits permanecem unsigned.

```csharp
var profile = new FixedNetworkProfile(FixedRuntime.Settings,
    FixedKind.Signed32, FixedCodec.VarInt);

int offset = 0;
FixedNetwork.TryWrite(buffer.AsSpan(), ref offset, distancia, profile, out status);
offset = 0;
FixedNetwork.TryRead(buffer.AsSpan(), ref offset, profile, out Fixed32 decoded, out status);
```

Para bit packing contínuo e Burst, use `FixedBufferWriter`/`FixedBufferReader` com ponteiros de buffers fornecidos pelo chamador; preserve `BitPosition` entre os campos. Os adapters de `Span` com offset em bytes alinham cada valor à próxima fronteira de byte. Falhas de leitura/escrita preservam o cursor; não há escrita parcial em buffers insuficientes. VarInts redundantes, truncados, grandes demais, códigos fora da faixa e descritores incompatíveis são rejeitados.

Reduzir `WireDecimalPlaces` exige `AllowLossy=true`. Use `FixedNetwork.Canonicalize` antes da simulação em **todos** os participantes; quantizar somente no envio mudaria o estado reproduzido remotamente. O perfil deve corresponder às políticas congeladas do runtime. Backend de execução/multiplicação não altera compatibilidade numérica.

## Verificação e reprodução

Execute a assembly `LegendaryTools.FixedPoint.Tests` em EditMode para validar aritmética, BigInteger, referências matemáticas, IEEE, rede, alocações e jobs Burst nativos. `[BurstDiscard]` confirma a execução nativa e impede que fallback managed passe despercebido. O teste de alocações cobre os kernels numéricos e codecs por ponteiro após aquecimento; parsing/formatting são managed e podem alocar.

```text
dotnet run --project Standalone~/FixedPoint.Tests/LegendaryTools.FixedPoint.Tests.csproj -c Release -- --noresult --workers=0
dotnet run --project Standalone~/FixedPoint.Benchmarks/LegendaryTools.FixedPoint.Benchmarks.csproj -c Release
```

Direcione artefatos de build para fora de `Assets` usando `--artifacts-path`. `Tools/FixedPoint` contém os geradores das fachadas e referências. Só os geradores de matemática precisam de `mpmath==1.3.0`; consumidores e testes usam os fontes/dados gerados que acompanham o pacote.

Use `Tools/FixedPoint/create_validation_project.py` para criar um projeto temporário separado e executar os builders Mono, IL2CPP e Android ARM64 sem interferir nas cenas do projeto de trabalho. Os passos e resultados estão em [VALIDATION.md](VALIDATION.md). No Windows, o player executa validação com `--fixed-point-validation --fixed-point-report=<arquivo>`; no APK de validação Android, ela inicia automaticamente. O assembly de validação é excluído de builds comuns.

O contrato usa APIs restritas do Burst, conforme sua [documentação de suporte a tipos](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/csharp-type-support.html). As funções matemáticas foram implementadas em inteiros para evitar a variação entre plataformas descrita para [MathF.Log](https://learn.microsoft.com/en-us/dotnet/api/system.mathf.log).
