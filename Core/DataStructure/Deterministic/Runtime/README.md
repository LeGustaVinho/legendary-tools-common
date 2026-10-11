# Runtime determinístico

Namespace: `LegendaryTools.Deterministic`. A simulação usa ticks inteiros, inputs normalizados, estado registrado e políticas congeladas. Coleções Native e kernels executam em Burst; o coordenador de ticks, retenção, replay e rede executa no host Managed do Unity. `NativeCounterSimulation` demonstra a conclusão dos jobs antes de publicar o checkpoint e os eventos.

## Componentes

| Tipo | Responsabilidade |
| --- | --- |
| `DeterministicRuntimeContext` | Backend explícito e fábrica de memória compatível |
| `DeterministicStateRegistry` | Registro por StateId, perfil congelado e checkpoint canônico do mundo |
| `ManagedStateBindings`, `NativeStateBindings` | Adapters das 18 famílias de coleções |
| `DeterministicWorldHistory` | Retenção limitada por boundary: full, keyframe/delta ou páginas compartilhadas |
| `DeterministicInputHistory<T,TEquality>` | Inputs reais imutáveis, predição e participação histórica dos jogadores |
| `DeterministicCommandBuffer<T>` | Partições exclusivas por produtor e merge em ordem total |
| `NativeDeterministicCommandBuffer<T>` | Writers para jobs, com verificações de dependências do Unity |
| `DeterministicSimulationRunner<TInput,TEquality,TEvent>` | Lockstep ou rollback, ressimulação e confirmação |
| `DeterministicEventLedger<T>` | Eventos especulativos, retração e confirmação uma única vez |
| `DeterministicReplayArchive<TInput,TCodec>` | Inputs reais, hashes e checkpoints versionados; restauração e seek |
| `DeterministicNetworkSession<TInput,TEquality,TEvent,TCodec>` | Inputs, commits, entrada tardia, reconexão e migração |
| `DeterministicRaftNode` | Eleições, log replicado, snapshots e consenso conjunto |
| `IDeterministicTransport`, `DeterministicLoopbackNetwork` | Contrato de datagramas e simulador de perda, duplicação e atraso |

## Criar e executar um mundo

```csharp
using LegendaryTools.Deterministic;
using LegendaryTools.Deterministic.Samples;

var context = new DeterministicRuntimeContext(DeterministicBackend.Managed);
using var counter = new DeterministicCell<int>();
// IDs de versões/configurações pertencem ao protocolo da aplicação.
var profile = new DeterministicCompatibilityProfile(
    application: 1, numeric: 0, random: 0, inputSchema: default(Int32Policy).SchemaId,
    simulationSettings: 1);
using var state = new DeterministicStateRegistry(context, profile, stateCapacity: 1);
state.TryRegister(ManagedStateBindings.Cell(
    1, counter, default(Int32Policy), 1, 128, 128)).ThrowIfFailure();
state.TryFreeze().ThrowIfFailure();
var options = new DeterministicRuntimeOptions(
    maxPlayers: 2, prediction: PredictionPolicy.HoldLast, inputDelay: 0);
var players = new[] { new PlayerId(1), new PlayerId(2) };
using var runner = new DeterministicSimulationRunner<int, Int32Policy, int>(
    context, options, state, new CounterSimulation(counter), players,
    neutral: 0, maxEventsPerTick: 8);
runner.TrySubmitInput(players[0], 0, 3).ThrowIfFailure();
runner.TrySubmitInput(players[1], 0, 7).ThrowIfFailure();
runner.TryAdvance(1, out int executed).ThrowIfFailure();
// NextTick == 1: checkpoint 1 contém o estado após simular o input do tick 0.
```

O registry exige IDs não zero e únicos. `TryFreeze` ordena os bindings por ID; a ordem de registro não afeta os bytes. O argumento de semântica do binding precisa identificar capacidades, política de crescimento, igualdade, ordem e outras regras que alterem o comportamento futuro. Não incluir backend, slots de snapshots ou tamanho de páginas nesse identificador quando forem apenas escolhas locais de armazenamento. Schemas e versões dos codecs integram a assinatura do binding.

`maxCanonicalBytes` e `scratchBytes` são limites explícitos de cada binding. Dimensionar scratch para as estruturas temporárias usadas na validação, com alinhamento; o tamanho do pacote canônico sozinho não garante scratch suficiente. Os adapters fornecidos validam dados e relações antes de publicar. O registry também mantém um checkpoint de recuperação caso um adapter personalizado falhe durante a publicação. Um adapter que falha no próprio rollback deixa o registry faulted.

O perfil deve identificar a versão das regras da aplicação, aritmética, algoritmo/sampler aleatório, input e parâmetros da simulação. `FixedDeterministicProfile.TryFingerprint` fornece a composição da configuração congelada do FixedPoint e do Philox; `FixedRandomStateCodec` conserva key, stream e posição. Registrar o RNG como parte do estado. Os adapters opcionais ficam em uma assembly separada, sem alterar a implementação de FixedPoint.

Para Native, criar `DeterministicRuntimeContext(DeterministicBackend.BurstCompiler, new NativeDeterministicMemoryFactory())`, usar coleções `NativeDeterministic*` e `NativeStateBindings`. Burst precisa estar habilitado; indisponibilidade retorna `BackendUnavailable`. A escolha do backend permanece fixa. Checkpoints podem comunicar réplicas com backends diferentes, cada uma importando no backend que já possui.

## Ticks, rollback e apresentação

Boundary N é o estado anterior ao input N. O runner só captura N+1 depois de completar a simulação e validar os eventos. Se o tick falhar, restaura N e abandona os eventos desse tick. Retornar `Pending` permite esperar inputs ou recursos; tratar os demais statuses explicitamente no host.

`TrySubmitInput` aceita o tick já normalizado. `TrySubmitLocalInput` adiciona o input delay configurado. Os ticks iniciais desse delay recebem o input neutro. A predição é explicitamente `HoldLast` ou `Neutral`. O limite de predição e o histórico impedem especulação/rollback ilimitados. Inputs reais repetidos com o mesmo valor são idempotentes; alterar um input real já conhecido é `ProtocolConflict`.

Em rollback, inputs atrasados que diferem da predição solicitam restauração do primeiro tick afetado e ressimulação. `TrySynchronizeRollback` termina a correção sem executar um tick novo. Em lockstep, o runner espera todos os inputs ativos. A confirmação pode ser automática pela completude dos inputs ou externa por `TryCommitInputsThrough`; sessões de rede usam confirmação externa pelo consenso.

`IDeterministicEventSink<T>` recebe `Speculated`, `Retracted` e `Confirmed` com IDs estáveis por tick, sistema, entidade e sequência. Apresentação especulativa precisa tratar a retração; efeitos externos permanentes devem usar apenas `Confirmed`. O sink e o observer não devem alterar o mundo determinístico nem lançar exceções. A aplicação cuida dos custos e alocações de sua apresentação.

O host chama `TryPump(nowMs, maxSimulationTicks, maxProposals)` com relógio monotônico e orçamento de trabalho. `nowMs` controla rede e eleições; não entra na regra do gameplay. Um agendador da aplicação fornece a cadência de 60 Hz ou outra `TickRate` congelada. O runner não consulta relógio, física ou RNG global do Unity.

## ECS e comandos paralelos

`SlotMap` oferece identidade com geração; `SparseSet` armazena componentes densos. O identificador pertence ao pool que o emitiu. Usar `OrderedByEntity` quando a ordem de sistemas depender de IDs, pois a remoção densa troca com o último elemento. Referências Unity/Entities são resolvidas no host e não entram no estado canônico.

Cada produtor de comandos recebe um ID fixo e uma partição exclusiva. A ordem é `(Phase, SystemId, Entity.Index, Entity.Generation, Sequence, ProducerId)`. A sequência deve representar a ordem lógica do produtor, independentemente da ordem de agendamento. Cabeçalhos duplicados são conflito. Completar todos os jobs, chamar `TryMerge`, aplicar os comandos ordenados e depois `TryReset`.

`Tests/Deterministic/Integration/NativeCommandRuntimeTests.cs` demonstra dois produtores Burst independentes. Não existe dependência do pacote Entities; um system group da aplicação pode executar esse mesmo protocolo. O estado do sistema, pools e RNG devem ser registrados antes de congelar o registry.

## Memória e snapshots

Os limites padrão de runtime são 60 Hz, delay 2, histórico 120 ticks, predição até 8 e keyframes a cada 16. Backend, número máximo de jogadores, predição e capacidades precisam ser escolhidos pelo chamador. `Fixed` nas coleções reserva memória; `Grow` pode alocar até o limite. O runtime pré-aloca suas arenas, com custo de criação proporcional aos máximos.

| Política do mundo | Retenção |
| --- | --- |
| `FullPerTick` | Cópia completa por boundary |
| `KeyframesAndDeltas` | Keyframe completo; intervalos alterados contra o keyframe; promoção para full se o delta custar mais |
| `CopyOnWrite` | Páginas canônicas idênticas compartilhadas; cópia de páginas alteradas |

Snapshots locais de coleção são independentes dessa retenção: as duas políticas contíguas fazem cópias completas; o COW das coleções atua nas páginas do armazenamento interno. Se apenas o histórico do mundo for necessário, configurar `snapshotSlots: 0` nas coleções evita reservar um segundo histórico de leases. As arenas mantêm espaço para o pior caso; payload compartilhado menor não reduz a memória já reservada. `WorldHistory.ReservedBytes`, `LivePages` e `TryGetStoredBytes` permitem observar esses custos.

Buffers, frames, comandos e views retornados são empréstimos. Não reter após a próxima operação que usa o staging, mudança de roster ou descarte do proprietário. Collections Native são aliases quando copiadas e devem ser descartadas exatamente uma vez. Completar jobs antes de operar o registry, capturar/restaurar ou descartar owners. Descartar runner/sessão antes do registry e das coleções que registrou. A sessão não descarta o transport fornecido pelo chamador.

## Replay

Criar `DeterministicReplayArchive` com `runner.Fingerprint`, codec de input e limites de bytes/records/jogadores. `TryBegin(state, boundary)` grava o checkpoint inicial. Depois de cada tick confirmado, adicionar os inputs reais em ordem de PlayerId e o hash de boundary N+1 por `TryAppendInputs(N, inputs, hash)`. `TryAppendCheckpoint` permite keyframes de seek.

Para gravar um checkpoint de um boundary passado, restaurar explicitamente esse mundo em um registry apropriado antes de adicioná-lo; o archive serializa o estado atual do registry. `TryWriteCanonical` exporta; `TryReadCanonical` valida integralmente schemas, perfil, checksums, sequência e checkpoints antes de substituir o archive. `TryFindCheckpoint`/`TryRestoreCheckpoint` encontram o ponto de partida; `TryReplayTo` exige um runner posicionado no boundary inicial do seek e verifica hashes durante o avanço. Mudanças de participação acompanham os IDs dos frames reais.

## Sessões e transporte

Criar `DeterministicNetworkSession` com registry congelado, pipeline, transport, SessionId novo, voters iniciais e topologia. `AuthoritativeHost` envia inputs ao líder; `PeerToPeer` distribui inputs entre os participantes. Ambas replicam commits por Raft. O líder propõe frames completos; os demais executam o mesmo gameplay e conferem hashes.

O transport fornece `LocalPeer`, MTU, envio com cópia dos bytes e recepção não bloqueante. Sua implementação deve vincular o remetente à identidade de processo correspondente. A sessão verifica identidade completa, destinatário, SessionId, perfil, versão e XXH64. XXH64 serve para integridade/divergência; a aplicação escolhe a autenticação e segurança do transporte quando precisar delas.

`DeterministicLoopbackNetwork` oferece endpoints, relógio explícito, links e falhas reproduzíveis (`LoopbackFaults`). Não há adapter de sockets/Unity Transport nesta entrega; o contrato permite acrescentá-los sem alterar a simulação. Perda ou capacidade temporária do transport causa retransmissão nos próximos pumps.

`TrySubmitLocalInput(captureTick, value)` guarda inputs até sua confirmação. Depois de entrar/reentrar, retomar a captura a partir de `EarliestLocalCaptureTick`; ticks anteriores podem pertencer a outro período de participação ou processo. `IsReady`, `CommittedBoundary`, hashes, `Raft.Leader` e o observer permitem conduzir a apresentação.

`MaxPlayers` limita os PlayerIds distintos durante toda a sessão; IDs inativos conservam carry para reentrada. `RaftOptions.MaxPeers` limita todas as identidades `(PlayerId, Incarnation)` conhecidas, inclusive processos substituídos; reservar margem para learners e reinícios. O maior frame completo e as duas listas da configuração conjunta precisam caber em `MaxCommandBytes` e no MTU. Dimensionar `MaxSnapshotBytes` para o checkpoint de aplicação mais os metadados e listas Raft. Os limites não crescem automaticamente.

Snapshots de rede são divididos em chunks com janela limitada, confirmação de prefixo e retransmissão. Um learner instala o checkpoint comprometido, preserva o estado futuro do RNG/pools e recebe o restante do log. `Pending` na instalação é repetido sem perder o pacote já recebido. Líderes e seguidores compactam o prefixo aplicado do log para manter a retenção limitada.

## Quórum, migração e reinício

Raft usa maioria estrita, voto único por term, comparação do log e commit por maioria de entradas do term atual. Configurações conjuntas exigem maioria antiga e nova; a configuração mais recente no log determina decisões de consenso. O algoritmo segue o [artigo original de Raft](https://raft.github.io/raft.pdf), adaptado ao contrato de processos efêmeros desta biblioteca.

Sem quórum, a sessão pausa a especulação/confirmação assim que sua detecção por timeout expira. Não há detecção instantânea de partições. Mensagens atrasadas podem manter uma certificação de disponibilidade até o timeout; apenas a maioria válida pode comprometer novos frames. Eleições alteram o líder sem alterar as regras do gameplay.

Com maioria sobrevivente, o novo líder remove por consenso conjunto participantes sem contato após `MembershipTimeoutMs` (padrão 1500 ms). `0` desabilita essa remoção automática. Enquanto a mudança está em andamento, a sessão aguarda. O roster de inputs só muda de forma ordenada no boundary comprometido. Assim, um host perdido deixa de impedir novos frames quando a remoção é confirmada.

`TryRequestJoinOrReconnect(bootstrapPeer)` solicita um líder, transfere estado e espera admissão. Um processo ainda vivo conserva sua incarnation. Um processo reiniciado usa o mesmo PlayerId, uma incarnation nova e `startAsLearner: true`. Não persistimos/restauramos `CurrentTerm`/`VotedFor` para recuperar poder de voto. O quórum existente precisa admitir a nova identidade; nunca reutilizar a incarnation de um processo que perdeu suas credenciais em memória.

Dois jogadores precisam dos dois votos. Se um processo desaparecer, o sobrevivente não pode diminuir o quórum sozinho nem admitir o reiniciado. Para continuar explicitamente uma partida diferente, exportar `TryWriteSnapshot`, criar uma nova sessão e novos voters e chamar `TryRestoreAsNewSession` antes de qualquer eleição. O SessionId precisa ser diferente. Esse checkpoint portátil importa gameplay e carry, sem credenciais Raft ou continuidade do consenso anterior.

O contrato cobre falhas cooperativas de transporte/processo. Capacidades esgotadas, schemas incompatíveis e divergência são statuses observáveis; tratá-los no host. A aplicação define como anunciar a pausa, corrigir configurações ou encerrar a sessão.

## Exemplos e evidência

`Samples/CounterSimulation.cs` contém a mesma transição Managed e Native/Burst. Os testes de runtime mostram rollback, lockstep, replay, protocolo, falhas e migração. `Tests/Deterministic/Integration/NativeNetworkSessionTests.cs` verifica uma sessão Managed e outra Native no mesmo grupo. Consulte `Tools/Deterministic/VALIDATION.md` e o CSV de benchmarks antes de interpretar garantia de plataforma ou desempenho.
