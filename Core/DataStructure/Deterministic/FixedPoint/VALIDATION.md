# Evidência de validação

Revalidado em 11/10/2026 para os tipos escalares signed/unsigned de 32/64 bits e seus codecs. Os limites publicados da matemática são alvos verificados por referências; a amostragem não é uma prova exaustiva. As correções e a revisão independente estão em [REVIEW.md](REVIEW.md); a rodada adicional de casos comuns, limites e entradas incomuns está em [EDGE_CASE_VALIDATION.md](EDGE_CASE_VALIDATION.md).

| Ambiente | Evidência atual |
|---|---|
| Unity 6000.3.20f1, Mono Editor, Windows x64 | 87 testes escalares passaram em Editor isolado |
| Burst 1.8.29, Windows x64 | 94 campos por caso no corpus de 20.880 casos e 235.170 comparações adicionais de matemática binária; `BurstDiscard` confirma execução nativa, inclusive da API pública |
| .NET 8.0.29, Windows x64 | 81 testes do escopo escalar passaram; o XML completo contém 435 testes, incluindo 354 de Random/vetores criados em outras tarefas |
| .NET 8.0.31, Linux ARM64 emulado via Docker | O mesmo assembly passou em processo ARM64: 81 testes escalares e os 354 testes adicionais |
| Player Mono Windows x64 | 47.560 casos passaram, todos confirmados como Burst nativo; API pública e assinaturas versionadas passaram; 716 casos managed de parsing/formatting passaram separadamente |
| Player IL2CPP Windows x64 | Build e execução passaram; mesmas assinaturas do corpus numérico e dos 716 casos managed de Mono |
| Player IL2CPP/Burst Android ARM64 | APK recompilado com a validação atual; execução pendente em dispositivo ARM64, aceita pelo usuário |

Os players usam uma cópia isolada dos fontes escalares. As APIs de vetores e números aleatórios estavam sendo implementadas em outras tarefas durante esta execução e não fazem parte da revisão nem da validação IL2CPP aqui descritas. Os relatórios `scalar-player-source-sha256.json` da revisão inicial e `Recheck/scalar-source-sha256.json` da rodada adicional identificam os fontes efetivamente compilados.

As referências incluem 13.340 casos por perfil para raízes, trigonometria, funções hiperbólicas, exponenciais, logaritmos e recíprocos. Elas usam razões decimais exatas e mpmath 1.3.0 com 160 dígitos. As políticas de arredondamento na aritmética/conversão IEEE são comparadas com BigInteger. A conversão de saída IEEE também compara a distância racional exata aos dois vizinhos representáveis e verifica o empate para significando par.

A rodada adicional inclui 117.585 referências por perfil para Pow, Log com base e Atan2, geradas com 200 dígitos e potências inteiras racionais exatas. Ela acrescentou 27 casos standalone e um caso Burst nativo. Também compara 64.488 casos de gramática finita com `decimal.TryParse` e 888 casos de nomes especiais/símbolos personalizados; detalhes e correções estão em [EDGE_CASE_VALIDATION.md](EDGE_CASE_VALIDATION.md).

Na primeira revisão foram acrescentados 26 casos NUnit standalone e um caso adicional de Burst no Editor. `FixedPolicyOracleTests` executa 389.760 comparações de aritmética/FMA contra racionais e módulo euclidiano independentes, abrangendo todas as precisões, os cinco arredondamentos, quatro políticas de overflow e três políticas de resultado negativo unsigned. Também verifica conversões entre as quatro variantes nas 190 combinações de escalas 32/64, numeradores até `ulong.MaxValue` e divisão por zero. `FixedReviewRegressionTests` reproduziu 12 falhas antes das correções; os 13 casos da fixture passaram depois.

A rede tem round-trip para todas as variantes e codecs, incluindo extremos, especiais e cursores compartilhados entre campos. Os casos inválidos cobrem VarInt redundante/truncado/grande demais, códigos de faixa não utilizados, perfis incompatíveis, buffers insuficientes, limites unsigned acima de `long.MaxValue` e quantização com perda explícita. Nenhum teste bem-sucedido depende de clamp silencioso do serializer.

`FixedNetworkGoldenTests` acrescenta bytes esperados literais de VarInt e little-endian, cada truncamento de leitura/escrita, bit packing LSB-first iniciado nos oito offsets possíveis, preservação dos bits externos ao campo e falha sem avanço do cursor quando a expansão da precisão causa overflow.

## Corpus comum entre runtimes

`Tests/FixedPoint/FixedReplayCorpus.cs` é a fonte do corpus. `Tools/FixedPoint/sync_replay_corpus.py` gera uma cópia idêntica para a assembly opcional de validação dos players. A versão 1 contém 20.880 casos: 58 famílias de tipo/precisão, cinco arredondamentos e 72 combinações de overflow, divisão por zero, resultado negativo unsigned e perfil matemático.

Cada caso tem 94 campos comparados diretamente: valores raw, status, bits IEEE, operações unárias/binárias, FMA, Pow, Log com base, Atan2, SinCos, operações escalares, conversões entre variantes e os bytes/cursores de cada codec. A execução managed compara `Portable`, `Intrinsic` e `Auto`; os jobs nativos usam os três modos e verificam a ausência de fallback managed. Os 26.680 vetores de referência dos dois perfis mais esse corpus resultam em 47.560 casos por player.

| Assinatura versionada | Valor |
|---|---|
| Entradas do corpus | `14675807239874021106` |
| Saídas do corpus | `6622938699589000362` |
| Validação completa de cada player | `11024892444286813640` |
| SHA-256 do CSV completo .NET/Mono/IL2CPP | `318392b12d9a390d4d63e318e327489b02ad625de08ea5c0c5859e02cdfa9ce5` |

Os três CSVs completos coincidiram na comparação direta byte a byte e no SHA-256, incluindo as entradas e todos os campos de saída de cada linha. A assinatura das entradas foi adicionada após localizar um estreitamento indevido no C++ gerado pelo IL2CPP para uma expressão do gerador de testes. A expressão foi separada em variáveis `ulong`/`bool`; o resultado final passou sem alterar a assinatura esperada das saídas. A investigação e a execução inicialmente falha foram preservadas para auditoria.

A igualdade entre backends é exigida para a mesma configuração e versão da implementação. O corpus cobre as combinações descritas com entradas finitas selecionadas e valores especiais; não enumera todo o domínio de 32/64 bits nem todas as possíveis sequências de operações. O hash complementa as comparações campo a campo.

A API managed tem regressões para zero negativo convertido em inteiros unsigned, seus formatos monetário/percentual/customizado e rejeição de símbolo monetário no meio dos dígitos. O formato de round-trip conserva o sinal de zero independentemente do formatador do runtime.

O teste de alocações mede o heap managed após aquecimento para aritmética, raiz e escrita/leitura por ponteiro. Parsing/formatting são managed e não têm promessa de zero alocação.

## Microbenchmark managed

Uma execução local em .NET 8.0.29, Windows x64, durante outras atividades do Editor. Tempos incluem loop/checksum e podem variar com JIT, CPU e carga. Não são medições de Burst nem previsões de custo por frame.

| Variante | Soma ns/op | Multiplicação ns/op | Divisão ns/op | Sin Accurate µs/op | Sin Fast µs/op | VarInt write/read ns/op |
|---|---:|---:|---:|---:|---:|---:|
| Signed32 | 44,1 | 56,3 | 58,0 | 47,7 | 23,3 | 519,5 |
| Unsigned32 | 11,8 | 39,8 | 39,1 | 50,2 | 24,1 | 207,5 |
| Signed64 | 11,6 | 29,2 | 29,7 | 45,6 | 23,6 | 177,3 |
| Unsigned64 | 11,6 | 32,4 | 29,1 | 45,7 | 23,2 | 188,3 |

Todas essas medições reportaram zero bytes managed por operação após aquecimento. O primeiro grupo tem efeitos de tiering do JIT; compare estratégias em execuções controladas antes de escolher um perfil por orçamento de CPU.

## Reproduzir players sem interferir no projeto de trabalho

`Tools/FixedPoint/create_validation_project.py` copia somente a biblioteca, os adaptadores, as referências e o builder para um projeto temporário separado. O projeto principal pode manter cenas sem nome ou não salvas.

```text
python Tools/FixedPoint/sync_replay_corpus.py --check
python Tools/FixedPoint/create_validation_project.py <diretorio-temporario-novo>
Unity -batchmode -nographics -projectPath <diretorio-temporario> -executeMethod LegendaryTools.FixedPoint.Editor.FixedValidationBuild.BuildMonoBatch -quit -logFile <log>
Unity -batchmode -nographics -projectPath <diretorio-temporario> -executeMethod LegendaryTools.FixedPoint.Editor.FixedValidationBuild.BuildIl2CppBatch -quit -logFile <log>
Unity -batchmode -nographics -projectPath <diretorio-temporario> -executeMethod LegendaryTools.FixedPoint.Editor.FixedValidationBuild.BuildAndroidBatch -quit -logFile <log>
FixedPointValidation.exe -batchmode -nographics --fixed-point-validation --fixed-point-report=<json>
FixedPointValidation.exe -batchmode -nographics --fixed-point-managed-validation --fixed-point-managed-report=<json>
```

Um resultado válido precisa ter `Passed=true`, `PublicApiPassed=true`, `Cases=NativeCases=47560`, `ReplayCases=NativeReplayCases=20880`, `ReplayVersion=1` e as três assinaturas da tabela acima. O job da API pública verifica as quatro fachadas, o runtime compartilhado e conversão de zero negativo em Burst nativo. Um build concluído sozinho não comprova paridade em runtime.

A execução managed opcional usa uma flag separada, deve ter `Passed=true`, `Cases=716`, `Checksum=8252000883644710091` e `Error=""`. Ela verifica parsing/formatting nos quatro tipos e em todas as precisões, sem entrar nos jobs Burst.

Para diagnóstico, o player aceita `--fixed-point-replay-trace=<csv>`. O programa standalone aceita `--replay-trace=<csv>` para exportar o mesmo formato. A sincronização do corpus deve ser feita antes de criar a cópia do projeto; `--check` apenas verifica que as duas fontes já coincidem.

O APK Android/ARM64 usa a mesma validação, escreve `fixed-point-validation.json` no diretório persistente do próprio app e executa automaticamente ao iniciar. Compilar o APK não o instala em um dispositivo.

Não havia dispositivo Android conectado no ambiente de validação. O usuário autorizou concluir a entrega com a execução IL2CPP/Burst Android ARM64 como pendência documentada. Os testes managed em processo ARM64 emulado e o build nativo do APK passaram, mas não substituem essa execução em dispositivo.

Os relatórios locais foram preservados em `Builds/FixedPointValidation/Reports` do projeto anfitrião, incluindo os XMLs NUnit, o relatório do Editor, os players Mono/IL2CPP e `revalidation-2026-10-11.json`. A pasta `Review` contém a revisão Codex, regressões antes/depois e os resultados das tentativas de revisão externa. Os CSVs grandes de diagnóstico permanecem em `Temp/FixedPointReview`; suas assinaturas e contagens estão no relatório consolidado.

A pasta `Recheck` preserva a rodada adicional: XMLs finais, reproduções das divergências de gramática, correções dos oráculos, JSONs dos dois caminhos dos players, status/proveniência dos builds, manifesto SHA-256 e `recheck-2026-10-11.json`.

O APK atual está em `Builds/FixedPointValidation/Android/FixedPointValidation.apk`; SHA-256 `037bef07a4daac75e04f744ff8803003813e1b71655b67d9ccd94d7efc5c5833`. Os relatórios dos players cobrem kernels numéricos, conversões IEEE, rede e o job da API pública; parsing/formatting também são exercitados pela execução managed opcional dos players.

A revisão independente Codex foi concluída e confirmou as correções. O Antigravity não produziu uma revisão completa porque uma ferramenta foi negada no modo headless. A alternativa Gemini CLI falhou com autenticação `UNSUPPORTED_CLIENT`, além de mensagens de configuração/trust. Nenhum parecer Gemini foi recebido; o usuário autorizou concluir com essa limitação documentada.

O workflow `fixed-point-validation.yml` é manual e prepara a suíte managed para Windows x64, Linux x64 e Linux ARM64. Ainda não foi executado remotamente. Os [runners ARM64 documentados pelo GitHub](https://docs.github.com/en/actions/reference/runners/github-hosted-runners) permitem repetir em hardware distinto da emulação local.

`Tools/FixedPoint/il2cpp-windows.vsconfig` descreve os componentes C++ e SDK necessários ao build Windows. A lista usa os [identificadores oficiais de componentes do Visual Studio Build Tools](https://learn.microsoft.com/en-us/visualstudio/install/workload-component-id-vs-build-tools?view=vs-2022). A instalação local foi autorizada pelo usuário: Visual Studio Build Tools 17.14.37710.0, MSVC 14.44.35207 e Windows SDK 10.0.26100.0. O instalador informou que não há reinicialização pendente.
