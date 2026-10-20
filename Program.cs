using LinqToDB;
using LinqToDB.Data;
using Otimizacao;
using ter_otimizacao;

var mutex = new Mutex(false, @"Global\{8F6F0AC4-B9A1-45fd-5433-72F04E6BDE80}");
if (!mutex.WaitOne(TimeSpan.Zero, true)) return;

DataConnection.DefaultSettings = new TranscolarConfig();
Log.WriteLine("Iniciando serviço de alteração de rotas...");

while (true)
{
    var requisicoes = new List<Requisicoes>(0);
    try
    {
        using var db = new TranscolarDB("Transcolar");
#if !DEBUG
        requisicoes = db.Requisicoes.Where(r =>
            (r.TipoOtimizacao == TiposOtimizacao.Rotas ||
             r.TipoOtimizacao == TiposOtimizacao.Custos ||
             r.TipoOtimizacao == TiposOtimizacao.ParadoesFrotaMinima) &&
            (r.Situacao == 3 || r.Situacao == 4) &&
            r.Versao > 0).ToList();
#else
        Log.WriteLine("Modo DEBUG: processando apenas a requisicao 11001411");
        requisicoes = db.Requisicoes.Where(r => r.Requisicao == 11001411 && r.Versao == 1).ToList();
#endif
        Log.WriteLine($"{requisicoes.Count} Requisições encontradas para processamento");
    }
    catch (Exception e)
    {
        Log.WriteLine($"Erro ao acessar o banco: {e.Message}");
    }


    foreach (var requisicao in requisicoes)
    {
        try
        {
#if !DEBUG

            using var db = new TranscolarDB("Transcolar");
            db.Requisicoes.Where(r => r.Requisicao == requisicao.Requisicao && r.Versao == requisicao.Versao)
                .Set(r => r.Situacao, (short)4)
                .Update();
#endif
            Otimizar(requisicao);
        }
        catch (Exception e)
        {
            try
            {
                using var db = new TranscolarDB("Transcolar");
                db.Requisicoes.Where(r => r.Requisicao == requisicao.Requisicao && r.Versao == requisicao.Versao)
                    .Set(r => r.Situacao, (short)5)
                    .Set(r => r.DescricaoError, e.ToString())
                    .Update();
            }
            catch
            {
                Log.WriteLine($"Erro ao atualizar a requisicao {requisicao.Requisicao}: {e}");
            }
            Log.WriteLine($"Erro ao processar requisicao {requisicao.Requisicao}: {e}");
        }
    }

    Thread.Sleep(1000);
}



static void Otimizar(Requisicoes req)
{
    Log.WriteLine($"Inicialndo alteração da requisicao {req.Requisicao}");
    var versao = req.Versao ?? throw new InvalidOperationException(
        $"A requisicao {req.Requisicao} nao possui versao.");
    if (versao <= 0)
        throw new InvalidOperationException(
            $"A alteracao exige uma versao maior que zero (requisicao {req.Requisicao}, versao {versao}).");

    using var db = new TranscolarDB("Transcolar");
    db.Viagens.Where(v => v.Requisicao == req.Requisicao && v.Versao == versao).Delete();
    db.Paradas.Where(v => v.Requisicao == req.Requisicao && v.Versao == versao).Delete();

    var alunosAtuais = db.Alunos.Where(a => a.Requisicao == req.Requisicao && a.TipoOutlier == 0 && a.Versao == versao).ToList();
    var alunosAntigos = db.Alunos.Where(a => a.Requisicao == req.Requisicao && a.TipoOutlier == 0 && a.Versao == versao - 1).ToList();
    var escolas = db.Escolas.Where(e => e.Requisicao == req.Requisicao && e.Versao == versao).ToList();
    var veiculos = new List<Veiculos>();
    var veiculosParticulares = db.Veiculos.Where(v => v.Requisicao == req.Requisicao).ToList();
    var turnos = db.Turnos.Where(t => t.Requisicao == req.Requisicao)
        .ToList()
        .Select(t =>
        {
            t.HrFim = t.HrFim.AddDays(1);
            t.HrInicio = t.HrInicio.AddDays(1);
            return t;
        })
        .ToList();
    // 0008: Horario = linha de turnos_req do aluno (alunos_req.turno é Turnos.Id). Usado por
    // LimiarCaminhada para reconhecer o noturno pelo código (Turnos.Turno == 3), não pelo id.
    foreach (var aluno in alunosAtuais.Concat(alunosAntigos))
        aluno.Horario = turnos.FirstOrDefault(t => t.Id == aluno.Turno)!;

    var tiposVeiculo = db.TiposVeiculo.Where(t => t.Requisicao == req.Requisicao).ToList();

    req.TiposVeiculo = tiposVeiculo;
    req.Distancias = new Matriz(req);
    req.Distancias.dijkstra = true;

    var viagensAntigas = db.Viagens.Where(v => v.Requisicao == req.Requisicao && v.Versao == versao - 1 && v.Tipo == 1).ToList();

    if (alunosAntigos.Count > 0 && viagensAntigas.Count == 0)
        throw new InvalidOperationException(
            $"A versao anterior {versao - 1} possui alunos, mas nao possui viagens para reaproveitamento.");

    req.NovaViagem = viagensAntigas.Select(v => (int)v.Viagem).DefaultIfEmpty(0).Max() + 1;

    foreach (var viagem in viagensAntigas)
        viagem.Paradas = db.Paradas.Where(p => p.Viagem == viagem.Viagem && p.Requisicao == viagem.Requisicao && p.Versao == viagem.Versao).OrderBy(p => p.Sequencial).ToList();

    AtualizaVeiculos(viagensAntigas, veiculos, req);

    // 0006: ida e volta decididas juntas. Fase 1 (remover/adaptar in loco) nos dois sentidos,
    // depois a entrada de alunos em viagens existentes é decidida por aluno para ida E volta
    // (só entra se couber nas duas), e só então cada sentido é finalizado/otimizado.
    var turnoPorId = Insercao.MapaTurnoPorId(turnos);
    var (viagensIda, naoAdaptadosIda) = PreparaSentido(req, alunosAtuais, alunosAntigos, escolas, turnoPorId, sentido: 1, viagensAntigas); //IDA
    var (viagensVolta, naoAdaptadosVolta) = PreparaSentido(req, alunosAtuais, alunosAntigos, escolas, turnoPorId, sentido: 2, viagensAntigas); //VOLTA
    SincronizaAdaptacoesRejeitadas(req, viagensIda, naoAdaptadosIda, alunosAtuais);
    // 0010: aluno trocado in loco vai para outra viagem compatível se o desvio lá (ida+volta, critério
    // da 0006) for menor que o que ele custa na viagem herdada.
    RealocaTrocasInLoco(req, viagensIda, viagensVolta, AlunosModificados(alunosAntigos, alunosAtuais), turnoPorId);

    var alunosParaInserir = alunosAtuais.Where(at => alunosAntigos.All(an => an.Seq != at.Seq))
        .Concat(naoAdaptadosIda).Concat(naoAdaptadosVolta)
        .GroupBy(a => a.Seq).Select(g => g.First())
        .ToList();
    var alunosOtm = InserirAlunosIdaVolta(req, viagensIda, viagensVolta, alunosParaInserir, turnoPorId);

    var ida = FinalizaSentido(req, alunosAtuais, escolas, veiculos, veiculosParticulares, turnos, sentido: 1, viagensIda, alunosOtm); //IDA
    var volta = FinalizaSentido(req, alunosAtuais, escolas, veiculos, veiculosParticulares, turnos, sentido: 2, viagensVolta, alunosOtm); //VOLTA

    foreach (var veiculo in veiculos)
    {
        short seq = 1;
        veiculo.Intervalos = veiculo.Intervalos.OrderBy(i => i.intervalo.Inicio).ToList();

        for (int i = 0; i < veiculo.Intervalos.Count; i++)
        {
            var (intervalo, verticeIni, verticeFim, viagem) = veiculo.Intervalos[i];
            viagem.SequencialVeiculo = seq;
            viagem.PlacaVeiculo = veiculo.Placa;

            seq++;
            if (i < veiculo.Intervalos.Count - 1)
            {
                var (_, verticeIniProx, _, _) = veiculo.Intervalos[i + 1];
                if (verticeFim != verticeIniProx) seq++;
            }
        }
    }

    foreach (var viagem in ida.Union(volta))
        viagem.Insert();

    foreach (var veiculo in veiculos)
    {
        veiculo.CriaAproveitamento(req);
        Log.WriteLine($"\tVeiculo {veiculo.Placa} tipo {veiculo.TipoVeiculo} usado em {veiculo.Intervalos.Count} viagens");
    }

    // Timeout opcional (segundos) só para TER_PreencheOtimizacao. Sem a variável (ou <= 0)
    // mantém o comportamento atual (Command Timeout=0 da connection string = sem limite).
    // Se o timeout estourar, o Npgsql cancela o comando, o Postgres faz rollback da função
    // e a exceção cai no catch do loop principal (situacao 5 + descricao_error).
    var timeoutPreenche = int.TryParse(Environment.GetEnvironmentVariable("TER_PREENCHE_TIMEOUT_SEC"), out var timeoutEnv) && timeoutEnv > 0 ? timeoutEnv : 0;
    if (timeoutPreenche > 0) db.CommandTimeout = timeoutPreenche;

    var viagensPreencher = db.Viagens.Count(v => v.Requisicao == req.Requisicao && v.Versao == versao);
    Log.WriteLine($"Iniciando TER_PreencheOtimizacao({req.Requisicao}, {versao}) para {viagensPreencher} viagens " +
                  $"(timeout: {(timeoutPreenche > 0 ? timeoutPreenche + " s" : "sem limite")})");
    var cronometro = System.Diagnostics.Stopwatch.StartNew();
    var preenchimentoConcluido = db.Execute<bool>(
        $"SELECT requisicao.TER_PreencheOtimizacao({req.Requisicao}, {versao});");
    cronometro.Stop();
    Log.WriteLine($"TER_PreencheOtimizacao({req.Requisicao}, {versao}) retornou {preenchimentoConcluido} em {cronometro.Elapsed:hh\\:mm\\:ss}");
    if (!preenchimentoConcluido)
        throw new InvalidOperationException(
            $"TER_PreencheOtimizacao retornou falha para a requisicao {req.Requisicao}, versao {versao}.");

    db.Requisicoes.Where(r => r.Requisicao == req.Requisicao && r.Versao == req.Versao)
        .Set(r => r.Situacao, (short)12)
        .Update();

    Log.WriteLine("Otimização concluída");


}

static (List<Viagens> viagens, List<Alunos> naoAdaptados) PreparaSentido(Requisicoes req, List<Alunos> alunosAtuais, List<Alunos> alunosAntigos, List<Escolas> escolas, IReadOnlyDictionary<int, short> turnoPorId, short sentido, List<Viagens> viagensAntigas)
{
    var viagens = viagensAntigas.Where(v => v.Sentido == sentido).ToList();

    // 0006: escolas atualizadas ANTES de adaptar/inserir, para que o filtro de escola e o
    // desvio usem o vértice atual da escola (antes era feito só depois das inserções).
    AtualizaEscolasDasViagens(viagens, escolas);

    RemoverAlunos(viagens, alunosAntigos.Where(an => alunosAtuais.All(at => at.Seq != an.Seq)).ToList(), req);
    var naoAdaptados = ModificarAlunos(viagens, AlunosModificados(alunosAntigos, alunosAtuais), req, turnoPorId);

    return (viagens, naoAdaptados);
}

// 0005/0006: se a adaptação in loco foi rejeitada na volta depois de aceita na ida, desfaz
// também na ida, para o aluno ser realocado nos dois sentidos (nunca separado entre veículos).
static void SincronizaAdaptacoesRejeitadas(Requisicoes req, List<Viagens> viagensIda, List<Alunos> naoAdaptadosIda, List<Alunos> alunosAtuais)
{
    if (req.AdaptacoesRejeitadas.Count == 0) return;

    var jaNaLista = naoAdaptadosIda.Select(a => a.Seq).ToHashSet();
    foreach (var viagem in viagensIda)
    {
        var removidas = viagem.Paradas.RemoveAll(p => p.Tipo == 1 && req.AdaptacoesRejeitadas.Contains(p.Codigo));
        if (removidas > 0) viagem.RenumeraParadas();
    }

    foreach (var aluno in alunosAtuais.Where(a => req.AdaptacoesRejeitadas.Contains(a.Seq) && !jaNaLista.Contains(a.Seq)))
    {
        naoAdaptadosIda.Add(aluno);
        Log.WriteLine($"\tAluno {aluno.Seq}: adaptação rejeitada na volta, desfeita também na ida; será realocado", ConsoleColor.Yellow);
    }
}

static List<Viagens> FinalizaSentido(Requisicoes req, List<Alunos> alunosAtuais, List<Escolas> escolas, List<Veiculos> veiculos, List<Veiculos> veiculosParticulares, List<Turnos> turnos, short sentido, List<Viagens> viagens, List<Alunos> alunosOtm)
{
    foreach (var veiculo in veiculosParticulares)
        veiculo.TipoVeiculo = Veiculos.TipoVeiculoPelaCapacidade(veiculo.Capacidade, req);

    foreach (var escola in escolas)
        escola.turnos = turnos.Where(t => t.Escola == escola.Seq).ToList();

    RemoveViagensSemAlunos(viagens, veiculos);
    AtualizaViagensReaproveitadas(viagens, alunosAtuais, req);

    foreach (var viagem in viagens)
    {
        viagem.Versao = req.Versao ?? 0;
        foreach (var parada in viagem.Paradas)
            parada.Versao = req.Versao ?? 0;
    }

    var rotas = new List<(List<Rota>, short)>();
    var novasViagens = new List<Viagens>();
    foreach (var turno in turnos.GroupBy(t => t.Turno))
    {
        Log.WriteLine($"Otimizando turno {turno.First().Descricao}");
        var alunosTurno = alunosOtm.Where(a => turno.Any(t => t.Id == a.Turno)).ToList();
        var rotasTurno = OtimizaTurno(req, turno.Key, alunosTurno, escolas);

        rotas.Add((rotasTurno, turno.Key));
    }

    foreach (var (rotasTurno, turno) in rotas)
    {
        if (!rotasTurno.Any()) continue;

        Parallel.ForEach(rotasTurno, v =>
        {
            v.Reordenacao();
            v.TroncoPrincipal1();
        });

        Parallel.ForEach(rotasTurno, v => v.TroncoPrincipal2());

        var viagensTurno = rotasTurno.Select(c => c.ToViagem(sentido, turno)).Where(v => v.AlunosAtendidos > 0).ToList();
        novasViagens.AddRange(viagensTurno);

        Log.WriteLine($"\tGeradas {viagensTurno.Count} viagens");
    }

    AssociaVeiculos(novasViagens, veiculos, veiculosParticulares, req);

    return viagens.Union(novasViagens).ToList();
}

static void AtualizaVeiculos(List<Viagens> viagens, List<Veiculos> veiculos, Requisicoes req)
{
    foreach (var viagem in viagens)
    {
        var veiculo = veiculos.FirstOrDefault(v => v.Placa == viagem.PlacaVeiculo);
        if (veiculo == null)
        {
            veiculo = new Veiculos()
            {
                Placa = viagem.PlacaVeiculo,
                TipoVeiculo = viagem.TipoVeiculo,
                Capacidade = viagem.CapacidadeVeiculo,
                AnoFabricacao = viagem.AnoFabricacaoVeiculo,
                Intervalos = new List<(IntervaloTempo intervalo, int verticeIni, int verticeFim, Viagens viagem)>(),
                VeiculoEspecial = viagem.VeiculoEspecial,
                NumViagem = 1
            };

            veiculo.Intervalos.Add((viagem.IntervaloViagem, viagem.Paradas.First().Vertice, viagem.Paradas.Last().Vertice, viagem));
            veiculos.Add(veiculo);
        }
        else
        {
            veiculo.NumViagem++;
            veiculo.Intervalos.Add((viagem.IntervaloViagem, viagem.Paradas.First().Vertice, viagem.Paradas.Last().Vertice, viagem));
        }
    }

    foreach (var veiculo in veiculos)
    {
        short seq = 1;
        veiculo.Intervalos = veiculo.Intervalos.OrderBy(i => i.intervalo.Inicio).ToList();

        for (int i = 0; i < veiculo.Intervalos.Count; i++)
        {
            var (intervalo, verticeIni, verticeFim, viagem) = veiculo.Intervalos[i];

            seq++;
            if (i < veiculo.Intervalos.Count - 1)
            {
                var (_, verticeIniProx, _, _) = veiculo.Intervalos[i + 1];
                if (verticeFim != verticeIniProx) seq++;
            }
        }
    }
}

// 0006: entrada de alunos em viagens existentes.
//  - filtro OBRIGATÓRIO de escola (parada tipo 2 da escola do aluno) e turno (Turnos.Id -> Turnos.Turno);
//  - posição respeitando a escola do aluno (ida: antes; volta: depois);
//  - viagem de MENOR desvio entre as compatíveis (antes: a primeira da lista que aceitava);
//  - duração ATUAL (soma da matriz nas paradas atuais) + desvio <= TempoMaximo
//    (antes: viagem.Duracao da versão anterior, não atualizada entre inserções);
//  - ida e volta juntas: só entra se houver candidato nos dois sentidos; senão vai para OtimizaTurno.
static List<Alunos> InserirAlunosIdaVolta(Requisicoes req, List<Viagens> viagensIda, List<Viagens> viagensVolta, List<Alunos> alunos, IReadOnlyDictionary<int, short> turnoPorId)
{
    var alunosOtm = new List<Alunos>();
    float custo(Viagens v, int a, int b) => req.Distancias[a, b, v.TipoVeiculo];
    float caminhada(int a, int b) => req.Distancias[a, b, 0];

    foreach (var aluno in alunos)
    {
        var limiar = aluno.LimiarCaminhada(req);
        var ci = Insercao.MelhorCandidato(viagensIda, aluno, turnoPorId, custo, caminhada, req.ConsideraSentido, limiar, req.TempoMaximo);
        var cv = ci == null ? null : Insercao.MelhorCandidato(viagensVolta, aluno, turnoPorId, custo, caminhada, req.ConsideraSentido, limiar, req.TempoMaximo);

        if (ci == null || cv == null)
        {
            alunosOtm.Add(aluno);
            continue;
        }

        var paradaIda = AplicaCandidato(req, aluno, ci);
        var paradaVolta = paradaIda == null ? null : AplicaCandidato(req, aluno, cv);
        if (paradaIda == null || paradaVolta == null)
        {
            if (paradaIda != null)
            {
                ci.Viagem.Paradas.Remove(paradaIda);
                ci.Viagem.RenumeraParadas();
            }
            alunosOtm.Add(aluno);
            continue;
        }

        Log.WriteLine($"\tAluno {aluno.Seq} (escola {aluno.Escola}) -> ida {ci.Viagem.Viagem:000} ({ci.Tipo}, +{ci.Desvio:0.0} min, {ci.DuracaoAtual + ci.Desvio:0.0}/{req.TempoMaximo}), " +
                      $"volta {cv.Viagem.Viagem:000} ({cv.Tipo}, +{cv.Desvio:0.0} min, {cv.DuracaoAtual + cv.Desvio:0.0}/{req.TempoMaximo})");
    }

    return alunosOtm;
}

// 0010: troca in loco → melhor viagem. Para cada aluno adaptado in loco (presente na ida e na volta
// herdadas), compara o custo marginal dele nas herdadas com o melhor candidato (Insercao.MelhorCandidato,
// 0006: escola/turno, lotação, janela da escola, TempoMaximo) nas outras viagens, ida e volta juntas.
// Se for menor, tira a parada das herdadas (renumera; viagem que ficar sem alunos sai em
// RemoveViagensSemAlunos, no FinalizaSentido) e aplica os dois candidatos; se a aplicação falhar,
// desfaz tudo. Não há teto de km aqui (decisão do Lucas).
static void RealocaTrocasInLoco(Requisicoes req, List<Viagens> viagensIda, List<Viagens> viagensVolta,
    List<(Alunos aluno, bool adaptavel)> modificados, IReadOnlyDictionary<int, short> turnoPorId)
{
    float custo(Viagens v, int a, int b) => req.Distancias[a, b, v.TipoVeiculo];
    float caminhada(int a, int b) => req.Distancias[a, b, 0];

    foreach (var aluno in modificados.Where(m => m.adaptavel).Select(m => m.aluno))
    {
        if (req.AdaptacoesRejeitadas.Contains(aluno.Seq)) continue;
        var hi = viagensIda.FirstOrDefault(v => v.Paradas.Any(p => p.Tipo == 1 && p.Codigo == aluno.Seq));
        var hv = viagensVolta.FirstOrDefault(v => v.Paradas.Any(p => p.Tipo == 1 && p.Codigo == aluno.Seq));
        if (hi == null || hv == null) continue;

        var r = Insercao.MelhorRealocacaoInLoco(hi, hv, aluno, viagensIda, viagensVolta, turnoPorId, custo, caminhada,
                                                req.ConsideraSentido, aluno.LimiarCaminhada(req), req.TempoMaximo);
        if (r == null) continue;

        var pi = hi.Paradas.First(p => p.Tipo == 1 && p.Codigo == aluno.Seq);
        var pv = hv.Paradas.First(p => p.Tipo == 1 && p.Codigo == aluno.Seq);
        var (ii, iv) = (hi.Paradas.IndexOf(pi), hv.Paradas.IndexOf(pv));
        hi.Paradas.Remove(pi);
        hv.Paradas.Remove(pv);

        var novaIda = AplicaCandidato(req, aluno, r.Ida);
        var novaVolta = novaIda == null ? null : AplicaCandidato(req, aluno, r.Volta);
        if (novaIda == null || novaVolta == null)
        {
            if (novaIda != null) { r.Ida.Viagem.Paradas.Remove(novaIda); r.Ida.Viagem.RenumeraParadas(); }
            hi.Paradas.Insert(ii, pi);
            hv.Paradas.Insert(iv, pv);
            continue;
        }

        hi.RenumeraParadas();
        hv.RenumeraParadas();
        Log.WriteLine($"\tAluno {aluno.Seq}: troca in loco realocada {hi.Viagem:000}/{hv.Viagem:000} (+{r.DesvioHerdada:0.0} min) -> " +
                      $"ida {r.Ida.Viagem.Viagem:000} ({r.Ida.Tipo}, +{r.Ida.Desvio:0.0}) / volta {r.Volta.Viagem.Viagem:000} " +
                      $"({r.Volta.Tipo}, +{r.Volta.Desvio:0.0})");
    }
}

static Paradas? AplicaCandidato(Requisicoes req, Alunos aluno, CandidatoInsercao c)
{
    var viagem = c.Viagem;
    var parada = new Paradas()
    {
        Codigo = aluno.Seq,
        Nome = aluno.Nome,
        Distancia = c.Tipo == TipoInsercao.Mover ? aluno.DistanciaVia + c.Caminhada : aluno.DistanciaParada,
        Vertice = c.Vertice,
        Viagem = viagem.Viagem,
        Requisicao = viagem.Requisicao,
        Versao = req.Versao ?? 0,
        Tipo = 1,
        Carregamento = (short)(viagem.Sentido == 1 ? 1 : 2)
    };

    viagem.Paradas.Insert(c.Posicao, parada);
    if (!viagem.TrafegaViagem(viagem.TipoVeiculo, req.Distancias))
    {
        viagem.Paradas.Remove(parada);
        return null;
    }

    viagem.RenumeraParadas();
    return parada;
}

static List<(Alunos aluno, bool adaptavel)> AlunosModificados(List<Alunos> alunosAntigos, List<Alunos> alunosAtuais)
{
    var modificados = new List<(Alunos aluno, bool adaptavel)>();
    var pares = from alunoAntigo in alunosAntigos
                join alunoAtual in alunosAtuais on alunoAntigo.Seq equals alunoAtual.Seq
                select (AlunoAntigo: alunoAntigo, AlunoAtual: alunoAtual);

    foreach (var (an, at) in pares)
    {
        if (at.VerticeParada != an.VerticeParada && at.Turno == an.Turno && at.Escola == an.Escola) modificados.Add((at, true));
        else if (at.VerticeParada != an.VerticeParada || at.Turno != an.Turno || at.Escola != an.Escola) modificados.Add((at, false));
    }

    return modificados;
}

// Critério de tempo da adaptação in loco, alinhado à inserção (Insercao.MelhorNaViagem): o desvio e a duração
// resultante da viagem (soma dos trechos na ordem atual) têm de ficar abaixo de TempoMaximo.
// Adaptação que não piora a viagem é sempre aceita (viagens herdadas acima do limite).
static bool AdaptacaoRespeitaTempo(Requisicoes req, float tempoAntes, float tempoDepois)
{
    if (!float.IsFinite(tempoDepois)) return false;

    var desvio = tempoDepois - (float.IsFinite(tempoAntes) ? tempoAntes : 0f);
    if (desvio <= 0) return true;

    return desvio < req.TempoMaximo && tempoDepois < req.TempoMaximo;
}

static List<Alunos> ModificarAlunos(List<Viagens> viagens, List<(Alunos aluno, bool adaptavel)> alunos, Requisicoes req, IReadOnlyDictionary<int, short> turnoPorId)
{
    var alunosAdaptaveis = alunos.Where(a => a.adaptavel).Select(a => a.aluno).ToList();
    var alunosNaoAdaptaveis = alunos.Where(a => !a.adaptavel).Select(a => a.aluno).ToList();
    var naoAdaptaveisPorSeq = alunosNaoAdaptaveis.Select(a => a.Seq).ToHashSet();

    foreach (var viagem in viagens)
        viagem.Paradas.RemoveAll(p => p.Tipo == 1 && naoAdaptaveisPorSeq.Contains(p.Codigo));

    foreach (var viagem in viagens)
    {
        var adaptaveisPorSeq = alunosAdaptaveis.ToDictionary(a => a.Seq);
        foreach (var modificar in viagem.Paradas.Where(p => p.Tipo == 1 && adaptaveisPorSeq.ContainsKey(p.Codigo)).ToList())
        {
            var aluno = adaptaveisPorSeq[modificar.Codigo];
            alunosAdaptaveis.Remove(aluno);

            var vertice = modificar.Vertice;
            var tempoAntes = req.Distancias[viagem.Paradas.Select(p => p.Vertice), viagem.TipoVeiculo];
            modificar.Vertice = aluno.VerticeParada;
            var tempoDepois = req.Distancias[viagem.Paradas.Select(p => p.Vertice), viagem.TipoVeiculo];

            // Viagem pareada (ida/volta do mesmo veículo): se a adaptação já foi rejeitada no
            // outro sentido, rejeita aqui também para não separar o aluno entre veículos.
            var rejeitadaNoOutroSentido = req.AdaptacoesRejeitadas.Contains(aluno.Seq);

            // 0006: a viagem tem de atender a escola e o turno do aluno (defesa: vale também in loco).
            if (!rejeitadaNoOutroSentido &&
                Insercao.ViagemCompativel(viagem, aluno, turnoPorId) &&
                viagem.TrafegaViagem(viagem.TipoVeiculo, req.Distancias) &&
                AdaptacaoRespeitaTempo(req, tempoAntes, tempoDepois))
            {
                modificar.Vertice = aluno.VerticeParada;
                modificar.Codigo = aluno.Seq;
                modificar.Distancia = aluno.DistanciaParada;
                modificar.Nome = aluno.Nome;

                // 0007: a troca in loco mantém a posição antiga; se o novo vértice já existe em outro
                // ponto da viagem (ou o aluno estava no meio de um grupo no vértice antigo, Y–X–Y),
                // isso gera revisita. Reposiciona na mesma viagem quando não piora a duração.
                // 0009: nem o km (índice 0 = distancias_req.distancia em metros; fora da matriz, fallback
                // Dijkstra com veículo 0 = distância física). A troca in loco em si não muda de critério.
                if (Insercao.ReposicionaParada(viagem, modificar, aluno.Escola!.Value,
                        (a, b) => req.Distancias[a, b, viagem.TipoVeiculo], req.TempoMaximo,
                        (a, b) => req.Distancias[a, b, 0]))
                    Log.WriteLine($"\tAluno {aluno.Seq}: parada reposicionada na viagem {viagem.Viagem:000} (evita revisita)");
            }
            else
            {
                // Desfaz a troca, tira o aluno desta viagem e devolve para o fluxo normal
                // (InserirAlunosIdaVolta -> Mover/Inserir; se não couber, OtimizaTurno).
                modificar.Vertice = vertice;
                viagem.Paradas.Remove(modificar);
                req.AdaptacoesRejeitadas.Add(aluno.Seq);
                alunosNaoAdaptaveis.Add(aluno);
                Log.WriteLine(
                    $"\tAluno {aluno.Seq} não adaptado in loco na viagem {viagem.Viagem:000} " +
                    $"(vértice {vertice}->{aluno.VerticeParada}, tempo {tempoAntes:0.0}->{tempoDepois:0.0} min, " +
                    $"máximo {req.TempoMaximo}{(rejeitadaNoOutroSentido ? ", rejeitado no outro sentido" : "")}); será realocado",
                    ConsoleColor.Yellow);
            }

        }
    }

    return alunosNaoAdaptaveis;
}

static void RemoverAlunos(List<Viagens> viagens, List<Alunos> alunos, Requisicoes req)
{
    var alunosRemovidos = alunos.Select(a => a.Seq).ToHashSet();
    foreach (var viagem in viagens)
        viagem.Paradas.RemoveAll(p => p.Tipo == 1 && alunosRemovidos.Contains(p.Codigo));
}

static void RemoveViagensSemAlunos(List<Viagens> viagens, List<Veiculos> veiculos)
{
    var viagensVazias = viagens.Where(v => v.Paradas.All(p => p.Tipo != 1)).ToHashSet();
    if (viagensVazias.Count == 0) return;

    foreach (var veiculo in veiculos)
    {
        var removidas = veiculo.Intervalos.RemoveAll(i => viagensVazias.Contains(i.viagem));
        veiculo.NumViagem = Math.Max(0, veiculo.NumViagem - removidas);
    }

    foreach (var viagem in viagensVazias)
        Log.WriteLine($"\tDescartando viagem {viagem.Viagem:000}: nenhum aluno permaneceu após a alteração");

    viagens.RemoveAll(viagensVazias.Contains);
}

static void AtualizaEscolasDasViagens(List<Viagens> viagens, List<Escolas> escolasAtuais)
{
    var escolasPorSeq = escolasAtuais.ToDictionary(e => e.Seq);

    foreach (var viagem in viagens)
    {
        viagem.Paradas.RemoveAll(p => p.Tipo == 2 && !escolasPorSeq.ContainsKey(p.Codigo));

        foreach (var parada in viagem.Paradas.Where(p => p.Tipo == 2))
        {
            var escola = escolasPorSeq[parada.Codigo];
            parada.Vertice = escola.Vertice;
            parada.Nome = escola.Nome;
        }
    }
}

static void AtualizaViagensReaproveitadas(List<Viagens> viagens, List<Alunos> alunosAtuais, Requisicoes req)
{
    var alunosPorSeq = alunosAtuais.ToDictionary(a => a.Seq);

    foreach (var viagem in viagens)
    {
        // NÃO reordenar por Sequencial: InserirAlunosIdaVolta/AplicaCandidato inserem paradas novas com
        // Sequencial=0 na posição correta da lista; OrderBy(Sequencial) as jogava para o início
        // da viagem (idas e voltas, volta começando fora da escola). A ordem da lista em memória
        // é a usada por TrafegaViagem e pela checagem de tempo, e é a ordem que deve ser gravada.
        var alunosViagem = viagem.Paradas
            .Where(p => p.Tipo == 1 && alunosPorSeq.ContainsKey(p.Codigo))
            .Select(p => alunosPorSeq[p.Codigo])
            .ToList();

        viagem.AlunosAtendidos = (short)alunosViagem.Count;
        viagem.AlunosEstaduais = (short)alunosViagem.Count(a => a.Administracao == 1);
        viagem.AlunosMunicipais = (short)alunosViagem.Count(a => a.Administracao == 2);
        viagem.OcupacaoMaxima = (short)alunosViagem.Count;
        viagem.Monitor = alunosViagem.Any(a => a.TipoEnsino is 1 or 2);
        viagem.VeiculoEspecial = alunosViagem.Any(a => a.VeiculoEspecial);

        // 0007: um bloco por vértice (herdadas inclusive: a v1 renumera e expõe Y–X–Y que viesse da
        // troca in loco, da inserção ou da ordem herdada). Feito antes de renumerar e recalcular tempos.
        // Guarda: só reagrupa se a duração final <= TempoMaximo (ou, se já estourava, se não piorar).
        // 0009: e só se não piorar tempo nem km (índice 0 da matriz / fallback) em relação à ordem atual.
        var fundidos = Insercao.AgrupaVerticesIguais(viagem,
            seq => alunosPorSeq.TryGetValue(seq, out var al) ? al.Escola : null,
            (a, b) => req.Distancias[a, b, viagem.TipoVeiculo], req.TempoMaximo,
            (a, b) => req.Distancias[a, b, 0]);
        if (fundidos > 0)
            Log.WriteLine($"\tViagem {viagem.Viagem:000}: {fundidos} vértice(s) reagrupado(s) num só bloco");

        viagem.RenumeraParadas();

        var duracaoMinutos = 0d;
        if (viagem.Sentido == 1)
        {
            for (var i = viagem.Paradas.Count - 2; i >= 0; i--)
            {
                var minutos = req.Distancias[viagem.Paradas[i].Vertice, viagem.Paradas[i + 1].Vertice, viagem.TipoVeiculo];
                if (!float.IsFinite(minutos))
                    throw new InvalidOperationException(
                        $"Trecho inacessível na viagem reaproveitada {viagem.Viagem} da requisição {req.Requisicao} " +
                        $"(sentido={viagem.Sentido}, tipo_veiculo={viagem.TipoVeiculo}, " +
                        $"paradas {viagem.Paradas[i].Sequencial}->{viagem.Paradas[i + 1].Sequencial}, " +
                        $"vértices {viagem.Paradas[i].Vertice}->{viagem.Paradas[i + 1].Vertice}).");

                duracaoMinutos += minutos;
                viagem.Paradas[i].Tempo = SomaMinutos(viagem.Paradas[i + 1].Tempo, -minutos);
            }
        }
        else
        {
            for (var i = 1; i < viagem.Paradas.Count; i++)
            {
                var minutos = req.Distancias[viagem.Paradas[i - 1].Vertice, viagem.Paradas[i].Vertice, viagem.TipoVeiculo];
                if (!float.IsFinite(minutos))
                    throw new InvalidOperationException(
                        $"Trecho inacessível na viagem reaproveitada {viagem.Viagem} da requisição {req.Requisicao} " +
                        $"(sentido={viagem.Sentido}, tipo_veiculo={viagem.TipoVeiculo}, " +
                        $"paradas {viagem.Paradas[i - 1].Sequencial}->{viagem.Paradas[i].Sequencial}, " +
                        $"vértices {viagem.Paradas[i - 1].Vertice}->{viagem.Paradas[i].Vertice}).");

                duracaoMinutos += minutos;
                viagem.Paradas[i].Tempo = SomaMinutos(viagem.Paradas[i - 1].Tempo, minutos);
            }
        }

        viagem.Duracao = TimeSpan.FromMinutes(duracaoMinutos);
    }
}

static TimeSpan SomaMinutos(TimeSpan horario, double minutos)
{
    const double minutosDia = 24 * 60;
    var total = (horario.TotalMinutes + minutos) % minutosDia;
    if (total < 0) total += minutosDia;
    return TimeSpan.FromMinutes(total);
}

static List<Rota> OtimizaTurno(Requisicoes req, short turno, List<Alunos> alunosTurno, List<Escolas> escolas)
{
    var rotas = new List<Rota>();
    foreach (var escola in escolas)
    {
        var alunosEscola = alunosTurno.Where(a => a.Escola == escola.Seq).ToList();
        if (alunosEscola.Any()) rotas.AddRange(OtimizaEscola(req, alunosEscola, escola, turno));
    }

    Parallel.ForEach(rotas, v =>
    {
        v.Reordenacao();
        v.TroncoPrincipal1();
    });


    var endLoop = false;
    var maxCandidatos = 10;
    while (true)
    {
        var analise = new List<(Rota, Rota, Rota)>();

        Parallel.For(0, rotas.Count, i =>
        {
            for (var j = i + 1; j < rotas.Count; j++)
            {
                var c1 = rotas[i];
                var c2 = rotas[j];
                var c3 = c1.Mistura(req, turno, c2);

                lock (analise) analise.Add((c1, c2, c3));
            }
        });

        // Alteracoes sempre usam a mesma heuristica de agrupamento do tipo 5,
        // independentemente do objetivo usado na otimizacao original.
        analise = OrdenaCandidatosComoTipo5(analise);


        var candidatos = new List<(Rota, Rota, Rota)>();
        var removidos = new HashSet<Rota>();

        if (maxCandidatos > 1) maxCandidatos--;

        foreach (var (c1e, c2e, c12e) in analise)
        {
            if (removidos.Contains(c1e) || removidos.Contains(c2e)) continue;
            candidatos.Add((c1e, c2e, c12e));
            removidos.Add(c1e);
            removidos.Add(c2e);
            if (candidatos.Count >= maxCandidatos) break;
        }

        if (!candidatos.Any() && !endLoop)
        {
            Log.WriteLine("Não é possível agrupar rotas, otimizando rotas existentes para tentar agrupar novamente");
            Parallel.ForEach(rotas, c => c.Reordenacao(changes: Math.Min(2000, alunosTurno.Count * 10)));

            endLoop = true;
            continue;
        }

        if (!candidatos.Any()) break;

        foreach (var (c1e, c2e, c12e) in candidatos)
        {
            rotas.Remove(c1e);
            rotas.Remove(c2e);
            rotas.Add(c12e);
        }

        endLoop = false;
    }

    return rotas;
}

static List<Rota> OtimizaEscola(Requisicoes req, List<Alunos> alunos, Escolas escola, short turno)
{
    var rotas = alunos.Select(a => new Rota(req, [escola], [a], turno)).ToList();

    var endLoop = false;

    while (true)
    {
        var analise = new List<(Rota, Rota, Rota)>();

        Parallel.For(0, rotas.Count, i =>
        {
            for (var j = i + 1; j < rotas.Count; j++)
            {
                var c1 = rotas[i];
                var c2 = rotas[j];
                var c3 = c1.Mistura(req, turno, c2);

                lock (analise) analise.Add((c1, c2, c3));
            }
        });

        analise = OrdenaCandidatosComoTipo5(analise);


        var candidatos = new List<(Rota, Rota, Rota)>();
        var removidos = new HashSet<Rota>();
        var maxCandadatos = 5;

        foreach (var (c1e, c2e, c12e) in analise)
        {
            if (removidos.Contains(c1e) || removidos.Contains(c2e)) continue;
            candidatos.Add((c1e, c2e, c12e));
            removidos.Add(c1e);
            removidos.Add(c2e);
            if (candidatos.Count >= maxCandadatos) break;
        }

        if (!candidatos.Any() && !endLoop)
        {
            Log.WriteLine("Não é possível agrupar rotas, otimizando rotas existentes para tentar agrupar novamente");
            Parallel.ForEach(rotas, c => c.Reordenacao(changes: Math.Min(2000, alunos.Count * 10)));

            endLoop = true;
            continue;
        }

        if (!candidatos.Any()) break;

        foreach (var (c1e, c2e, c12e) in candidatos)
        {
            rotas.Remove(c1e);
            rotas.Remove(c2e);
            rotas.Add(c12e);
        }

        endLoop = false;
    }

    return rotas;
}

static List<(Rota, Rota, Rota)> OrdenaCandidatosComoTipo5(List<(Rota, Rota, Rota)> candidatos)
{
    return candidatos.AsParallel()
        .Where(c => !float.IsInfinity(c.Item3.Custo) && c.Item3.Factivel)
        .OrderBy(c => Math.Min(c.Item1.Alunos.Length, c.Item2.Alunos.Length))
        .ThenByDescending(c => c.Item1.Custo + c.Item2.Custo - c.Item3.Custo)
        .ToList();
}

static void AssociaVeiculos(List<Viagens> viagens, List<Veiculos> veiculos, List<Veiculos> proprios, Requisicoes req)
{
    foreach (var ida in viagens.OrderBy(v => v.Paradas[0].Tempo))
    {
        var viagem = ida;
        var intervalo = viagem.IntervaloViagem;
        Veiculos veiculo = EncontraVeiculo(veiculos, req, viagem, intervalo);

        veiculo.Intervalos.Add((intervalo, viagem.Paradas.First().Vertice, viagem.Paradas.Last().Vertice, viagem));

        viagem.CapacidadeVeiculo = (short)veiculo.Capacidade;
        viagem.AnoFabricacaoVeiculo = veiculo.AnoFabricacao;
        viagem.PlacaVeiculo = veiculo.Placa;
        viagem.TipoVeiculo = veiculo.TipoVeiculo;
        viagem.VeiculoEspecial = veiculo.VeiculoEspecial;

        viagem.AtualizaPosProcessamento(veiculo.TipoVeiculo, req.Distancias);

        veiculo.NumViagem++;
    }
}

static Veiculos EncontraVeiculo(List<Veiculos> veiculos, Requisicoes req, Viagens viagem, IntervaloTempo intervalo)
{
    var novaViagem = (intervalo, viagem.Paradas.First().Vertice, viagem.Paradas.Last().Vertice, viagem);

    float ChaveProximidadeInicio(Veiculos v) =>
        v.Intervalos.Count == 0
            ? 0f
            : req.Distancias[viagem.Paradas.Last().Vertice, v.Intervalos.First().verticeIni, viagem.TipoVeiculo];

    float ChaveProximidadeFim(Veiculos v) =>
        v.Intervalos.Count == 0
            ? 0f
            : req.Distancias[v.Intervalos.Last().verticeFim, viagem.Paradas.First().Vertice, viagem.TipoVeiculo];

    var veiculo = veiculos.Where(v => !(v.Intervalos.Any(i => i.intervalo & intervalo)))
        .Where(v => ValidaConexoes(v, novaViagem, req))
        .Where(v => (v.TipoVeiculo == viagem.TipoVeiculo || v.TipoVeiculo == viagem.TipoVeiculo + 1)
                    && v.VeiculoEspecial == viagem.VeiculoEspecial)
        .OrderBy(v => v.TipoVeiculo - viagem.TipoVeiculo)
        .ThenBy(v => v.Intervalos.Count == 0 ? 0 : 1) // idle (esvaziado) primeiro
        .ThenBy(v => (int)ChaveProximidadeInicio(v) / 10)
        .ThenBy(v => ChaveProximidadeFim(v))
        .FirstOrDefault(v => viagem.TrafegaViagem(v.TipoVeiculo, req.Distancias));

    if (veiculo == null)
    {
        veiculo = new Veiculos()
        {
            Placa = req.NovoVeiculo,
            TipoVeiculo = viagem.TipoVeiculo,
            Capacidade = req.DadosVeiculo(viagem.TipoVeiculo).capacidade,
            AnoFabricacao = req.DadosVeiculo(viagem.TipoVeiculo).ano,
            Intervalos = new List<(IntervaloTempo intervalo, int verticeIni, int verticeFim, Viagens viagem)>(),
            VeiculoEspecial = viagem.VeiculoEspecial,
            NumViagem = 1
        };

        veiculos.Add(veiculo);
    }

    return veiculo;
}

static bool ValidaConexoes(Veiculos veiculo, (IntervaloTempo intervalo, int verticeIni, int verticeFim, Viagens viagem) novaViagem, Requisicoes req)
{
    var intervalos = new List<(IntervaloTempo intervalo, int verticeIni, int verticeFim, Viagens viagem)>(veiculo.Intervalos);
    if (intervalos.Any(i => i.intervalo & novaViagem.intervalo)) return false;

    intervalos.Add(novaViagem);
    intervalos = intervalos.OrderBy(i => i.intervalo.Inicio).ToList();

    for (var i = 0; i < intervalos.Count - 1; i++)
    {
        var anterior = intervalos[i];
        var proximo = intervalos[i + 1];
        var tempo = req.Distancias[anterior.verticeFim, proximo.verticeIni, veiculo.TipoVeiculo];
        if (float.IsInfinity(tempo)) return false;
        if (anterior.intervalo.Fim.AddMinutes(tempo) > proximo.intervalo.Inicio) return false;
    }

    var ultimo = intervalos[^1];
    var primeiro = intervalos[0];
    return !float.IsInfinity(req.Distancias[ultimo.verticeFim, primeiro.verticeIni, veiculo.TipoVeiculo]);
}
