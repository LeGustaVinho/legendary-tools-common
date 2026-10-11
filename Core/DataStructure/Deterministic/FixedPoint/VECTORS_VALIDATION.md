# Evidência de validação dos vetores

Revalidação ampliada em 11/10/2026. Foram acrescentados 43 casos NUnit managed e dois casos de execução paralela Burst. Consulte [VECTORS.md](VECTORS.md) para API, semântica, rede e Inspector. Esta evidência é específica de FixedVector2/FixedVector3; a validação escalar existente está em [VALIDATION.md](VALIDATION.md).

## Ambientes e verificações

| Ambiente | Evidência |
|---|---|
| .NET 8.0.29, Windows x64, Release | 59 testes de vetores passaram, incluindo oráculos, codecs, retomada de simulação e zero alocação |
| .NET 8.0.31, Linux ARM64 emulado via Docker | Os mesmos 59 testes passaram; entradas, saídas e ticks coincidiram byte a byte com x64 |
| Unity 6000.3.20f1 / Burst 1.8.29, Editor Windows x64 | 68 testes passaram; inclui jobs paralelos em ordem invertida e batches de 1/17 |
| Player Mono, Windows x64 | 115.220 casos e 36.480 ticks comparados diretamente com Burst nativo; API pública/layout/rede passaram |
| Player IL2CPP, Windows x64 | Os mesmos casos e ticks passaram; trace completo idêntico a Mono/.NET |
| Android ARM64 / IL2CPP + Burst AOT | APK compilado; execução pendente em dispositivo ARM64 |

A suíte standalone completa do workspace também passou: 543 testes, zero falhas. Esse total inclui outras funcionalidades do pacote; os 59 testes de vetores estão identificados separadamente nos relatórios. Nenhuma alteração do núcleo vetorial foi necessária nesta ampliação; as falhas encontradas durante a construção dos testes eram do próprio oráculo/configuração de validação e foram corrigidas antes desta execução final.

O `BurstDiscard` marca o caminho managed. Os testes exigem marca 1 na execução direta e marca 0 no job agendado: habilitar Burst ou agendar um job sozinho não comprova execução nativa. Os players também verificam o tamanho das structs e usam a fachada pública de geometria/codec, além dos kernels internos.

O APK contém o mesmo harness e inicia a validação automaticamente. Nenhum dispositivo foi listado por `adb devices`; não foi possível verificar o runtime IL2CPP/Burst Android ARM64. A execução managed ARM64 emulada e o build AOT não substituem essa verificação. Essa pendência é mantida conforme o plano aprovado.

## Corpus e referências independentes

`Tests/FixedPoint/FixedVectorReplayCorpus.cs` é a fonte compartilhada. `Tools/FixedPoint/sync_vector_replay_corpus.py` gera a cópia da assembly opcional dos players; `--check` detecta uma cópia desatualizada.

O corpus versão 2 contém 115.220 casos. Preserva os 9.200 cenários anteriores e acrescenta 104.880 casos com entradas de 64 bits variadas: todas as 19 precisões, cinco arredondamentos, quatro políticas de overflow, Accurate/Fast, Auto/Portable/Intrinsic, ambas as dimensões e 23 operações. As entradas vêm de um gerador inteiro com seed fixa; não dependem de System.Random nem de estado externo. Os parâmetros incluem deltas negativos, zero e extrapolação.

Outros 1.140 casos executam simulações de 32 ticks, totalizando 36.480 estados intermediários. Combinam SmoothDamp, MoveTowards, limites de magnitude e snapshots com codecs/precisão de rede alternados. Os testes retomam cada sequência no tick 16 a partir de posição e velocidade salvas, exigindo igualdade de todos os campos subsequentes. Jobs IJobParallelFor também comparam cada tick com managed, invertendo a ordem de submissão e variando o tamanho do batch.

Managed/Burst compara **diretamente** cada coordenada raw de saída, velocidade, escalar, status numérico, status de escrita/leitura, ambos os cursores, vetor decodificado e até os 30 bytes do payload. A assinatura das entradas detecta diferenças no gerador entre runtimes; a assinatura das saídas complementa a comparação campo a campo.

| Assinatura | Valor |
|---|---|
| Versão | `2` |
| Casos | `115220` |
| Checksum das entradas | `5598736731664205017` |
| Checksum das saídas e payloads | `9703503698145310419` |
| Ticks comparados diretamente | `36480` |
| SHA-256 do trace completo .NET x64/ARM64/Mono/IL2CPP | `a976374e4e682ea75784bef39a07c08c6de0df2d5b49d2d70d25bddff1481473` |

Os quatro CSVs de 50.798.231 bytes coincidiram na comparação direta byte a byte: 151.700 linhas, com 39 campos por linha. Cada linha identifica caso/tick, 17 campos de entrada/configuração e 20 campos de saída. O hash não substitui essa comparação. `compare_vector_traces.py` também verifica estrutura, ordem e contagens para rejeitar um trace truncado ou vazio.

`FixedVectorReferenceTests` compara 760 referências independentes, em dois perfis (1.520 avaliações). O gerador usa mpmath 1.3.0 com 160 dígitos, razões decimais exatas e arredondamento final para par. Inclui entradas pequenas, extremos antiparalelos e direções quase colineares. O limite verificado é um raw em Accurate e quatro em Fast para resultados finitos representáveis. Os casos não constituem prova exaustiva do domínio.

`FixedVectorTests` compara Dot e norma com BigInteger e limites de raiz inteira em 1.000 amostras, atravessando todas as precisões/arredondamentos selecionados e ambos os backends. Verifica os casos degenerados definidos, igualdade/hash, mutação, indexação, graus/radianos e atualização atômica dos argumentos `ref`.

`FixedVectorOracleTests` acrescenta 9.120 configurações de entrada em todas as precisões, todos os arredondamentos/overflows, Portable/Intrinsic e ambas as dimensões. Compara Dot, normas, normalização, reflexão, Cross e projeções contra fórmulas racionais BigInteger e raiz inteira independentes; verifica também os status. Há casos de cancelamento, limites finitos assimétricos, WrapFinite e raízes com sinal.

`FixedVectorContractTests` verifica explicitamente bits de zero negativo, extrapolação, rotação com delta negativo, overshoot de SmoothDamp, velocidade máxima zero, tempo mínimo, todos os parâmetros inválidos selecionados, preservação de refs e comportamento de kernels não inicializados.

Os codecs têm bytes esperados literais para VarInt, FixedWidth little-endian e BitPacked LSB-first; falhas no último eixo deixam destino/cursor intactos. Há testes de payload truncado/malformado, buffer insuficiente, especiais, perfis incompatíveis, descriptors, precisão com perda explícita, canonicalização idempotente e vetores concatenados em um cursor de bits contínuo.

`FixedVectorNetworkBoundaryTests` verifica cada truncamento de payload VarInt/FixedWidth e descriptor em cursor não zero, offsets desalinhados, os oito offsets de BitPacked com precisões distintas por eixo, códigos não utilizados em cada coordenada e 450 combinações de especiais com preservação dos raws nos três codecs.

A integração Unity verifica conversões explícitas, wrappers com precisão independente do runtime, entrada decimal de 18 casas sem float/double, edição de múltiplos objetos, Undo, persistência/reload de asset, override de prefab e criação de controles UI Toolkit. Os drawers IMGUI e UI Toolkit usam a mesma função de edição validada.

## Alocações e custo

A avaliação completa do corpus, incluindo codecs por ponteiro, reportou zero alocação managed após aquecimento. O microbenchmark também reportou zero bytes managed/op em todas as operações medidas. Parsing/formatting e edição no Inspector são managed e podem alocar.

Medição local Release, .NET 8.0.29, Windows x64, Intel Core i9-13900KF, precisão três casas, Accurate:

| Vector3 | ns/op |
|---|---:|
| Add | 27,8 |
| Dot | 2.551,3 |
| Normalize | 10.001,3 |
| Slerp | 336.297,2 |
| SmoothDamp | 38.979,3 |
| VarInt write/read | 519,8 |

Esses tempos incluem loop/checksum e concorrência com outras atividades do Editor. Não são medições de Burst nem orçamento garantido por frame. As operações com intermediários largos e trigonometria têm custo maior; a escolha do perfil deve ser medida com os jobs reais no alvo.

## Reprodução

Execute os comandos a partir da raiz do pacote:

```text
dotnet run --project Standalone~/FixedPoint.Tests/LegendaryTools.FixedPoint.Tests.csproj -c Release -- --where="class =~ FixedVector" --workers=0
dotnet run --project Standalone~/FixedPoint.Benchmarks/LegendaryTools.FixedPoint.Benchmarks.csproj -c Release
python Tools/FixedPoint/sync_vector_replay_corpus.py --check
python Tools/FixedPoint/create_validation_project.py <diretorio-temporario-novo-fora-do-pacote>
unity run <diretorio-temporario> -- -executeMethod LegendaryTools.FixedPoint.Editor.FixedValidationBuild.BuildMonoBatch -nographics -logFile <log>
unity run <diretorio-temporario> -- -executeMethod LegendaryTools.FixedPoint.Editor.FixedValidationBuild.BuildIl2CppBatch -nographics -logFile <log>
unity run <diretorio-temporario> -- -executeMethod LegendaryTools.FixedPoint.Editor.FixedValidationBuild.BuildAndroidBatch -nographics -logFile <log>
FixedPointValidation.exe -batchmode -nographics --fixed-point-validation --fixed-point-report=<json>
```

No Editor aberto: `unity command run_tests --mode editor --filter FixedVector --async_tests true --format json`, seguido de `unity command test_status --format json`. Confirme que o resultado corresponde ao filtro solicitado antes de salvar o relatório.

Um relatório válido de player exige `Passed=true`, `VectorPublicApiPassed=true`, `VectorCases=NativeVectorCases=115220`, `VectorReplayVersion=2`, `VectorChecksum=9703503698145310419`, `VectorInputChecksum=5598736731664205017` e `NativeVectorSequenceTicks=36480`. A contagem escalar existente `Cases=47560` não inclui `VectorCases`; as contagens são separadas. O APK escreve `fixed-point-validation.json` no diretório persistente do app.

Para exportar todos os campos e ticks:

```text
dotnet run --project Standalone~/FixedPoint.Tests/LegendaryTools.FixedPoint.Tests.csproj -c Release -- --vector-replay-trace=<dotnet.csv>
FixedPointValidation.exe -batchmode -nographics --fixed-point-validation --fixed-point-report=<json> --fixed-vector-replay-trace=<player.csv>
python Tools/FixedPoint/compare_vector_traces.py <dotnet.csv> <mono.csv> <il2cpp.csv> <arm64.csv> --output <comparacao.json>
```

Os diretórios de destino devem existir. Exportar o trace é managed; a execução dos ticks exportados pelo player foi previamente confirmada como Burst nativo.

Os geradores de API (`generate_vector_api.py`, `generate_vector_unity_api.py`) usam somente a biblioteca padrão de Python. O gerador de referências (`generate_vector_reference.py`) exige mpmath 1.3.0; aceita `--dependency-dir` para uma instalação local isolada. Os fontes gerados estão incluídos no pacote e não precisam de Python para compilar. A sincronização do corpus foi conferida após a execução; o conteúdo executável dos fontes do snapshot também foi comparado com o workspace atual, sem diferenças. Alterações de comentários/quebras de linha durante outras tarefas estão identificadas no inventário.

## Relatórios locais

No projeto anfitrião, `Builds/FixedVectorValidation/Reports/Revalidation` contém os resultados NUnit Windows/ARM64, `editor-x64.json`, `player-mono-x64.json`, `player-il2cpp-x64.json`, os quatro CSVs completos, `trace-comparison-all.json` e o inventário SHA-256 dos fontes isolados. Os relatórios anteriores e o microbenchmark continuam no diretório pai. Os tempos publicados acima não foram medidos novamente; esta execução ampliou a validação funcional/determinística. O novo APK está em `Builds/FixedVectorValidation/Android/Revalidation/FixedPointValidation.apk`.

SHA-256 do APK: `b664498d9c0302eb93ad51e74afda16f185d79f7e0004a81bec751a4451f67f6`.

A cópia do player está em `Temp/FixedVectorRevalidationProject-20261011`; ela preserva os fontes efetivamente compilados enquanto outras alterações seguem no workspace compartilhado. O resumo consolidado distingue os testes de vetores da suíte completa do pacote. Nenhum workflow remoto foi executado nesta validação. A igualdade continua condicionada à mesma versão, configuração e sequência de entradas; os testes ampliados não enumeram todo o domínio de 64 bits nem substituem a execução nativa em hardware ARM64.
