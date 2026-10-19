# Patch ter-alteracao — dijkstra cedo + TrafegaViagem + mensagem de erro

Base: `transcolar/ter-alteracao` @ `cc324ae` (main)  
Conta: lucasvra via cursor-github MCP  
`gh auth status`: **não autenticado** — patch só local; PR via MCP depois do ok do Lucas.

## O que muda

1. **Program.cs / Otimizar** — logo após `req.Distancias = new Matriz(req);`, liga `req.Distancias.dijkstra = true;` para o reaproveitamento (e o resto do fluxo) usar fallback Dijkstra quando a aresta falta em `distancias_req`. Hoje isso só acontece em `Rota.TroncoPrincipal2`, depois do reaproveitamento.

2. **modelos/Tabelas.cs / TrafegaViagem** — remove o early-return `if (veiculo == TipoVeiculo) return true;` e passa a checar trechos finitos na sequência de paradas via o indexador `Matriz[IEnumerable<int>, int]` (já existente; soma trecho a trecho).

3. **Program.cs / AtualizaViagensReaproveitadas** — mensagem de erro inclui sentido, tipo_veiculo, sequenciais e vértices a→b.

## API Matriz (confirmada)

- `this[int v1, int v2, int veiculo]` — par a par; se aresta ausente e `dijkstra==true`, chama `DistanciaDijkstra`.
- `this[IEnumerable<int> vertices, int veiculo]` — soma consecutivos na ordem do IEnumerable; **não precisa** somar manualmente no caller.
- `dijkstra` é `public bool { get; internal set; } = false` — setável de `Program.cs` (mesmo assembly).

## Callers de TrafegaViagem (assinatura inalterada)

- `ModificarAlunos` — com mesmo `TipoVeiculo` (agora valida de verdade; cobre adaptação 51009524)
- `InserirAluno` / `MoverAluno` — mesmo tipo
- `EncontraVeiculo` — tipo possivelmente diferente

Nenhum caller precisa de ajuste de assinatura.

## Risco OrderBy(Sequencial)

`InserirAluno`/`MoverAluno` criam `Paradas` **sem** setar `Sequencial` (fica 0). `OrderBy(p => p.Sequencial)` colocaria a nova parada no início e invalidaria o caminho. Por isso o patch usa a **ordem atual da lista** (já é a sequência do caminho após Insert) em vez de `OrderBy`. Se no futuro Insert passar a preencher Sequencial, OrderBy fica equivalente.

## Riscos gerais

- Mais chamadas a `TER_DistanciaPontosDijkstra` (cache em memória mitiga repeats).
- `TrafegaViagem` pode rejeitar mais adaptações/inserções/associações de veículo (comportamento desejado).
- Com Dijkstra cedo, rotas novas também usam fallback antes de TroncoPrincipal2 (já setava true; agora é no-op lá).

## Próximo passo

Salvo em `/workspace/ter-alteracao-patch/`. Com ok do Lucas: branch + commit + PR via cursor-github MCP (gh CLI sem auth). Dados/matriz depois, conforme combinado.

## Arquivos neste diretório

| Caminho | Conteúdo |
|---|---|
| `0001-dijkstra-cedo-trafega-mensagem.patch` | Diff unificado aplicável com `git apply` sobre cc324ae |
| `patched/Program.cs` | Program.cs completo já alterado |
| `patched/Tabelas.cs` | Tabelas.cs completo já alterado (vai em `modelos/`) |
| `orig/` | Cópias do cc324ae para diff |
| `snippets/` | Trechos isolados para review rápido |

## Decisão OrderBy vs lista

Lucas pediu `OrderBy(p => p.Sequencial)`. Confirmado que `Matriz[IEnumerable<int>, int]` aceita isso. **Não** entrou no patch porque `InserirAluno`/`MoverAluno` inserem paradas com `Sequencial` default 0 — OrderBy quebraria o caminho. A ordem da lista já é a sequência correta nesses callers. Equivalente ao intent quando a lista veio do banco ordenada.
