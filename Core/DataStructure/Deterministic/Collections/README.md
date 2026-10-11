# Coleções determinísticas

Namespace: `LegendaryTools.Deterministic`. Os algoritmos de coleção são compartilhados entre buffers GC e buffers nativos. Os backends têm tipos públicos separados e não fazem migração automática de dados.

As coleções, checkpoints canônicos e o runtime de rollback/replay/lockstep estão implementados. Sessões de rede oferecem authoritative host, P2P, entrada tardia, reconexão e migração por Raft. Veja `../Runtime/README.md` e `Tools/Deterministic/VALIDATION.md` para os contratos de integração e a validação realizada.

## Escolha de memória e backend

```csharp
var options = new BufferOptions(
    initialCapacity: 128,
    allocation: AllocationPolicy.Fixed,
    snapshots: SnapshotPolicy.CopyOnWrite,
    snapshotSlots: 8);

// GC: criação e operações de coleção em código Managed.
using var managed = new DeterministicList<int>(options);
managed.Add(42);

// Nativo: criar antes de agendar jobs; também pode ser operado pelo host Managed.
var native = new NativeDeterministicList<int>(options, Allocator.Persistent);
native.TryAdd(42);
// Completar todos os jobs e leitores emprestados antes de native.Dispose().
```

Escolha um backend para o estado da aplicação na inicialização. Nenhuma API converte uma coleção Managed para Native ou vice-versa. As capacidades e limites são explícitos. `Fixed` exige `MaxCapacity == InitialCapacity`; `Grow` dobra a capacidade até o máximo configurado. RingBuffer, Array e Cell usam capacidade fixa. A alocação de construção, os pools de snapshots e as tabelas são custos de inicialização. Crescimento em `Grow` pode alocar. Com `Fixed`, as operações e a captura/restauração usam memória previamente reservada.

Elementos e políticas genéricas são `unmanaged`. Referências a objetos e entidades externas devem usar IDs estáveis. Igualdade e ordem de structs de aplicação exigem políticas explícitas e puras. Primitivos inteiros, bool, char e enums têm políticas fornecidas; não há igualdade, hash ou codec automático para structs arbitrários ou ponto flutuante.

## Ordem e identidade

| Coleção | Ordem observável / particularidade |
| --- | --- |
| Array, Cell, List | Índices; `List.RemoveAt` preserva ordem, swap removal é explícita |
| Queue, Deque, RingBuffer | Ordem lógica do anel; sobrescrita de RingBuffer é opt-in |
| Stack | Enumeração do topo para a base |
| Dictionary, HashSet | Inserção; atualizar conserva posição, remover e reinserir coloca no fim |
| SortedList | Array ordenado, busca binária; inserção/remoção deslocam elementos |
| SortedDictionary, SortedSet | AVL por índices; busca e alterações O(log n) |
| PriorityQueue | Menor prioridade; empates FIFO por contador; enumeração explícita do heap sem ordenação |
| BitSet | Bits por índice; contagem e busca do próximo bit ativo |
| SlotMap | Handles com índice/geração; reutilização FIFO; gerações esgotadas aposentam o slot |
| SparseSet | Componentes densos; remoção troca com o último; `OrderedByEntity` oferece ordem por ID |
| LinkedList | Nós em pool com handles e ligações anterior/próximo |
| MultiMap | Chaves em ordem de inserção, valores em ordem de inserção por chave |

`StableHandle` é local ao pool/coleção que o emitiu. Não use um handle de outra coleção como se tivesse o mesmo proprietário. Snapshot e rollback restauram gerações, fila de reutilização, vínculos e contadores. Retomar um estado anterior pode revalidar um handle que era válido naquele estado. Nunca persistir `Revision` como estado de simulação: ele invalida views e enumeradores, mas não integra o checkpoint.

Managed oferece as interfaces C# correspondentes quando aplicáveis. Enumeradores concretos são structs; enumerar pela interface pode fazer boxing. HashSet/SortedSet oferecem operações em lote por `ReadOnlySpan<T>` com resultado preparado antes de alterar a coleção. O custo/alocação de um produtor `IEnumerable<T>` externo pertence ao chamador.

## Snapshots de coleção

```csharp
collection.TryCapture(out var checkpoint).ThrowIfFailure();
collection.Add(value);
collection.TryRestore(checkpoint).ThrowIfFailure();
checkpoint.Dispose();
```

Snapshots usam slots prealocados e leases. Copiar um snapshot não cria outro lease; descartar uma cópia antiga não pode liberar um lease posterior no mesmo slot. `FullPerTick` e `KeyframesAndDeltas` usam armazenamento contíguo e cópias completas nos snapshots de coleção. O provedor de retenção de keyframes/deltas do mundo é uma camada separada. `CopyOnWrite` compartilha páginas entre raízes e copia páginas escritas, com pool limitado. Reservar mais slots aumenta o custo de memória.

No backend Native, cópias de coleções são aliases, e o proprietário precisa sobreviver aos snapshots, views e jobs. Dispose ocorre uma única vez, após completar os jobs. Checks do Unity ajudam a detectar acessos inválidos; código de produção deve cumprir esse contrato de vida útil. Views e enumeradores emprestados expiram quando a coleção muda ou é restaurada. Nenhuma view pública fornece referência gravável ao armazenamento.

## Checkpoints canônicos

`TryWriteCanonical`, `TryValidateCanonical` e `TryReadCanonical` aceitam codecs explícitos. Dictionary, SortedList, SortedDictionary, MultiMap e PriorityQueue usam codecs de chave/valor ou elemento/prioridade. BitSet tem codec próprio.

```csharp
// Buffers GC prealocados no backend Managed. Os ponteiros são emprestados somente durante esta operação.
unsafe
{
    fixed (byte* output = packet)
    fixed (byte* temporary = workspace)
    {
        var writer = new DeterministicWriter(output, packet.Length);
        collection.TryWriteCanonical(ref writer, default(Int32Policy)).ThrowIfFailure();

        var reader = new DeterministicReader(output, writer.Position);
        var scratch = new CanonicalScratch(temporary, workspace.Length);
        collection.TryReadCanonical(ref reader, ref scratch, default(Int32Policy)).ThrowIfFailure();
    }
}
```

No Native/Burst, usar buffers nativos de bytes e mantê-los vivos durante a operação. `CanonicalScratch` não possui a memória; dimensionar workspace para os itens temporários, alinhamento e validação de índices. Uma chamada que retorna `BufferTooSmall` permite aumentar o buffer no limite de configuração. Não reter ponteiros de staging depois de liberar/desfixar a memória.

O formato tem magic, versão, tipo da coleção e schema/versão dos codecs. Inteiros usam little endian ou varint canônico; campos são escritos explicitamente. Nenhum padding, endereço, bucket derivado ou cache do CLR é serializado. Os checkpoints preservam capacidade lógica, ordem, gerações, filas de reutilização e contador da PriorityQueue. Buckets de hash e índices esparsos são reconstruídos após validação. Leituras rejeitam truncamento, schemas incompatíveis, IDs duplicados, ciclos e árvores/heaps inválidos antes de publicar o estado. MultiMap valida os dois buffers e suas relações antes de alterar qualquer um deles.

Codecs e políticas precisam ser puros e determinísticos. Codecs usam os métodos de `DeterministicWriter`, incluindo seu modo `CreateMeasure`; não escrever diretamente em `writer.Buffer`. Aplicações devem manter IDs de schema estáveis e registrar configurações de políticas e aritmética no perfil de compatibilidade da sessão. O formato da coleção não substitui esse perfil nem as verificações de integridade do protocolo.

## Validação

Os testes Editor exercitam jobs Burst com marcador `BurstDiscard`, comparam bytes Managed/Native e verificam snapshots, colisões, limites e ausência de garbage nas operações fixas. O harness `Standalone~/Deterministic.Validation` é uma ferramenta de desenvolvimento, sem compromisso de distribuição Managed fora do Unity. O corpus de player Windows x64 também foi executado em IL2CPP. Consulte o relatório de validação para distinguir suíte Editor, corpus de player e plataformas ainda sem execução.

Os arquivos `.Generated.cs` são gerados pelos scripts em `Tools/Deterministic`, na ordem: primitivas, codecs de campos, sequências Native, mapas ordenados Native, bindings canônicos, políticas FixedPoint e bindings de estado. Algoritmos permanecem nos kernels compartilhados. Os vetores XXH64 possuem gerador independente com `python-xxhash`.
