# Revisão e correções do fixed-point escalar

Concluída em 11/10/2026. Escopo: `Fixed32`, `UFixed32`, `Fixed64`, `UFixed64`, configuração, kernels escalares, conversões IEEE, API managed, adaptadores Burst e codecs escalares. A evidência por ambiente está em [VALIDATION.md](VALIDATION.md). Vetores e números aleatórios desenvolvidos em outras tarefas não integram esta revisão.

## Achados reproduzidos e corrigidos

| Origem | Problema | Correção e verificação |
|---|---|---|
| Codex independente, P2 | O parser limitava expoentes decimais a 1 bilhão, alterando o resíduo de `WrapFinite` para textos válidos como `1e1000000001` | Expoente mantido como `BigInteger` até a quantização/módulo; testes com expoentes além de Int32/Int64 e expoentes negativos enormes |
| Codex independente, P2 | `Log(1, -0)` retornava NaN por classificar zero negativo como uma base estritamente negativa | Domínio distingue negativo de zero assinado; regressões com ±0/±Infinity em todos os tipos, precisões, arredondamentos e perfis |
| Codex independente, P2 | Atalhos de `Tanh` e underflow interno de `Exp`/`Pow` perdiam `Inexact` e a direção do arredondamento | Preserva a fração descartada, arredonda para uma unidade quando a direção exige e conserva o sinal de zero; caminho comum para resultados positivos/negativos de Pow |
| Codex, investigação cruzada | IL2CPP estreitava o resíduo `ulong` para 32 bits em uma expressão do gerador do corpus, produzindo entradas distintas entre runtimes | Magnitude e sinal calculados em variáveis separadas; assinatura versionada das entradas; comparação final dos CSVs completos .NET/Mono/IL2CPP |

Os três defeitos de produção foram reproduzidos antes da correção. A fixture de regressão tinha 12 casos falhos e um aprovado; depois, os 13 passaram. Uma segunda leitura independente confirmou as correções e executou 19 casos das fixtures de regressão/políticas e 560 sondas racionais/modulares adicionais sem falhas. Nenhum achado reproduzido dessa revisão permanece aberto.

O problema do gerador surgiu porque a comparação Managed/Burst dentro do mesmo player recebia as mesmas entradas incorretas. A primeira divergência entre runtimes foi o caso 3615: `3069178193` tornou-se `18446744072483762513` no IL2CPP. Dos 20.880 casos, 2.895 tinham entradas diferentes; nenhum caso com entradas iguais tinha saídas diferentes. A auditoria do C++ escalar compilado não identificou estreitamento equivalente nos kernels de produção. Após corrigir o gerador, as entradas e todos os campos de saída coincidiram, mantendo a assinatura esperada `6622938699589000362`.

## Cobertura acrescentada

- Oráculos independentes de aritmética/FMA, conversões de escala/largura/sinal, numeradores unsigned completos, divisão por zero e políticas de overflow/arredondamento.
- Fixtures literais de bytes de rede e testes de truncamento, offset de bits, overflow de expansão e preservação do cursor/buffer.
- Corpus comum versionado de 20.880 casos, com 94 campos por caso comparados diretamente entre managed e Burst; assinaturas de entrada e saída verificadas entre processos/runtimes.
- Marcador `BurstDiscard` adicional no job da API pública do Editor, confirmando execução nativa da fachada e do runtime compartilhado.
- Players Mono e IL2CPP com os dois perfis de referência matemática e o corpus comum: 47.560 casos por player, todos nativos.

`FixedUnary` tem 25 operações, incluindo `ReciprocalSqrt`. A substituição de um número mágico pelo limite derivado da enumeração melhora a manutenção; não corrige uma operação antes ausente da cobertura.

## Revisão externa e limites aceitos

Uma rodada adicional encontrou e corrigiu problemas de gramática no parser managed, incluindo ordem de moeda/sinal, separadores personalizados, NULs terminais e prioridade/case dos nomes especiais. Ela acrescentou 28 casos NUnit e referências independentes para Pow, Log com base e Atan2. As reproduções, correções de expectativa dos testes e resultados estão em [EDGE_CASE_VALIDATION.md](EDGE_CASE_VALIDATION.md). Essa rodada não recebeu um novo parecer externo.

A revisão Codex foi executada em paralelo, com leitura independente dos fontes e sem consultar resultados externos antes de concluir seu parecer. O fluxo Antigravity do skill `parallel-review` foi iniciado com o material de revisão. A ferramenta informou `permission_denied` no modo headless e não retornou uma revisão completa. Não houve repetição desse fluxo após a negativa.

A alternativa Gemini CLI, configurada para leitura do pacote enviado e com ferramentas desabilitadas, saiu com código 55. A autenticação retornou `UNSUPPORTED_CLIENT`; o processo também reportou erro de configuração de hooks e diretório não confiável. Não há parecer Gemini, comparação de achados entre os dois modelos nem conclusão de ausência de problemas atribuível a Gemini. O usuário autorizou concluir com essa limitação documentada.

O APK Android ARM64 IL2CPP/Burst foi recompilado, mas não foi executado em dispositivo por não haver um conectado. Essa pendência foi aceita pelo usuário. O processo ARM64 emulado passou na suíte managed e não substitui a execução do player nativo Android.

A validação verifica determinismo nas configurações, casos e ambientes documentados, exigindo a mesma versão e configuração numérica entre participantes. Os vetores de referência e o corpus não constituem prova exaustiva de todo o domínio numérico.

Os relatórios originais, as regressões antes/depois, a investigação IL2CPP e as falhas das ferramentas externas estão preservados em `Builds/FixedPointValidation/Reports/Review` do projeto anfitrião. `revalidation-2026-10-11.json` registra assinaturas, contagens, escopo e limitações.
