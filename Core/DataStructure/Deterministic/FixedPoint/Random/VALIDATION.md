# Validação de FixedRandom v1

Resultados coletados em **11 de outubro de 2026**, com Unity **6000.3.20f1**, Burst **1.8.29** e Collections **2.6.6**. Host Windows x64, Intel Core i9-13900KF. A documentação de uso e o contrato binário estão em [README.md](README.md).

## Resultados executados

| Ambiente | Resultado | Execução confirmada |
|---|---|---|
| Unity Editor Windows x64 | 406 testes Random aprovados | Managed e jobs Burst nativos; marcadores `[BurstDiscard]` |
| .NET 8.0.29 Windows x64 | 403 testes Random aprovados | Standalone managed, sem dependência da engine |
| .NET 8.0.31 Linux ARM64 | 403 testes Random aprovados | Processo Arm64 em Docker `linux/arm64`, com emulação no host x64 |
| Player Windows Mono | 160 cenários originais + 64 replays longos; API NativeArray aprovada | Burst nativo, sem fallback managed; build e processo concluídos com sucesso |
| Player Windows IL2CPP | 160 cenários originais + 64 replays longos; API NativeArray aprovada | Burst nativo, sem fallback managed; build e processo concluídos com sucesso |
| Exemplos standalone | Gameplay, loot, spawn, eventos, rollback e codecs aprovados | Fonte compilado e executado por `--examples` |

O conjunto standalone completo de FixedPoint também passou: **500 testes**, incluindo os 403 de Random. A execução ARM64 acima valida o runtime managed nessa arquitetura; não representa execução de Burst/IL2CPP em dispositivo Android. Os players nativos executados foram Windows x64.

As execuções do corpus coincidiram com este contrato:

```json
{"Version":1,"Cases":160,"WordsPerCase":152,"Checksum":16080504445963702515}
```

`FixedRandomCorpus` congela um hash por cenário e o checksum agregado, em [FixedRandomCorpus.Golden.cs](../../../../../Tests/FixedPoint/FixedRandomCorpus.Golden.cs). Cada cenário registra valores raw, status, posições, snapshots e bytes dos codecs. Managed e Burst comparam cada palavra antes do hash. No Editor, o corpus também é executado com particionamentos de jobs de 1 e 17 elementos para verificar independência do escalonamento.

A matriz cobre as quatro famílias fixed, precisões extremas 0/9/18 e a configuração usual de três casas, perfis matemáticos Fast/Accurate, cinco modos de arredondamento e variações dos backends inteiros. Essas variações passam por estados internos de teste; a API pública e os adapters também são exercitados com a configuração padrão congelada do processo. Seeds, streams, eventos, blocos parcialmente consumidos e casos de rejeição fazem parte da cobertura.

As respostas conhecidas de Philox zero, todos os bits um e o vetor baseado em pi vêm dos [vetores oficiais Random123](https://github.com/DEShawResearch/random123/blob/main/tests/kat_vectors). Há referências independentes para ordem das palavras, montagem de `ulong`, rejeição e bytes little-endian de descriptor/snapshot. A sequência de seed zero começa com `6627e8d5 e169c58d bc57ac4c 9b00dbd8`.

Outros testes verificam domínio/overflow, exaustão sem wrap, igualdade de tabelas e seleção direta, pesos zero, unicidade de índices, permutações, equivalência de lotes e chamadas escalares, falhas sem mutação, pacotes truncados e VarUInt não canônico. Estatísticas com seed fixo verificam frequências de dados/probabilidade/pesos, quadrantes, simetria e momentos esperados de disco, esfera, superfície e triângulo. Elas complementam as provas por construção e referências determinísticas; não são uma certificação estatística geral do gerador.

## Revalidação ampliada

Foram adicionados **216 casos de teste no Editor** (215 também executáveis no standalone), preservando o código de produção de Random e todos os fixtures originais da v1:

- [FixedRandomRegressionTests.cs](../../../../../Tests/FixedPoint/FixedRandomRegressionTests.cs): 66 casos. Referência independente em `BigInteger` para derivação de chave, 2.048 blocos Philox com counters altos e ordem de palavras; redução sem viés e consumo exato nas quatro famílias, transições de largura, rejeição no último word disponível, lotes atômicos e pesos constantes/especiais/sobrepostos.
- [FixedRandomBoundaryTests.cs](../../../../../Tests/FixedPoint/FixedRandomBoundaryTests.cs): 84 casos. Todos os tamanhos truncados de descritor/snapshot, checkpoints em todas as transições de bits com encoder independente, 128 mutações de bits do descritor, sete desalinhamentos de ponteiro e geometria de raio máximo nos dois perfis e cinco arredondamentos.
- [FixedRandomReplayStressTests.cs](../../../../../Tests/FixedPoint/FixedRandomReplayStressTests.cs): 65 casos. 64 streams/eventos com 256 operações misturadas cada, posições raw altas e blocos parcialmente consumidos; registram valores, status, bytes e posições, verificando rollback/checkpoints, equivalência entre lotes e escalares e entre pesos diretos e tabelas.
- `FixedRandomBurstTests.LongReplayMatchesEveryManagedWordAcrossReverseOrderAndDifferentNativeJobPartitions`: comparação de cada palavra com managed, ordem invertida e lotes de 1/13, com marcador que falha em fallback managed. O mesmo teste de replay é executado nos players Mono e IL2CPP.

O replay longo usa [fixtures separados](../../../../../Tests/FixedPoint/FixedRandomReplayStress.Golden.cs), congelados sem alterar o corpus original:

```json
{"StressCases":64,"StressOperations":16384,"StressChecksum":6659428461585953849}
```

Cada player executa esses 64 cenários duas vezes em Burst nativo, totalizando `StressNativeCases=128` (32.768 operações nativas), além de sua referência managed. Os relatórios Mono e IL2CPP confirmaram `StressPassed=true` e o checksum acima. O contrato original permaneceu em `16080504445963702515`. Os novos testes não encontraram falhas que exigissem alteração no gerador.

Os relatórios desta rodada estão em `Builds/FixedRandomValidation/Recheck-20261011` do host: `Editor.json`, `Standalone-Windows-Random.xml`, `Standalone-Windows-All.xml`, `Standalone-ARM64.xml`, `Standalone-Corpus.txt`, `Mono.json`, `IL2CPP.json` e os respectivos status/logs de build. O projeto isolado fica em `Temp/FixedRandomRecheckPlayer-20261011`. Esses arquivos são artefatos locais, fora dos fixtures versionados.

## Alocações e medições

O teste de alocações aquece e depois executa kernels numéricos, espaciais, collections, pesos e codecs por ponteiro com `GC.GetAllocatedBytesForCurrentThread`. O resultado foi **zero bytes managed**. A construção dos arrays do chamador, os exemplos, a infraestrutura de testes e os relatórios ficam fora dessa medição.

Medição observacional após aquecimento, configuração padrão, builds Release, valores aproximados em ns/op:

| Operação | .NET managed x64 | Mono + Burst x64 | IL2CPP + Burst x64 |
|---|---:|---:|---:|
| `NextUInt32` | 4,0 | 1,9 | 1,9 |
| `NextFixed64` | 19,1 | 9,2 | 10,0 |
| `InsideUnitSphere` | 259,2 | 77,5 | 80,0 |
| `OnUnitSphere` | 76.212,5 | 25.889,2 | 28.409,5 |
| Snapshot write/read | 368,7 | — | — |

Todos os cinco loops managed medidos alocaram zero bytes. Os loops dos players confirmaram Burst nativo por marcador. Superfície é mais cara que volume porque usa trigonometria/raiz em inteiros largos. Esses números variam com CPU, compilador, perfil matemático e atividade do host; não constituem limites de performance. Nenhum tempo ARM64 emulado foi usado nessa tabela.

## Reprodução standalone

Execute da raiz do pacote. Direcione artefatos para uma pasta fora de `Assets`; em PowerShell, `ARTIFACTS` abaixo representa o caminho escolhido pelo chamador.

```powershell
dotnet build 'Standalone~/FixedPoint.Tests/LegendaryTools.FixedPoint.Tests.csproj' -c Release --artifacts-path ARTIFACTS
dotnet 'ARTIFACTS/bin/LegendaryTools.FixedPoint.Tests/release/LegendaryTools.FixedPoint.Tests.dll' '--where=class =~ FixedRandom' --workers=0 --noresult

dotnet build 'Standalone~/FixedPoint.RandomValidation/LegendaryTools.FixedPoint.RandomValidation.csproj' -c Release --artifacts-path ARTIFACTS
dotnet 'ARTIFACTS/bin/LegendaryTools.FixedPoint.RandomValidation/release/LegendaryTools.FixedPoint.RandomValidation.dll' --examples
dotnet 'ARTIFACTS/bin/LegendaryTools.FixedPoint.RandomValidation/release/LegendaryTools.FixedPoint.RandomValidation.dll' --benchmark
```

O validador verifica os fixtures antes dos exemplos/benchmarks. Para a suite inteira, omita o filtro NUnit. Para ARM64, monte a pasta de saída AnyCPU em uma imagem `mcr.microsoft.com/dotnet/runtime:8.0` com `--platform linux/arm64` e execute a mesma DLL. Confirme `Process architecture: Arm64` no relatório; emulação e hardware físico devem ser registrados separadamente.

No Editor, execute `LegendaryTools.FixedPoint.Tests.FixedRandom*` em EditMode. Os testes Burst falham se o marcador indicar fallback managed; passar apenas no standalone não substitui essa verificação.

## Players isolados

Crie um projeto temporário fora do pacote, usando o gerador existente:

```powershell
python Tools/FixedPoint/create_validation_project.py VALIDATION_PROJECT
unity run VALIDATION_PROJECT --editor-version 6000.3.20f1 --timeout 900 --format json -- -nographics -executeMethod LegendaryTools.FixedPoint.Editor.FixedValidationBuild.BuildMonoBatch -logFile MONO_BUILD_LOG
unity run VALIDATION_PROJECT --editor-version 6000.3.20f1 --timeout 900 --format json -- -nographics -executeMethod LegendaryTools.FixedPoint.Editor.FixedValidationBuild.BuildIl2CppBatch -logFile IL2CPP_BUILD_LOG
```

Substitua os nomes em maiúsculas por caminhos absolutos. O builder escreve `Logs/FixedPointValidation/build-status.json`: confirme `State=succeeded` e o backend esperado. O exit code da CLI sozinho não confirma o resultado do builder.

Execute cada `Builds/FixedPointValidation/{Mono,IL2CPP}/FixedPointValidation.exe` com:

```text
-batchmode -nographics --fixed-random-validation --fixed-random-report=ABSOLUTE_JSON_PATH -logFile ABSOLUTE_LOG_PATH
```

No PowerShell, aguarde o processo GUI com `Start-Process -WindowStyle Hidden -Wait -PassThru` e confira `ExitCode=0`. O JSON deve informar `Passed=true`, `PublicApiPassed=true`, `Cases=160`, `NativeCases=160` e o checksum original acima; também `StressPassed=true`, `StressCases=64`, `StressNativeCases=128`, `StressOperations=16384` e `StressChecksum=6659428461585953849`. A flag Random permite validar esses corpora independentemente do corpus aritmético existente. A assembly de validação só entra no build com `LEGENDARY_FIXEDPOINT_VALIDATION`.

Na execução inicial, o projeto isolado ficou em `Temp/FixedRandomValidation-20261011-1742` do host e os relatórios em `Builds/FixedRandomValidation/Reports`: `Editor.json`, `Standalone-Windows-Random.xml`, `Standalone-Windows.xml`, `Standalone-ARM64.xml`, `Mono.json`, `IL2CPP.json`, `Mono-Build.json`, `IL2CPP-Build.json` e `Managed-Benchmark.txt`, além dos logs. A revalidação ampliada usa a pasta `Recheck-20261011` descrita acima. Essas pastas são artefatos locais ignorados pelo Git, não fixtures do pacote.

## Regeneração controlada

`generate_random_api.py` reproduz somente as fachadas públicas e os adapters NativeArray. `generate_random_reference.py` copia os fontes dos dois corpora congelados dos testes para a assembly de validação. Nenhum fixture é regenerado durante testes nem builds.

Para uma nova versão deliberada do contrato, revise as mudanças e execute explicitamente o validador standalone com `--generate PATH_TO_GOLDEN_SOURCE`, atualize as versões/expectativas e depois copie as referências. `--generate-stress PATH_TO_STRESS_GOLDEN_SOURCE` só gera o fixture separado do replay longo. Mudança de golden sem revisão/versionamento quebra a proteção de compatibilidade de saves, rede e replays.
