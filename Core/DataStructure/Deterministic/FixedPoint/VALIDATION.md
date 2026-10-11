# Evidência de validação

Verificado em 11/10/2026. Este arquivo distingue testes executados de preparação de builds/CI. Os limites publicados da matemática são alvos verificados por referências; a amostragem não é uma prova exaustiva.

| Ambiente | Evidência atual |
|---|---|
| Unity 6000.3.20f1, Mono Editor, Windows x64 | 32 testes passaram |
| Burst 1.8.29, Windows x64 | Jobs nativos passaram em paridade de raw/status; `BurstDiscard` confirma execução nativa |
| .NET 8.0.29, Windows x64 | 28 testes passaram, sem dependências do Unity |
| .NET 8.0.31, Linux ARM64 emulado via Docker | Os mesmos 28 testes passaram em um processo ARM64 |
| Player Mono Windows x64 | 13.340 casos passaram; 13.340 casos confirmados como Burst nativo; API pública passou; checksum 14871527235704764883 |
| Player IL2CPP Windows x64 | Build e execução passaram; mesmos 13.340 casos, API pública e checksum do Mono |
| Player IL2CPP/Burst Android ARM64 | APK final compilado; execução pendente em dispositivo ARM64 |

As referências incluem 13.340 casos por perfil para raízes, trigonometria, funções hiperbólicas, exponenciais, logaritmos e recíprocos. Elas usam razões decimais exatas e mpmath 1.3.0 com 160 dígitos. As políticas de arredondamento na aritmética/conversão IEEE são comparadas com BigInteger. A conversão de saída IEEE também compara a distância racional exata aos dois vizinhos representáveis e verifica o empate para significando par.

A rede tem round-trip para todas as variantes e codecs, incluindo extremos, especiais e cursores compartilhados entre campos. Os casos inválidos cobrem VarInt redundante/truncado/grande demais, códigos de faixa não utilizados, perfis incompatíveis, buffers insuficientes, limites unsigned acima de `long.MaxValue` e quantização com perda explícita. Nenhum teste bem-sucedido depende de clamp silencioso do serializer.

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
python Tools/FixedPoint/create_validation_project.py <diretorio-temporario-novo>
Unity -batchmode -nographics -projectPath <diretorio-temporario> -executeMethod LegendaryTools.FixedPoint.Editor.FixedValidationBuild.BuildMonoBatch -quit -logFile <log>
Unity -batchmode -nographics -projectPath <diretorio-temporario> -executeMethod LegendaryTools.FixedPoint.Editor.FixedValidationBuild.BuildIl2CppBatch -quit -logFile <log>
Unity -batchmode -nographics -projectPath <diretorio-temporario> -executeMethod LegendaryTools.FixedPoint.Editor.FixedValidationBuild.BuildAndroidBatch -quit -logFile <log>
FixedPointValidation.exe -batchmode -nographics --fixed-point-validation --fixed-point-report=<json>
```

Um resultado válido precisa ter `Passed=true`, `PublicApiPassed=true`, contagem de casos e contagem de casos nativos iguais. O job da API pública verifica as quatro fachadas, o runtime compartilhado e conversão de zero negativo em Burst nativo. O checksum inclui raw, status, bits IEEE e round-trip de rede para comparação entre builds. Um build concluído sozinho não comprova paridade em runtime.

O APK Android/ARM64 usa a mesma validação, escreve `fixed-point-validation.json` no diretório persistente do próprio app e executa automaticamente ao iniciar. Compilar o APK não o instala em um dispositivo.

Não havia dispositivo Android conectado no ambiente de validação. O usuário autorizou concluir a entrega com a execução IL2CPP/Burst Android ARM64 como pendência documentada. Os testes managed em processo ARM64 emulado e o build nativo do APK passaram, mas não substituem essa execução em dispositivo.

Os relatórios locais foram preservados em `Builds/FixedPointValidation/Reports` do projeto anfitrião. O APK está em `Builds/FixedPointValidation/Android/FixedPointValidation.apk`; SHA-256 `912576A9B54689385060FE1464EC824B425D42755348A9534686A4EF0A68408E`. Os relatórios dos players cobrem kernels numéricos, conversões IEEE, rede e o job da API pública; parsing/formatting são exercitados pela suíte managed.

O workflow `fixed-point-validation.yml` é manual e prepara a suíte managed para Windows x64, Linux x64 e Linux ARM64. Ainda não foi executado remotamente. Os [runners ARM64 documentados pelo GitHub](https://docs.github.com/en/actions/reference/runners/github-hosted-runners) permitem repetir em hardware distinto da emulação local.

`Tools/FixedPoint/il2cpp-windows.vsconfig` descreve os componentes C++ e SDK necessários ao build Windows. A lista usa os [identificadores oficiais de componentes do Visual Studio Build Tools](https://learn.microsoft.com/en-us/visualstudio/install/workload-component-id-vs-build-tools?view=vs-2022). A instalação local foi autorizada pelo usuário: Visual Studio Build Tools 17.14.37710.0, MSVC 14.44.35207 e Windows SDK 10.0.26100.0. O instalador informou que não há reinicialização pendente.
