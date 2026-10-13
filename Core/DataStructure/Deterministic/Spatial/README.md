# Índices espaciais determinísticos

Este módulo usa `FixedBounds2/3`, `FixedRay2/3`, `Fixed64`, os kernels inteiros de geometria e o armazenamento determinístico existente. O núcleo não depende de GameObjects, de Unity.Entities ou de um ECS específico. O [checklist de entrega](../../../../Tools/Deterministic/SPATIAL_IMPLEMENTATION_STATUS.md) e a [auditoria dos requisitos](../../../../Tools/Deterministic/SPATIAL_COMPLETION_AUDIT.md) relacionam implementação, testes e limites da validação.

## Arquitetura e estratégias

| Assembly | Responsabilidade |
|---|---|
| Spatial | Contratos, geometria exata, mutação, consultas, pares, wire e dimensionamento |
| Spatial.Managed | Owners GC, leases COW e fachadas 2D/3D |
| Spatial.Native | Owners nativos, safety handles, readers paralelos e execução Burst |
| Spatial.Runtime | Bindings canônicos, fontes registradas e histórico local opcional |
| Spatial.Samples | Chaves de múltiplos volumes, fonte densa e aplicação de comandos |
| Spatial.Validation.Runtime | Corpus reproduzível para comparação Managed/Burst em players |

Escolha uma estratégia imutável na construção:

- `DynamicBvh`: árvore balanceada, alterações incrementais e envelopes com margem por lado. Uma atualização contida no envelope conserva a folha.
- `SparseGrid`: memberships incrementais, coordenadas com divisão floor e uma lista de objetos que excedem `MaxCellsPerObject`. Consultas com envelope visitam no máximo `MaxCellsPerQuery` células; acima disso fazem uma varredura exata. Raycast e nearest usam uma BVH auxiliar de todos os objetos. Essa BVH reaproveita os buffers de nós já reservados para o índice.
- `StaticBvh`: construção por mediana, maior extensão e desempate pela chave. Enquanto estiver dirty, as consultas examinam os registros lógicos. `TryRebuild` e um batch bem-sucedido publicam a árvore novamente.

As estratégias produzem os mesmos resultados e bytes canônicos. Estratégia, margens, limites de travessia, tamanho de célula, backend e retenção local ficam fora do fingerprint semântico. Ownership, limites lógicos, políticas de chave e configurações numéricas/geométricas fazem parte da compatibilidade.

## Uso Managed e Native

Inicialize `FixedRuntime` e `FixedBoundsRuntime` uma vez, antes da simulação. A representação MinMax/CenterExtents fica congelada no perfil global; consulte `../FixedPoint/BOUNDS.md`.

```csharp
var options = new SpatialIndexOptions(1024);
using var index = new DeterministicSpatialIndex3<int, int, Int32Policy>(options);
var box = FixedBounds3.FromMinMax(new FixedVector3(-1, -1, -1), new FixedVector3(1, 1, 1));
index.TryAdd(100, box, 7, new SpatialMetadata(2, flags: 1)).ThrowIfFailure();
index.TryMeasureQueryScratch(out int bytes).ThrowIfFailure();
var workspace = new byte[bytes];
var output = new SpatialHit<int>[16];
fixed (byte* scratch = workspace)
fixed (SpatialHit<int>* hits = output)
{
    var query = SpatialQuery.ForNearest(FixedVector3.zero, 4);
    index.TryQuery(query, new SpatialQueryScratch(scratch, bytes), hits, output.Length, out int count).ThrowIfFailure();
}
```

O exemplo exige um contexto `unsafe`. As fachadas Native têm o mesmo contrato e recebem `Allocator` na construção. Construa e descarte owners no host; jobs Burst podem aplicar mutações, sincronizar fontes e consultar buffers já alocados. Cada job leitor precisa de seu próprio workspace e output. Complete os jobs antes de escrever, capturar, importar, restaurar ou descartar o owner.

`AsReadOnly()` cria uma view emprestada. Uma mutação, restauração ou reconstrução invalida sua revisão; uma view não mantém o owner vivo. Os safety handles Native também verificam conflitos de jobs e aliases descartados. Na configuração Fixed, os caminhos testados de consulta, pares e sincronização não alocam GC após aquecimento. Grow pode alocar ao materializar capacidade.

## Semântica de consultas

- Fábricas públicas: `ForPoint`, `TryAabb`, `ForCircle/ForSphere`, `ForRay`, `ForSegment` e `ForNearest(k)`.
- Interseções e contatos nas bordas são inclusivos. Bounds default representa um ponto na origem. Direção zero é tratada por eixo; um ray completamente imóvel encontra as caixas que contêm sua origem.
- Point, AABB e circle/sphere são ordenados por chave. Nearest usa distância quadrada exata e chave. Ray/segment usa a fração exata de entrada e chave.
- Segmentos recebem dois endpoints e aceitam diferenças que não cabem em Fixed64. Distâncias usam intermediários maiores que 128 bits; a seleção não compara valores arredondados de `FixedBounds.SqrDistance`.
- `SpatialHit.Metric` é uma conversão diagnóstica feita depois da seleção. `MetricStatus` pode reportar overflow sem descartar um contato válido ou alterar a ordem.
- `SpatialFilter.All` aceita todas as camadas. `default(SpatialFilter)` tem máscara zero e não aceita objetos. RequiredFlags e ExcludedFlags filtram os metadados; predicados adicionais devem ser structs unmanaged e sem efeitos colaterais.
- Outputs insuficientes retornam `BufferTooSmall` com `written=0`, preservando o output. O workspace pode ser sobrescrito durante uma consulta; não o compartilhe entre leitores simultâneos.

Use uma chave externa estável para cada volume: EntityId, StableHandle ou uma chave composta `(EntityId, VolumeId)`. Equality, hash e comparer devem ser puros e consistentes; `Compare(a,b)==0` deve equivaler à igualdade. Não use IDs de instância do Unity nem hashes dependentes do processo.

## Pares em streaming

`TryMeasurePairScratch` dimensiona um workspace O(n). `TryGetPairs` cria um enumerador que expõe `TryNext(out pair, out hasValue)`. Cada par tem First < Second pela política de chave. A ordem é lexicográfica, contatos são exatos e os pares não se repetem. Filtro de camada é aplicado aos dois registros; `ISpatialPairPredicate` recebe First e Second já normalizados.

O enumerador empresta seu workspace até terminar. Não reutilize esse workspace nem intercale cópias do mesmo enumerador. A quantidade de pares pode ser O(n²), mas a memória reservada não depende dessa quantidade. Uma alteração do índice invalida a enumeração.

## Estado autônomo e fonte autoritativa

Um índice Autonomous mantém Bounds, payload e metadados como estado lógico próprio. `TryApplyBatch` valida/aplica a sequência dentro de um root transacional e restaura esse root se alguma operação falhar. Command buffers externos podem ordenar `SpatialMutation` antes desse batch.

Um índice Derived representa uma projeção de dados já registrados. Implemente `IDeterministicSpatialSource<TKey,TData,TBounds>` com Identity, Count, Revision e leitura ordinal estável. Crie Identity no host com `SpatialSourceIdentity.Create()` e guarde esse token no owner da fonte durante sua vida. Ele é local, distinto dos EntityIds/StateIds e excluído dos pacotes; evita confundir fontes que tenham a mesma revisão/contagem. Revision deve mudar em qualquer alteração relevante; leituras ordinais precisam ser repetíveis enquanto ela permanecer igual. O adaptador pode apontar para arrays, sparse sets, componentes de ECS ou registros de vários volumes, sem exigir uma arquitetura específica.

Os próprios índices autônomos também implementam a interface de fonte. Seu `TryGetAt` percorre a ordem de inserção em O(ordinal); use uma fonte densa com acesso ordinal O(1) para sincronizar grandes conjuntos sem custo quadrático.

Chame `TrySynchronize(source)` após mudar a fonte e antes de consultar a projeção. A sincronização retira membros ausentes, admite novos membros e altera somente Bounds/payload/metadados diferentes. Trocar todos os membros funciona com a capacidade cheia. Duplicatas, Bounds inválidos, falhas da fonte ou mudança de revisão abortam a publicação. Mutações públicas diretas em Derived são rejeitadas.

Leia Derived com `AsReadOnly(source)`. Essa view detecta tanto mudanças no índice quanto mudanças na fonte. A view comum e consultas diretas do owner são rejeitadas para Derived, evitando consultas sem validação da fonte.

## Rollback, replay e lockstep

`SpatialStateBindings.Managed2/3` e `Native2/3` registram índices autônomos. Informe codecs de chave/payload, fingerprint da política de chave, máximo de bytes canônicos e scratch de importação.

`ManagedDerived`/`NativeDerived` registram a projeção com seus source StateIds e `sourceRulesFingerprint`. Os IDs são ordenados, únicos, existentes e acíclicos; a regra de extração deve ser versionada sempre que mudar membership, chave, geometria ou payload. O binding Derived escreve apenas um descritor de quatro bytes. Os registros autoritativos continuam nos bindings das fontes.

O registry restaura todos os estados lógicos antes de chamar `TryCompleteRestore` em ordem de dependências. Essa etapa também roda quando uma publicação falha e o mundo anterior é recuperado. O capture chama `TryPrepareCapture` na mesma ordem, para sincronizar projeções antes de gerar o checkpoint.

- `Compact`: um snapshot manual captura somente registros/buckets. Restauração refaz placements e aceleração a partir dos registros; a projeção registrada refaz o cache das fontes restauradas.
- `FastRestore`: o histórico pode guardar roots COW locais. Um lease identifica binding, owner do histórico, boundary e geração. Ele nunca aparece nos bytes canônicos. Um lease ausente/expirado reconstrói a aceleração em vez de impedir o rollback.

`Records.SnapshotSlots` limita snapshots manuais e o pool local do binding; um slot adicional fica reservado para transações. A retenção local pode ser menor que a janela do WorldHistory. Expiração, truncate, reset e dispose liberam os leases daquele histórico, sem afetar outro owner. `LocalHistoryCount` e `LocalRestoreCount` permitem observar seu uso. Descarte os históricos e jobs antes dos owners.

O wire autônomo inclui o estado lógico do ordered map, preservando capacidade, ordem, free slots e os raws de Bounds, inclusive -0. Por padrão usa `FixedBounds2Policy`/`FixedBounds3Policy`; aceleração, workspace e revisões ficam fora dos pacotes. O payload `LPS1` contém limites lógicos e schemas dos codecs. A negociação completa do perfil numérico/geométrico e da política de chave pertence à assinatura do binding e ao protocolo do registry. Ao transportar um payload diretamente, negocie esse perfil antes da leitura.

## Codecs compactos e fronteira canônica

`SpatialBoundsNetworkCodec<FixedBounds2, SpatialBounds2NetworkAccess>` e sua variante 3D adaptam `FixedBoundsNetwork` ao codec determinístico existente. Aceitam VarInt, FixedWidth e BitPacked, preservam raws e -0 quando o perfil é exato e validam padding, geometria e perfil. Cada valor inclui oito bytes do fingerprint completo do descritor negociado. Bindings autônomos incluem os perfis completos de codecs configuráveis de chave, payload e Bounds via `ISpatialCodecFingerprint`, além dos schemas/versões. Um fingerprint configurado zero é rejeitado na criação do binding.

```csharp
var coordinates = new FixedNetworkProfile(FixedRuntime.Settings, FixedKind.Signed64, FixedCodec.VarInt);
var profile = new FixedBoundsNetworkProfile(new FixedVectorNetworkProfile(coordinates, dimensions: 3));
var codec = new SpatialBoundsNetworkCodec<FixedBounds3, SpatialBounds3NetworkAccess>(profile);
// Passe codec no argumento boundsCodec de SpatialStateBindings.Managed/Native.
```

Mesmo com `AllowLossy=true`, a escrita rejeita valores que ainda mudariam ao quantizar. Use `codec.TryCanonicalize` explicitamente no estado autoritativo, no mesmo boundary de todos os participantes, antes da indexação e do capture. A decodificação restaura aquele valor canônico. Quantização de transmissão pode encolher ou deslocar a caixa; ela não serve como envelope conservador exclusivo da aceleração.

## Adaptação a ECS e comandos

O assembly de [samples](Samples/SpatialIntegrationSamples.cs) é opcional (`autoReferenced=false`):

- `SpatialVolumeKey` combina `StableHandle` e `Volume`, distinguindo tanto volumes da mesma entidade quanto gerações de um slot reutilizado. `SpatialVolumePolicy` fornece igualdade/hash/comparer/codec e um fingerprint versionado.
- `SpatialArraySourceOwner` adapta um array autoritativo a leitura ordinal O(1). O aplicativo continua responsável pelo binding canônico do array. Chame `NotifyChanged` após mutações e restaurações, depois sincronize o índice Derived.
- `SpatialCommandApplication.TryApply` recebe um command buffer já produzido, faz merge e publica um batch atômico usando scratch do chamador. Os produtores devem terminar antes do merge.

Para uma integração completa de runner, input atrasado, replay, eventos e recuperação, consulte [SpatialRunnerTests](../../../../Tests/Deterministic/Spatial/SpatialRunnerTests.cs). O adaptador Native de uma fonte deve ser unmanaged e expor apenas buffers que o job pode ler; o token de identidade é criado previamente no host.

## Dimensionamento e reprodução

`SpatialMemory.TryMeasure<TKey,TData,TBounds>` calcula payload no MaxCapacity, inclusive páginas privadas para cada root retido e o spare transacional. Verifique esse orçamento antes de construir. Os constructors validam limites de índices, buckets, diretórios COW e workspace. `MemoryStats` mostra o payload realmente reservado e páginas alcançáveis por roots ativos/retidos. Números excluem overhead de GC/allocator e diretórios; query/pair workspace pertence ao chamador. `WorldHistory.LocalHistoryReservedBytes` informa o payload de sua tabela opcional de leases, separado da arena canônica.

Fontes de algoritmos são os kernels escritos à mão. Edite geradores, e não os arquivos `.Generated.cs`. `verify_spatial_generation.py` executa os oito geradores na ordem correta, verifica ausência de drift e cria metadados Unity ausentes com GUID estável. O harness `Standalone~/Spatial.Validation` roda em processos separados para MinMax e CenterExtents; `prepare_spatial_validation.py DEST --sync --players` atualiza o projeto Unity isolado sem apagar sua Library.

Os testes cobrem oráculos BigInteger, mutações aleatórias, geometria extrema, snapshots e imports atômicos, descritores derivados, recuperação, leases, pares, memória, readers Native paralelos e marcadores BurstDiscard. A matriz numérica executada cobre 480 combinações de representação, precisão, arredondamento, overflow e modo aritmético, com nove testes por combinação. A ampliação inclui hashes com colisões, comparadores personalizados, staging com cursores deslocados, leases antigos e zero GC em mutação/capture/import/restore. O runner/replay foi exercitado nos dois backends com command buffers, inputs atrasados/duplicados, seek, eventos e falhas repetidas.

Os benchmarks executados contêm 864 linhas para 1k/10k/100k objetos, 2D/3D, três estratégias, duas políticas de rollback, distribuições uniforme/agrupada, tamanhos semelhantes/variados e movimento de 1/10/100%. Os 432 cenários correspondentes Managed/Burst coincidiram em digest de consultas, bytes canônicos e payload de memória. Os tempos incluem uma passagem por cenário, custos frios e agendamento; são observações locais. Capture/restore Native é medido no host. O orçamento COW inclui todos os roots retidos e o spare transacional.

Os benchmarks de hotpaths acrescentam 3984 linhas em MinMax/CenterExtents, Managed/Burst, 2D/3D e três estratégias. Isolam fragmentação de slots, fontes ordinais/densas, revisão sem mudanças, batches unitários, cópia de páginas, pares parcialmente consumidos, fases dos codecs e reservas Grow. As 48 seleções passaram, e os 1968 cenários correspondentes entre backends coincidiram nas observações de resultados e payload. São sete amostras após dois aquecimentos, com os custos de despacho Native incluídos. Consulte [oportunidades e metodologia](../../../../Tools/Deterministic/SPATIAL_HOTPATH_OPPORTUNITIES.md) e [os dados arquivados](../../../../Tools/Deterministic/Reports/Spatial/Hotpaths/summary.json). Nenhuma otimização dos algoritmos foi aplicada nessa rodada.

A rodada de otimização posterior remove a limpeza por slot do scratch e sua área de ordenação não utilizada, deduplica objetos da grade pela primeira célula compartilhada, evita a varredura de overflow quando vazio, reduz a limpeza do workspace de sincronização, percorre a fonte ordinal pela extremidade mais próxima, reutiliza a medição validada no wire e preserva aceleração compartilhada em batches StaticBvh sem mudança geométrica. As observações canônicas e os checksums permanecem preservados nos cenários executados. Consulte [alterações e comparação](../../../../Tools/Deterministic/SPATIAL_OPTIMIZATION_RESULTS.md) e [os relatórios dessa rodada](../../../../Tools/Deterministic/Reports/Spatial/Optimizations/summary.json), incluindo variações e o controle de JIT.

Players x64 Mono e IL2CPP executam dois corpora nas duas representações. O segundo aplica atualizações em ordens diferentes, varia a cadência de rebuild e repete o replay em outra ordem. Cada palavra de resultados e bytes canônicos é comparada com Managed, em três ordens de agendamento por dimensão. Cada player exercita 72 cenários únicos e 5076 passos de ressimulação. Os corpora têm checksums versionados independentes. ARM64 não foi executado: não havia dispositivo conectado. Consulte os [relatórios arquivados](../../../../Tools/Deterministic/Reports/Spatial/implementation-summary.json) e os comandos do checklist para reproduzir a validação.
