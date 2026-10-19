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
                    .Set(r => r.DescricaoError, e.Message)
                    .Update();
            }
            catch
            {
                Log.WriteLine($"Erro ao atualizar a requisicao {requisicao.Requisicao}: {e.Message}");
            }
            Log.WriteLine($"Erro ao processar requisicao {requisicao.Requisicao}: {e.Message}");
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

    var ida = AtualizaSentido(req, db, alunosAtuais, alunosAntigos, escolas, veiculos, veiculosParticulares, turnos, tiposVeiculo, sentido: 1, viagensAntigas); //IDA
    var volta = AtualizaSentido(req, db, alunosAtuais, alunosAntigos, escolas, veiculos, veiculosParticulares, turnos, tiposVeiculo, sentido: 2, viagensAntigas); //VOLTA

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

    var preenchimentoConcluido = db.Execute<bool>(
        $"SELECT requisicao.TER_PreencheOtimizacao({req.Requisicao}, {versao});");
    if (!preenchimentoConcluido)
        throw new InvalidOperationException(
            $"TER_PreencheOtimizacao retornou falha para a requisicao {req.Requisicao}, versao {versao}.");

    db.Requisicoes.Where(r => r.Requisicao == req.Requisicao && r.Versao == req.Versao)
        .Set(r => r.Situacao, (short)12)
        .Update();

    Log.WriteLine("Otimização concluída");


}

static List<Viagens> AtualizaSentido(Requisicoes req, TranscolarDB db, List<Alunos> alunosAtuais, List<Alunos> alunosAntigos, List<Escolas> escolas, List<Veiculos> veiculos, List<Veiculos> veiculosParticulares, List<Turnos> turnos, List<TiposVeiculo> tiposVeiculo, short sentido, List<Viagens> viagensAntigas)
{
    var viagens = viagensAntigas.Where(v => v.Sentido == sentido).ToList();



    foreach (var veiculo in veiculosParticulares)
        veiculo.TipoVeiculo = Veiculos.TipoVeiculoPelaCapacidade(veiculo.Capacidade, req);

    foreach (var escola in escolas)
        escola.turnos = turnos.Where(t => t.Escola == escola.Seq).ToList();

    RemoverAlunos(viagens, alunosAntigos.Where(an => alunosAtuais.All(at => at.Seq != an.Seq)).ToList(), req);
    var alunosNaoModificados = ModificarAlunos(viagens, AlunosModificados(alunosAntigos, alunosAtuais), req);
    var alunosOtm = InserirAlunos(viagens, alunosAtuais.Where(at => alunosAntigos.All(an => an.Seq != at.Seq)).Union(alunosNaoModificados).ToList(), req);

    AtualizaEscolasDasViagens(viagens, escolas);
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

static List<Alunos> InserirAlunos(List<Viagens> viagens, List<Alunos> alunos, Requisicoes req)
{
    var alunosOtm = new List<Alunos>();

    foreach (var aluno in alunos)
    {
        bool inserido = false;
        foreach (var viagem in viagens)
        {
            if (viagem.Paradas.Count(p => p.Tipo == 1) >= viagem.CapacidadeVeiculo) continue;

            inserido = MoverAluno(req, aluno, viagem);
            if (inserido) break;
            inserido = InserirAluno(req, aluno, viagem);
            if (inserido) break;
        }

        if (!inserido) alunosOtm.Add(aluno);
    }

    return alunosOtm;
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

static List<Alunos> ModificarAlunos(List<Viagens> viagens, List<(Alunos aluno, bool adaptavel)> alunos, Requisicoes req)
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
            modificar.Vertice = aluno.VerticeParada;

            if (viagem.TrafegaViagem(viagem.TipoVeiculo, req.Distancias))
            {
                modificar.Vertice = aluno.VerticeParada;
                modificar.Codigo = aluno.Seq;
                modificar.Distancia = aluno.DistanciaParada;
                modificar.Nome = aluno.Nome;
            }
            else
            {
                modificar.Vertice = vertice;
                alunosNaoAdaptaveis.Add(aluno);
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
        viagem.Paradas = viagem.Paradas.OrderBy(p => p.Sequencial).ToList();
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

        short sequencialVertice = 0;
        for (var i = 0; i < viagem.Paradas.Count; i++)
        {
            if (i == 0 || viagem.Paradas[i - 1].Vertice != viagem.Paradas[i].Vertice)
                sequencialVertice++;

            viagem.Paradas[i].Sequencial = (short)(i + 1);
            viagem.Paradas[i].SequencialVertices = sequencialVertice;
        }

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

    var veiculo = veiculos.Where(v => !(v.Intervalos.Any(i => i.intervalo & intervalo)))
        .Where(v => ValidaConexoes(v, novaViagem, req))
        .Where(v => (v.TipoVeiculo == viagem.TipoVeiculo || v.TipoVeiculo == viagem.TipoVeiculo + 1) && v.VeiculoEspecial == viagem.VeiculoEspecial)
        .OrderBy(v => v.TipoVeiculo - viagem.TipoVeiculo)
        .ThenBy(v => (int)req.Distancias[viagem.Paradas.Last().Vertice, v.Intervalos.First().verticeIni, viagem.TipoVeiculo] / 10)
        .ThenBy(v => req.Distancias[v.Intervalos.Last().verticeFim, viagem.Paradas.First().Vertice, viagem.TipoVeiculo])
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

static bool InserirAluno(Requisicoes req, Alunos aluno, Viagens viagem)
{
    bool inserido = false;
    int pos = 0;
    var minDistancia = float.PositiveInfinity;

    for (int i = 0; i < viagem.Paradas.Count - 1; i++)
    {
        var parada = viagem.Paradas[i];
        var proxima = viagem.Paradas[i + 1];
        var distancia = req.Distancias[parada.Vertice, aluno.VerticeParada, viagem.TipoVeiculo] + req.Distancias[aluno.VerticeParada, proxima.Vertice, viagem.TipoVeiculo] - req.Distancias[parada.Vertice, proxima.Vertice, viagem.TipoVeiculo];
        if (distancia < minDistancia)
        {
            minDistancia = distancia;
            pos = i;
        }
    }

    if (float.IsFinite(minDistancia) && minDistancia < req.TempoMaximo && (TimeSpan.FromMinutes(minDistancia) + (viagem.Duracao ?? TimeSpan.Zero)).TotalMinutes < req.TempoMaximo)
    {
        var parada = new Paradas()
        {
            Codigo = aluno.Seq,
            Nome = aluno.Nome,
            Distancia = aluno.DistanciaParada,
            Vertice = aluno.VerticeParada,
            Viagem = viagem.Viagem,
            Requisicao = viagem.Requisicao,
            Versao = req.Versao ?? 0,
            Tipo = 1,
            Carregamento = (short)(viagem.Sentido == 1 ? 1 : 2)
        };

        viagem.Paradas.Insert(pos + 1, parada);

        if (viagem.TrafegaViagem(viagem.TipoVeiculo, req.Distancias))
        {
            viagem.AtualizaPosProcessamento(viagem.TipoVeiculo, req.Distancias);
            inserido = true;
        }
        else
            viagem.Paradas.Remove(parada);
    }

    return inserido;
}

static bool MoverAluno(Requisicoes req, Alunos aluno, Viagens viagem)
{
    bool movido = false;
    int vertice = 0;
    Paradas? minParada = null;
    var minDistancia = float.PositiveInfinity;

    foreach (var parada in viagem.Paradas.Where(p => p.Tipo == 1))
    {
        var distanciaIda = req.Distancias[aluno.VerticeVia, parada.Vertice, 0];
        var distanciaVolta = req.Distancias[parada.Vertice, aluno.VerticeVia, 0];
        var distancia = req.ConsideraSentido ? Math.Max(distanciaIda, distanciaVolta) : distanciaIda;

        if (distancia < minDistancia)
        {
            minDistancia = distancia;
            vertice = parada.Vertice;
            minParada = parada;
        }
    }

    if (minParada != null && float.IsFinite(minDistancia) && aluno.DistanciaVia + minDistancia <= aluno.LimiarCaminhada(req))
    {
        var parada = new Paradas()
        {
            Codigo = aluno.Seq,
            Nome = aluno.Nome,
            Distancia = aluno.DistanciaVia + minDistancia,
            Vertice = vertice,
            Viagem = viagem.Viagem,
            Requisicao = viagem.Requisicao,
            Versao = req.Versao ?? 0,
            Tipo = 1,
            Carregamento = (short)(viagem.Sentido == 1 ? 1 : 2)
        };

        int pos = viagem.Paradas.IndexOf(minParada);
        viagem.Paradas.Insert(pos + 1, parada);

        if (viagem.TrafegaViagem(viagem.TipoVeiculo, req.Distancias))
        {
            viagem.AtualizaPosProcessamento(viagem.TipoVeiculo, req.Distancias);
            movido = true;
        }
        else
            viagem.Paradas.Remove(parada);
    }

    return movido;
}
