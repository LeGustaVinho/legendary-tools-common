# Grafos determinísticos

Namespace: `LegendaryTools.Deterministic`. A mesma sequência lógica de operações, sobre o mesmo estado inicial e configuração semântica, produz os mesmos handles, consultas, algoritmos e bytes em Managed e Native/Burst.

As otimizações de limpeza, crescimento por delta, DFS e lookup CSR são comparadas
em [GRAPH_EXTENDED_OPTIMIZATION_COMPARISON.md](../../../../Tools/Deterministic/GRAPH_EXTENDED_OPTIMIZATION_COMPARISON.md).

## Criar e modificar

```csharp
var options = new GraphOptions(
    new BufferOptions(128, snapshotSlots: 0),
    new BufferOptions(512, snapshotSlots: 0),
    direction: GraphDirection.Directed,
    cycles: GraphCyclePolicy.Acyclic,
    allowParallelEdges: false,
    allowSelfEdges: false);

using var graph = new DeterministicGraph<int, int>(options);
var first = graph.AddNode(10);
var second = graph.AddNode(20);
var connection = graph.AddEdge(first, second, 5);
graph.TrySetNode(first, 30).ThrowIfFailure();

foreach (var arc in graph.Outgoing(first))
    graph.TryGetEdge(arc.Edge, out var edge).ThrowIfFailure();
```

`GraphNodeHandle` e `GraphEdgeHandle` são tipos distintos sobre `StableHandle`. Pertencem ao grafo/linhagem; não são IDs globais nem `Unity.Entities.Entity`. Versões alternativas podem conter o mesmo handle com dados diferentes. Remoção incrementa a geração, reutilização é FIFO e geração esgotada aposenta o slot. Rollback pode revalidar handles de um estado anterior.

As opções ficam congeladas na construção. O padrão é direcionado, ciclos permitidos, sem arestas paralelas/autoarestas, memória fixa. `default(GraphOptions)` é inválido. Payloads e codecs são `unmanaged`; referências externas usam IDs estáveis. Igualdade de payloads não define identidade do nó.

O modo acíclico rejeita cada alteração que criaria ciclo. No não direcionado, isso garante uma floresta: autoarestas e duas ligações paralelas constituem ciclos. No direcionado, arestas paralelas no mesmo sentido podem ser válidas. `CycleDetected`, `DuplicateKey`, `NotFound` e falhas de recursos não publicam uma mutação parcial.

Nós/arestas são enumerados por handle crescente. Adjacências seguem handle de aresta crescente; autoarestas não direcionadas têm duas incidências, mas uma única aresta lógica. Remover um nó remove suas arestas incidentes em ordem determinística. `TrySetEdge` altera o payload; mudar endpoints requer remover e criar outra aresta.

## Native e ECS

```csharp
var native = new NativeDeterministicGraph<int, int>(options, Allocator.Persistent);
// Construir e reservar memória antes de agendar jobs.
// Jobs usam Try* e os statuses; o host também pode operar a coleção Native.
var borrowed = native.AsReadOnly();
// Completar todos os jobs/leitores antes de restaurar ou descartar native.
native.Dispose();
```

Native owners copiados são aliases. Descartar exatamente uma vez e manter o proprietário vivo durante todos os empréstimos. Acesso de leitura pode ocorrer em paralelo; alterações exigem um escritor exclusivo. Cada algoritmo usa seu próprio workspace. `Fixed` reserva os recursos; `Grow` dos buffers pode alocar. A expansão dos slots de versões é explicitamente uma operação do host entre jobs.

Views e enumeradores do mutável expiram após alteração/restauração. Use `TryMoveNext` para distinguir fim de enumeração de invalidação; `MoveNext` retorna falso em ambos. Não alterar o grafo durante uma travessia.

O núcleo não depende de Entities. Um system group pode emitir `GraphMutation<TNode, TEdge>` no command buffer existente, completar os produtores, fazer o merge e aplicar os comandos por um escritor. A ordem lógica é a ordem total de `DeterministicCommandHeader`; nunca derive sequência/ProducerId da ordem de agendamento. Para comandos que criam nós, a aplicação usa os handles retornados antes de construir referências subsequentes.

## Formas imutáveis

O congelado dimensiona seu workspace de construção com `GraphScratch.TryMeasureFrozen`
e libera esse backing após build/import bem sucedido. Falhas mantêm o workspace para
retry; readers e owners Native copiados continuam válidos após a construção em job.

| Forma | Construção e vida útil |
| --- | --- |
| Snapshot | `TryCapture`; lease sobre uma captura consistente, com COW quando configurado. O owner deve sobreviver à captura. |
| Congelado | `FrozenDeterministicGraph` / `NativeFrozenDeterministicGraph`; destino previamente reservado e construção única por `TryFreeze`/`TryBuild`. Independente da origem. |
| Persistente | `PersistentDeterministicGraph` / `NativePersistentDeterministicGraph`; `TryWith*` publica outro lease sem mudar a versão de origem ou a seleção atual. |

```csharp
using var frozen = new FrozenDeterministicGraph<int, int>(options);
graph.TryFreeze(frozen).ThrowIfFailure();
// CSR compacto; consultas por handle usam busca binária.

using var versions = new PersistentDeterministicGraph<int, int>(options,
    new GraphPersistenceOptions(8, 64, AllocationPolicy.Grow,
        GraphVersionRetention.Leases));
versions.TryCaptureCurrent(out var empty).ThrowIfFailure();
versions.TryWithAddNode(empty, 10, out var populated, out var node).ThrowIfFailure();
versions.TrySelectCurrent(populated).ThrowIfFailure();
empty.Dispose();
populated.Dispose(); // A seleção atual tem seu próprio lease.
```

`Leases` permite liberar versões; cópias de um handle/version/snapshot são aliases do mesmo lease. Descartar uma cópia antiga não libera uma versão posterior que reutilizou o slot. `UntilOwnerDisposed` conserva as versões publicadas até descartar o store; `Dispose` de uma versão não expulsa esse histórico. `GraphVersionId` é local, não integra o estado de rede.

`TryWithBatch` publica uma raiz para um lote ordenado e abandona todo o lote se uma operação falhar. Os comandos referenciam handles conhecidos pelo chamador. Só a versão selecionada por `TrySelectCurrent` entra no binding do mundo. Capturas internas de publicação/recuperação têm reserva adicional ao orçamento de versões públicas.

Ao alcançar a capacidade, `TryWith*` retorna `SnapshotCapacityExceeded`. No host, `TryReserveVersionCapacity(required)` expande até o máximo configurado, preservando os leases. Uma solicitação de expansão executada em Burst retorna `BackendUnavailable`; complete os jobs, expanda e repita a operação. Com retenção completa, dimensionar os limites para todo o histórico desejado.

## Algoritmos

`GraphAlgorithms<TNode, TEdge>` aceita leitores concretos de mutável, snapshot, congelado ou versão persistente. Oferece BFS, DFS, alcançabilidade, ciclos, componentes e ordenação topológica. Componentes de direcionados são fracamente conexos. Ordenação topológica só aceita direcionados e desempata pelo menor handle disponível.

Reserve bytes com `GraphScratch.TryMeasure` e resultados com `GraphWorkBuffer<T>`. Managed fixa arrays durante a chamada; Native usa memória nativa viva durante todo o job. As implementações são iterativas. Resultados são publicados somente após sucesso; scratch pode ser usado como staging e não é estado da simulação.

## Checkpoints e deltas

A aplicação de deltas mantém validação completa e publica apenas os records que
diferem quando as capacidades coincidem. Mudança de capacidade usa publicação completa.
Um delta vazio continua invalidando views mutáveis emprestadas, com zero cópias COW.
Raízes antigas permanecem imutáveis; a normalização dos codecs também é preservada.

`TryWriteCanonical`, `TryValidateCanonical` e `TryReadCanonical` recebem codecs explícitos de nós/arestas. Inteiros e FixedPoint podem usar as políticas existentes. Para structs próprios, implementar `IDeterministicCodec<T>` com campos explícitos, schema/versão estáveis e métodos puros que também funcionem no modo de medição do writer. Não codificar padding, ponteiros ou referências.

O mutável e as versões persistentes conservam capacidades lógicas, slots, gerações, aposentadorias, filas de reutilização e endpoints. Adjacências são reconstruídas após validação. O congelado usa formato compacto próprio sem histórico de alocação mutável. Backend, páginas, leases, workspace e retenção local não participam do fingerprint semântico.

Use `GraphWire<...>.TryMeasureScratch` para importação. `GraphCheckpoint` é um empréstimo de bytes canônicos do mutável/persistente. `TryWriteDelta` compara esses campos canônicos e retorna `GraphTransferKind.Patch` ou `Checkpoint`, escolhendo a menor codificação. `GraphDeltaMode.Reversible` conserva preimages e metadados anteriores; `TryApplyDelta(..., reverse: true)` desfaz exatamente a transferência. Avanço simples usa menos bytes.

O receptor precisa estar no hash de origem exato (no reverso, no hash de destino). Uma transferência repetida ou sobre outro estado retorna `ProtocolConflict`; a aplicação pode decidir ignorar mensagens já reconhecidas. Schemas/perfis, referências, filas, ordenação e hashes são verificados antes da publicação. `TryValidateDelta` não publica; `TryWithDelta` cria uma nova versão persistente. Congelados são reconstruídos a partir do resultado.

Os buffers de entrada, saída e scratch devem ser separados. `GraphDeltaWire<...>.TryMeasureScratch` dimensiona o staging de duas raízes; as fachadas que serializam a raiz atual no scratch precisam também de espaço para esses bytes temporários. `BufferTooSmall` permite aumentar o workspace no host. Não reter ponteiros de `GraphCheckpoint`/staging após liberar ou desfixar a memória.

## Runtime e medição

Registrar `ManagedStateBindings.Graph` / `NativeStateBindings.Graph`, ou `PersistentGraph` para a seleção atual, antes de congelar o registry. Usar IDs de estado não zero e codecs/perfis iguais nos peers. O binding compõe as opções semânticas do grafo com o identificador de semântica da aplicação. `GraphSimulation` e `NativeGraphSimulation` demonstram a mesma transição, com jobs concluídos antes de checkpoints/eventos.

Rollback, replay e lockstep usam o runtime existente. Deltas são uma API pública adicional e não alteram automaticamente o protocolo de transporte. Grafos congelados compartilhados podem ser validados por hash em `SimulationSettings`; mudanças desse recurso exigem a política de compatibilidade da aplicação.

`MemoryStats` informa payload reservado/vivo, páginas vivas, páginas da raiz atual compartilhadas e leases físicos dos buffers. Exclui tabelas de bookkeeping, headers do allocator/GC e, no persistente, os descriptors de versões. COW reduz payload vivo compartilhado, mas não libera a reserva configurada. Medições de host Native não representam throughput de um job Burst.

Os geradores de facades ficam em `Tools/Deterministic`. Algoritmos e formatos ficam nos kernels compartilhados. Caminhos mínimos, SCC e adaptadores Entities não fazem parte desta versão.
