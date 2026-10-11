# Revalidação de casos comuns, limites e entradas incomuns

Rodada adicional de 11/10/2026, após a revisão registrada em [REVIEW.md](REVIEW.md). O escopo é escalar: `Fixed32`, `UFixed32`, `Fixed64`, `UFixed64`, seus kernels e codecs. Foram acrescentados **28 casos NUnit**: 27 disponíveis em standalone e um adicional que exige Burst nativo no Editor.

## Problemas encontrados e corrigidos

O parser managed retirava sinais e símbolos monetários antes de verificar sua posição. Isso aceitava entradas inválidas como `- CUR12` e rejeitava formas válidas como `CUR(12)` e `(12)CUR`. Também confundia separadores numéricos/monetários, espaços de agrupamento de culturas e NULs terminais. A busca antecipada por `E` interpretava como expoente um separador decimal personalizado com esse nome. O parser agora consome os tokens em sua ordem original, respeitando `NumberStyles` e `NumberFormatInfo`, sem converter a mantissa ou o expoente por float/double.

Nomes especiais também tinham diferenças: sinais personalizados eram sensíveis a maiúsculas, a concatenação de sinal negativo e símbolo positivo gerava um infinito não configurado, e símbolos coincidentes escolhiam NaN antes de Infinity. Os nomes literais agora têm prioridade definida: +Infinity, −Infinity, NaN. O sinal positivo permite +Infinity/NaN; o negativo permite NaN, enquanto −Infinity precisa coincidir com seu símbolo completo. Números finitos são reconhecidos antes desses nomes. As regras são verificadas contra o [parser oficial do .NET 8](https://raw.githubusercontent.com/dotnet/runtime/v8.0.0/src/libraries/System.Private.CoreLib/src/System/Number.Parsing.cs) e aplicadas explicitamente em Mono e IL2CPP. Texto vazio e `null` continuam inválidos.

A matriz ampliada de moeda/estilos reproduziu **1.536 divergências** antes da correção. A matriz de símbolos personalizados reproduziu **288 divergências**. As fixtures finais passaram sem divergências. As mudanças de produção desta rodada estão em `Internal/FixedManaged.cs`; os kernels numéricos mantêm as assinaturas anteriores.

## Cobertura acrescentada

| Fixture | Verificação |
|---|---|
| `FixedManagedEdgeTests` — 9 casos | 64.488 comparações de gramática finita com `decimal.TryParse`, todos os 512 conjuntos válidos de flags numéricos, 216 casos de nomes especiais e 672 casos de sinais/símbolos personalizados; round-trip dos quatro tipos e conversões decimal com oráculo BigInteger |
| `FixedBinaryMathEdgeTests` — 6 casos | Pow, Log com base e Atan2 contra **117.585 referências por perfil**, nos quatro tipos, todas as 58 famílias de precisão e cinco arredondamentos; domínio, NaN, infinitos e zeros assinados |
| `FixedScalarBoundaryTests` — 12 casos | ScaleB até `int.MinValue`/`int.MaxValue`, deslocamentos largos, empates de Round, limites de precisão e overflow; Repeat/PingPong/DeltaAngle contra módulo euclidiano independente, incluindo unsigned acima de `long.MaxValue` |
| `FixedBinaryMathBurstTests` — 1 caso | **235.170 comparações** exatas de raw/status entre managed e Burst nativo, com os dois perfis e os três modos aritméticos; `BurstDiscard` detecta fallback managed |

`Tools/FixedPoint/generate_binary_math_reference.py` usa mpmath 1.3.0 com **200 dígitos decimais**, entradas racionais e semente fixa. Potências de expoente inteiro usam `Fraction` exata. Isso evita que o arredondamento do próprio oráculo classifique incorretamente `MaxValue^1` como overflow. O gerador não importa a biblioteca de produção. As comparações de referência permitem até 1 raw em Accurate e 4 raw em Fast, verificam a classe especial e os status, exceto `Inexact`; a comparação managed/Burst verifica todos os bits de status, incluindo `Inexact`.

Houve duas correções no teste de nomes especiais. O `double.TryParse` do Mono apresentou 57 diferenças em relação à gramática do .NET 8, portanto a expectativa passou a ser explícita e é conferida adicionalmente contra o .NET 8 em standalone. Além disso, o Mono pt-BR usa `+Infinito`/`-Infinito`: sintetizar `-+Infinito` não cria um símbolo válido. O teste agora consulta o símbolo negativo configurado. Essas falhas de expectativa e os falsos overflows do oráculo foram registrados separadamente dos defeitos de produção.

## Resultados finais

| Ambiente | Resultado |
|---|---|
| .NET 8.0.29, Windows x64 | **81 testes escalares passaram**; a execução completa contém 435 testes, incluindo 354 de Random/vetores desenvolvidos em outras tarefas |
| .NET 8.0.31, Linux ARM64 emulado | Mesmo assembly standalone; **81 testes escalares e os 354 adicionais passaram** |
| Unity 6000.3.20f1, Mono Editor, Windows x64 | **87 testes passaram**, incluindo os jobs Burst 1.8.29 nativos |
| Player Mono Windows x64 | **47.560 casos numéricos nativos**, API pública e assinaturas passaram; **716 casos managed de parsing/formatting passaram** |
| Player IL2CPP Windows x64 | Mesmas contagens, raws, status e assinaturas de Mono |
| Android ARM64 IL2CPP/Burst | APK recompilado; execução em dispositivo continua pendente, conforme aceite do usuário |

Os players e o Editor usam uma cópia isolada dos fontes escalares. A cena não salva do projeto principal foi preservada. O manifesto `scalar-source-sha256.json` registra cada fonte compilada e sua correspondência com o pacote. O harness numérico compartilhado no pacote recebeu chamadas vetoriais em outra tarefa; o snapshot desta rodada mantém o harness exclusivamente escalar. Os resultados completos standalone incluem os testes paralelos, mas não representam uma revisão independente das novas APIs Random/vetoriais.

O corpus numérico continua na versão 1, com 20.880 casos e 94 campos por caso. A assinatura de entrada permanece `14675807239874021106`, a de saída `6622938699589000362` e a completa dos players `11024892444286813640`. A comparação byte a byte dos CSVs da revisão anterior está preservada; esta rodada repetiu o corpus e suas assinaturas, sem gerar novos CSVs completos.

Os XMLs finais, JSONs dos players, reproduções das falhas, status/proveniência dos builds e o consolidado `recheck-2026-10-11.json` estão em `Builds/FixedPointValidation/Reports/Recheck` do projeto anfitrião. A suíte ampla Windows/ARM64 usa o mesmo assembly identificado por SHA-256. Os XMLs registram as contagens e horários de cada execução.

## Reproduzir

```text
python Tools/FixedPoint/generate_binary_math_reference.py --dependency-dir <diretorio-com-mpmath-1.3.0>
dotnet run --project Standalone~/FixedPoint.Tests/LegendaryTools.FixedPoint.Tests.csproj -- --workers=1 --result=<xml>
```

Para Editor e players, siga [VALIDATION.md](VALIDATION.md), usando uma cópia isolada atualizada dos testes escalares. O player oferece uma segunda execução opcional para a API managed:

```text
FixedPointValidation.exe -batchmode -nographics --fixed-point-managed-validation --fixed-point-managed-report=<json>
```

Essa execução deve reportar `Passed=true`, `Cases=716`, `Checksum=8252000883644710091` e `Error=""`. O caminho numérico continua com `--fixed-point-validation`, separadamente. Parsing/formatting são managed; não têm promessa de execução Burst ou de zero alocações.

Os casos amostrados e os oráculos não são uma prova exaustiva do domínio inteiro. A execução Android em dispositivo e a revisão externa Gemini permanecem limitações aceitas e documentadas em [REVIEW.md](REVIEW.md). A execução ARM64 emulada não substitui o player Android.
