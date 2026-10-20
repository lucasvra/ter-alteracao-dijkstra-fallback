using Otimizacao;
using ter_otimizacao;
using Xunit;

namespace ter_alteracao.Tests;

public class InsercaoTests
{
    // Vértices numa reta: custo (min) = |a - b|. Caminhada idem (índice 0).
    private static float Custo(int a, int b) => Math.Abs(a - b);
    private static float CustoV(Viagens _, int a, int b) => Custo(a, b);
    // 0009: km da guarda de reordenação (metros) nos testes de revisita = mesma reta × 1000.
    private static float Metros(int a, int b) => 1000f * Custo(a, b);

    // Turnos.Id 100/101 = código 1 (manhã) das escolas 10/20; Id 200 = código 2 (tarde) da escola 10.
    private static readonly Dictionary<int, short> TurnoPorId = Insercao.MapaTurnoPorId(new[]
    {
        new Turnos { Id = 100, Escola = 10, Turno = 1 },
        new Turnos { Id = 101, Escola = 20, Turno = 1 },
        new Turnos { Id = 200, Escola = 10, Turno = 2 },
    });

    private static Paradas Aluno(int codigo, int vertice) => new() { Tipo = 1, Codigo = codigo, Vertice = vertice };
    private static Paradas Escola(int codigo, int vertice) => new() { Tipo = 2, Codigo = codigo, Vertice = vertice };

    private static Viagens Viagem(short numero, short sentido, short turno, params Paradas[] paradas) => new()
    {
        Viagem = numero, Sentido = sentido, Turno = turno, CapacidadeVeiculo = 40, TipoVeiculo = 1,
        Paradas = paradas.ToList()
    };

    private static Alunos NovoAluno(int escola, int turnoId, int vertice) => new()
    {
        Seq = 9999, Escola = escola, Turno = turnoId, VerticeParada = vertice,
        VerticeVia = vertice, DistanciaVia = 0
    };

    private static CandidatoInsercao? Melhor(IEnumerable<Viagens> viagens, Alunos aluno, float tempoMaximo = 120, double limiar = -1)
        => Insercao.MelhorCandidato(viagens, aluno, TurnoPorId, CustoV, Custo, false, limiar, tempoMaximo);

    [Fact]
    public void MapaTurno_IdDoAlunoViraCodigoDaViagem()
    {
        Assert.Equal((short)1, TurnoPorId[100]);
        Assert.Equal((short)1, TurnoPorId[101]);
        Assert.Equal((short)2, TurnoPorId[200]);
    }

    [Fact]
    public void AlunoDeOutraEscola_NuncaEntra_MesmoComDesvioZero()
    {
        // Viagem da escola 10 passa exatamente no vértice do aluno (desvio 0) e tem folga.
        var v = Viagem(1, 1, 1, Aluno(1, 5), Aluno(2, 6), Escola(10, 20));
        var aluno = NovoAluno(escola: 20, turnoId: 101, vertice: 5);

        Assert.False(Insercao.ViagemCompativel(v, aluno, TurnoPorId));
        Assert.Null(Melhor(new[] { v }, aluno, limiar: 1000)); // nem Mover, nem Inserir
    }

    [Fact]
    public void AlunoSemEscolaOuTurnoDesconhecido_NuncaEntra()
    {
        var v = Viagem(1, 1, 1, Aluno(1, 5), Escola(10, 20));
        Assert.Null(Melhor(new[] { v }, new Alunos { Seq = 1, Escola = null, Turno = 100, VerticeParada = 5 }));
        Assert.Null(Melhor(new[] { v }, NovoAluno(10, turnoId: 999, vertice: 5)));
    }

    [Fact]
    public void MesmaEscolaOutroTurno_NuncaEntra()
    {
        var manha = Viagem(1, 1, 1, Aluno(1, 5), Escola(10, 20));
        var aluno = NovoAluno(escola: 10, turnoId: 200, vertice: 6); // tarde
        Assert.Null(Melhor(new[] { manha }, aluno));
    }

    [Fact]
    public void EntreCompativeis_EscolheMenorDesvio_NaoAPrimeiraDaLista()
    {
        // Aluno no vértice 50 (escola 10 no vértice 0, sentido volta: escola primeiro).
        var longe   = Viagem(1, 2, 1, Escola(10, 0), Aluno(1, 10), Aluno(2, 20));   // desvio mínimo = 40+30-10 = 60
        var perto   = Viagem(2, 2, 1, Escola(10, 0), Aluno(3, 45), Aluno(4, 60));   // desvio = 5+10-15 = 0
        var outra   = Viagem(3, 2, 1, Escola(20, 0), Aluno(5, 49), Aluno(6, 51));   // outra escola
        var aluno = NovoAluno(escola: 10, turnoId: 100, vertice: 50);

        var c = Melhor(new[] { longe, perto, outra }, aluno);

        Assert.NotNull(c);
        Assert.Same(perto, c!.Viagem);
        Assert.Equal(TipoInsercao.Inserir, c.Tipo);
        Assert.Equal(2, c.Posicao);            // entre 45 e 60
        Assert.Equal(0f, c.Desvio);
    }

    [Fact]
    public void EntreCompativeis_EscolheMenorDesvio_ComDesviosPositivos()
    {
        // Ida (escola no fim). Aluno em 30.
        var a = Viagem(1, 1, 1, Aluno(1, 0), Aluno(2, 10), Escola(10, 100));  // 10->30->100: 20+70-90 = 0
        var b = Viagem(2, 1, 1, Aluno(3, 100), Aluno(4, 90), Escola(10, 80)); // 90->30->80: 60+50-10 = 100 (pior)
        var aluno = NovoAluno(10, 100, 30);
        var c = Melhor(new[] { b, a }, aluno, tempoMaximo: 500);
        Assert.Same(a, c!.Viagem);
    }

    [Fact]
    public void DuracaoAtualMaisDesvio_AcimaDoTempoMaximo_Rejeita_IgnorandoDuracaoAntiga()
    {
        // Duração atual 0->100->100 = 100 min; melhor desvio para o vértice 115 = 15+15-0 = 30 → 130.
        // Duracao gravada (defasada, da versão anterior) = 10 min e NÃO pode ser usada.
        var aluno = NovoAluno(10, 100, 115);
        var v2 = Viagem(2, 2, 1, Escola(10, 0), Aluno(1, 100), Aluno(2, 100));
        v2.Duracao = TimeSpan.FromMinutes(10);

        Assert.Null(Melhor(new[] { v2 }, aluno, tempoMaximo: 120));
        Assert.NotNull(Melhor(new[] { v2 }, aluno, tempoMaximo: 130)); // 100 + 30 <= 130
    }

    [Fact]
    public void Ida_InsereAntesDaEscolaDoAluno_Volta_Depois()
    {
        // Viagem com duas escolas. Ida: alunos ... escola 20 (v=50) ... escola 10 (v=100).
        var ida = Viagem(1, 1, 1, Aluno(1, 0), Escola(20, 50), Aluno(2, 60), Escola(10, 100));
        // aluno da escola 20 no vértice 70: sem janela ele iria entre 60 e 100 (desvio 0 = depois da escola 20)
        var aluno20 = NovoAluno(20, 101, 70);
        var c = Insercao.MelhorNaViagem(ida, aluno20, TurnoPorId, Custo, Custo, false, -1, 500);
        Assert.NotNull(c);
        Assert.True(c!.Posicao <= Insercao.IndiceEscola(ida, 20)); // embarca antes da escola 20

        // Volta: escola 10 (v=0), aluno, escola 20 (v=50), aluno. Aluno da escola 20 em 10 deve ficar depois da escola 20.
        var volta = Viagem(2, 2, 1, Escola(10, 0), Aluno(3, 10), Escola(20, 50), Aluno(4, 60), Aluno(5, 70));
        var aluno20v = NovoAluno(20, 101, 10);
        var cv = Insercao.MelhorNaViagem(volta, aluno20v, TurnoPorId, Custo, Custo, false, -1, 500);
        Assert.NotNull(cv);
        Assert.True(cv!.Posicao > Insercao.IndiceEscola(volta, 20));
    }

    [Fact]
    public void Mover_TemDesvioZero_EPreferido_SoEmParadaDoLadoCertoDaEscola()
    {
        var v = Viagem(1, 1, 1, Aluno(1, 40), Aluno(2, 80), Escola(10, 100));
        var aluno = NovoAluno(10, 100, 41);
        aluno.VerticeVia = 41;
        var c = Insercao.MelhorNaViagem(v, aluno, TurnoPorId, Custo, Custo, false, limiarCaminhada: 2, tempoMaximo: 500);
        Assert.Equal(TipoInsercao.Mover, c!.Tipo);
        Assert.Equal(40, c.Vertice);
        Assert.Equal(0f, c.Desvio);
    }

    [Fact]
    public void ViagemLotada_NaoRecebe()
    {
        var v = Viagem(1, 1, 1, Aluno(1, 5), Escola(10, 20));
        v.CapacidadeVeiculo = 1;
        Assert.Null(Melhor(new[] { v }, NovoAluno(10, 100, 6)));
    }

    // ---------------- 0007 (rascunho): revisitas ----------------

    [Fact]
    public void Inserir_MatrizNaoMetrica_NaoRevisitaVerticeExistente()
    {
        // Ida: 0 -> 10 -> 30(escola 20, outra) -> 50 -> escola 10 (100). Aluno da escola 10 em 30
        // (mesmo vértice da escola 20, logo Mover não se aplica: parada tipo 2).
        // Custo "torto": 0->30 + 30->10 - 0->10 = -1 (viola triangular). Sem a 0007 ele entraria
        // entre 0 e 10 e o vértice 30 seria visitado duas vezes.
        float custo(int a, int b) => (Math.Min(a, b), Math.Max(a, b)) switch
        {
            (0, 30) => 4, (10, 30) => 5, (0, 10) => 10, _ => Math.Abs(a - b)
        };
        var v = Viagem(1, 1, 1, Aluno(1, 0), Aluno(2, 10), Escola(20, 30), Aluno(3, 50), Escola(10, 100));
        var turnos = Insercao.MapaTurnoPorId(new[] { new Turnos { Id = 100, Escola = 10, Turno = 1 } });
        var c = Insercao.MelhorNaViagem(v, NovoAluno(10, 100, 30), turnos, custo, custo, false, -1, 500);
        Assert.NotNull(c);
        var vs = v.Paradas.Select(p => p.Vertice).ToList();
        Assert.True(vs[c!.Posicao - 1] == 30 || vs[c.Posicao] == 30, $"inserido na posição {c.Posicao}, longe do vértice 30");
        Assert.Equal(0f, c.Desvio);
    }

    [Fact]
    public void Reposiciona_TrocaInLocoNoMeioDeGrupo_NaoGeraYXY()
    {
        // Ida: três alunos no vértice 20, escola em 100. O do meio troca para o vértice 10 (in loco):
        // 20 -> 10 -> 20 (revisita de 20). Reposicionado: 20 -> 20 -> 10 -> 100 (mesma duração, sem revisita;
        // a janela da inserção não antepõe paradas ao início da ida, como no Inserir).
        var meio = Aluno(2, 20);
        var v = Viagem(1, 1, 1, Aluno(1, 20), meio, Aluno(3, 20), Escola(10, 100));
        meio.Vertice = 10;
        Assert.True(Insercao.ReposicionaParada(v, meio, 10, Custo, distancia: Metros));
        var vs = v.Paradas.Select(p => p.Vertice).ToList();
        Assert.Equal(new[] { 20, 20, 10, 100 }, vs);
        var blocos = 1 + vs.Zip(vs.Skip(1)).Count(t => t.First != t.Second);
        Assert.Equal(vs.Distinct().Count(), blocos); // nenhum vértice em dois blocos = sem revisita
    }

    [Fact]
    public void Reposiciona_NaoPiora_MantemPosicao()
    {
        var p = Aluno(2, 30);
        var v = Viagem(1, 1, 1, Aluno(1, 10), p, Aluno(3, 50), Escola(10, 100));
        Assert.False(Insercao.ReposicionaParada(v, p, 10, Custo, distancia: Metros));
        Assert.Equal(new[] { 10, 30, 50, 100 }, v.Paradas.Select(x => x.Vertice).ToArray());
    }

    // ---------------- 0007: um bloco por vértice + noturno ----------------

    private static int Blocos(Viagens v) => Insercao.Blocos(v.Paradas).Count;
    private static int Revisitas(Viagens v) => Blocos(v) - v.Paradas.Select(p => p.Vertice).Distinct().Count();

    [Fact]
    public void CasoA_HerdadaComGrupoPartido_ViraUmBloco()
    {
        // 93 (ida): ... 4552, 6375×3, 6651, 6375×2, 6436, escola. Simplificado numa reta.
        var v = Viagem(93, 1, 1, Aluno(1, 10), Aluno(2, 30), Aluno(3, 30), Aluno(4, 35), Aluno(5, 30), Aluno(6, 30),
                       Aluno(7, 50), Escola(10, 100));
        Assert.Equal(1, Revisitas(v));
        var n = Insercao.AgrupaVerticesIguais(v, _ => 10, Custo, distancia: Metros);
        Assert.Equal(1, n);
        Assert.Equal(0, Revisitas(v));
        Assert.Equal(8, v.Paradas.Count); // ninguém some
        Assert.Equal(2, v.Paradas.Count(p => p.Codigo is 5 or 6 && p.Vertice == 30));
    }

    [Fact]
    public void CasoA_Volta_JuntaNoFimDoBloco_ESemPiorarComMatrizMetrica()
    {
        var v = Viagem(94, 2, 1, Escola(10, 0), Aluno(1, 50), Aluno(2, 30), Aluno(3, 35), Aluno(4, 30), Aluno(5, 10));
        var antes = Insercao.Duracao(v.Paradas.Select(p => p.Vertice).ToList(), Custo);
        Insercao.AgrupaVerticesIguais(v, _ => 10, Custo, distancia: Metros);
        var depois = Insercao.Duracao(v.Paradas.Select(p => p.Vertice).ToList(), Custo);
        Assert.Equal(0, Revisitas(v));
        Assert.True(depois <= antes);
        Assert.Equal(2, TipoParada(v, 0)); // escola continua primeiro na volta
    }

    private static int TipoParada(Viagens v, int i) => v.Paradas[i].Tipo ?? 0;

    [Fact]
    public void CasoB_Mover_AlunoComVerticeParadaDiferente_CaminhaAteParadaExistente_SemRevisita()
    {
        // 127: aluno novo com vertice_parada 4572 (aqui 47) e via em 44; parada existente em 443 (aqui 44).
        var v = Viagem(127, 1, 2, Aluno(1, 20), Aluno(2, 44), Aluno(3, 44), Aluno(4, 60), Escola(10, 100));
        var turnos = Insercao.MapaTurnoPorId(new[] { new Turnos { Id = 300, Escola = 10, Turno = 2 } });
        var aluno = new Alunos { Seq = 77, Escola = 10, Turno = 300, VerticeParada = 47, VerticeVia = 45, DistanciaVia = 1 };

        // limiar 3: via(45)->44 = 1, +DistanciaVia 1 = 2 <= 3 → Mover para 44, colado ao bloco de 44
        var c = Insercao.MelhorNaViagem(v, aluno, turnos, Custo, Custo, false, limiarCaminhada: 3, tempoMaximo: 500);
        Assert.Equal(TipoInsercao.Mover, c!.Tipo);
        Assert.Equal(44, c.Vertice);
        Assert.Equal(1f, c.Caminhada);
        v.Paradas.Insert(c.Posicao, new Paradas { Tipo = 1, Codigo = 77, Vertice = c.Vertice });
        Assert.Equal(0, Revisitas(v));

        // limiar 1.5: 1 + 1 = 2 > 1.5 → não caminha; insere no próprio vértice 47
        var v2 = Viagem(128, 1, 2, Aluno(1, 20), Aluno(2, 44), Aluno(4, 60), Escola(10, 100));
        var c2 = Insercao.MelhorNaViagem(v2, aluno, turnos, Custo, Custo, false, limiarCaminhada: 1.5, tempoMaximo: 500);
        Assert.Equal(TipoInsercao.Inserir, c2!.Tipo);
        Assert.Equal(47, c2.Vertice);
    }

    [Fact]
    public void CasoC_ZigueZagueInLoco_ComCustoDeFallbackNaoMetrico_ViraDoisBlocos()
    {
        // 167 (ida): 6524, 6615×2, 6788(in loco), 6615, 6788(in loco), 6615×2, 6429, escola.
        // 6615<->6788 fora da matriz (fallback) e "barato" demais (0.5) — viola a triangular.
        float custo(int a, int b) => (Math.Min(a, b), Math.Max(a, b)) switch
        {
            (6615, 6788) => 0.5f,
            (6524, 6615) => 3, (6524, 6788) => 3, (6429, 6615) => 4, (6429, 6788) => 4,
            (6429, 7000) => 10, (6524, 6429) => 7,
            _ => 50
        };
        var v = Viagem(167, 1, 2, Aluno(1, 6524), Aluno(2, 6615), Aluno(3, 6615), Aluno(4, 6788), Aluno(5, 6615),
                       Aluno(6, 6788), Aluno(7, 6615), Aluno(8, 6615), Aluno(9, 6429), Escola(10, 7000));
        Assert.Equal(3, Revisitas(v));
        Insercao.AgrupaVerticesIguais(v, _ => 10, custo, distancia: (a, b) => 1000f * custo(a, b));
        Assert.Equal(0, Revisitas(v));
        Assert.Equal(10, v.Paradas.Count);
        Assert.Equal(2, v.Paradas.Count(p => p.Vertice == 6788));
        Assert.Equal(5, v.Paradas.Count(p => p.Vertice == 6615));
        Assert.Equal(2, TipoParada(v, v.Paradas.Count - 1)); // escola continua no fim
    }

    [Fact]
    public void MultiEscola_NaoJunta_QuandoViolariaOLadoDaEscola()
    {
        // Ida: aluno A (escola 10) em 40, escola 10 (50), aluno B (escola 20) em 60, escola 20 em 40.
        // O vértice 40 tem uma escola (não se move); levar A para lá o poria depois da escola 10.
        var v = Viagem(1, 1, 1, Aluno(1, 40), Escola(10, 50), Aluno(2, 60), Escola(20, 40));
        int? esc(int seq) => seq == 1 ? 10 : 20;
        Assert.Equal(0, Insercao.AgrupaVerticesIguais(v, esc, Custo, distancia: Metros));
        Assert.Equal(new[] { 40, 50, 60, 40 }, v.Paradas.Select(p => p.Vertice).ToArray());

        // Já o caso "aluno de outra escola embarca antes" é factível e junta:
        var w = Viagem(2, 1, 1, Aluno(1, 30), Escola(20, 40), Aluno(2, 30), Escola(10, 100));
        int? esc2(int seq) => seq == 1 ? 20 : 10;
        Assert.Equal(1, Insercao.AgrupaVerticesIguais(w, esc2, Custo, distancia: Metros));
        Assert.Equal(new[] { 30, 30, 40, 100 }, w.Paradas.Select(p => p.Vertice).ToArray());
    }

    [Fact]
    public void VerticeDeEscola_AlunoVaiParaOBlocoDaEscola()
    {
        // Volta: escola 10 em 0, aluno em 0 lá na frente → junta ao bloco da escola (depois dela).
        var v = Viagem(2, 2, 1, Escola(10, 0), Aluno(1, 20), Aluno(2, 0), Aluno(3, 40));
        Assert.Equal(1, Insercao.AgrupaVerticesIguais(v, _ => 10, Custo, distancia: Metros));
        Assert.Equal(new[] { 0, 0, 20, 40 }, v.Paradas.Select(p => p.Vertice).ToArray());
        Assert.Equal(2, TipoParada(v, 0));
    }

    [Fact]
    public void AgrupaPorVertice_RotaNova_JuntaNaPrimeiraOcorrencia()
    {
        var r = Insercao.AgrupaPorVertice(new[] { (1, 5), (2, 7), (3, 5), (4, 9), (5, 7) }, t => t.Item2);
        Assert.Equal(new[] { 1, 3, 2, 5, 4 }, r.Select(t => t.Item1).ToArray());
    }

    // ---------------- 0007: guarda de TempoMaximo no reagrupamento ----------------

    // Custo não métrico: juntar os dois 5 PIORA a viagem (17 → 36 min).
    private static float CustoTorto(int a, int b) => (Math.Min(a, b), Math.Max(a, b)) switch
    {
        (0, 5) => 5, (5, 6) => 1, (5, 100) => 10, (6, 100) => 30, (0, 6) => 30, _ => 99
    };
    private static Viagens ViagemTorta() => Viagem(9, 1, 1, Aluno(1, 0), Aluno(2, 5), Aluno(3, 6), Aluno(4, 5), Escola(10, 100));
    private static float Dur(Viagens v, Func<int, int, float> c) => Insercao.Duracao(v.Paradas.Select(p => p.Vertice).ToList(), c);

    [Fact]
    public void Guarda_ReagrupaDentroDoLimite()
    {
        // Métrica (reta): 100 → 90 min, limite 95.
        var v = Viagem(93, 1, 1, Aluno(1, 10), Aluno(2, 30), Aluno(3, 30), Aluno(4, 35), Aluno(5, 30), Aluno(6, 30),
                       Aluno(7, 50), Escola(10, 100));
        Assert.Equal(100f, Dur(v, Custo));
        Assert.Equal(1, Insercao.AgrupaVerticesIguais(v, _ => 10, Custo, tempoMaximo: 95));
        Assert.Equal(0, Revisitas(v));
        Assert.True(Dur(v, Custo) <= 95);

        // 0009: dentro do limite NÃO aceita mais piorar o tempo (17 → 36 com limite 40): mantém a ordem.
        var t = ViagemTorta();
        Assert.Equal(0, Insercao.AgrupaVerticesIguais(t, _ => 10, CustoTorto, tempoMaximo: 40));
        Assert.Equal(1, Revisitas(t));
        Assert.Equal(17f, Dur(t, CustoTorto));
    }

    [Fact]
    public void Guarda_NaoReagrupa_QuandoPassariaDoLimite()
    {
        var t = ViagemTorta();
        var ordem = t.Paradas.Select(p => p.Vertice).ToArray();
        Assert.Equal(17f, Dur(t, CustoTorto));
        Assert.Equal(0, Insercao.AgrupaVerticesIguais(t, _ => 10, CustoTorto, tempoMaximo: 20)); // 36 > 20
        Assert.Equal(ordem, t.Paradas.Select(p => p.Vertice).ToArray());                         // ordem mantida
        Assert.Equal(1, Revisitas(t));
    }

    [Fact]
    public void Guarda_ViagemQueJaEstourava_SoReagrupaSeNaoPiorar()
    {
        // Já estourava (17 > 15) e juntar piora (36): mantém.
        var t = ViagemTorta();
        Assert.Equal(0, Insercao.AgrupaVerticesIguais(t, _ => 10, CustoTorto, tempoMaximo: 15));
        Assert.Equal(1, Revisitas(t));

        // Já estourava (100 > 50) e juntar melhora (90): reagrupa, mesmo continuando acima do limite.
        var v = Viagem(93, 1, 1, Aluno(1, 10), Aluno(2, 30), Aluno(3, 30), Aluno(4, 35), Aluno(5, 30), Aluno(6, 30),
                       Aluno(7, 50), Escola(10, 100));
        Assert.Equal(1, Insercao.AgrupaVerticesIguais(v, _ => 10, Custo, tempoMaximo: 50));
        Assert.Equal(0, Revisitas(v));
        Assert.True(Dur(v, Custo) <= 100);
    }

    [Fact]
    public void Guarda_ReposicionaParada_RespeitaTempoMaximo()
    {
        Assert.True(Insercao.RespeitaTempo(100, 110, 120));   // dentro do limite, pode piorar até o limite
        Assert.False(Insercao.RespeitaTempo(100, 121, 120));  // passaria do limite
        Assert.True(Insercao.RespeitaTempo(130, 125, 120));   // já estourava e melhora
        Assert.False(Insercao.RespeitaTempo(130, 131, 120));  // já estourava e piora
        Assert.False(Insercao.RespeitaTempo(100, float.PositiveInfinity, 120));
    }

    // ---------------- 0007: casos reais da 51009524 (diag4, 19/10/2026) ----------------
    // 167 (ida, veículo 4): tempos da distancias_req (F2) e do fallback 6615<->6788 = 1,313 min (F5).
    private static float Custo167(int a, int b) => (Math.Min(a, b), Math.Max(a, b)) switch
    {
        (6429, 6524) => 21.370213f, (6429, 6615) => 12.063849f, (6429, 6788) => 10.751013f,
        (6524, 6615) => 9.306362f, (6524, 6788) => 10.6192f, (6615, 6788) => 1.3128372f,
        _ => float.PositiveInfinity
    };
    // 0009: distancias_req.distancia (m) da 167 (diag4 F2). 6615<->6788 não está na matriz; o fallback
    // físico não foi medido — usa-se o limite inferior pela triangular (7079,5 − 6204,2 = 8042,6 − 7167,3 = 875,2 m).
    private static float Metros167(int a, int b) => (Math.Min(a, b), Math.Max(a, b)) switch
    {
        (6429, 6524) => 14246.809f, (6429, 6615) => 8042.5664f, (6429, 6788) => 7167.342f,
        (6524, 6615) => 6204.2417f, (6524, 6788) => 7079.467f, (6615, 6788) => 875.2253f,
        _ => float.PositiveInfinity
    };

    [Fact]
    public void Real167_TrocaInLocoNoMeioDoGrupo_ReposicionaSemRevisita()
    {
        // v0: 6524, 6615×7 (2078027, 2078029, 1950888, 1995804, 2021608, 2078100, 1908279), 6429, escola.
        var v = Viagem(167, 1, 2, Aluno(2075000, 6524), Aluno(2078027, 6615), Aluno(2078029, 6615), Aluno(1950888, 6615),
                       Aluno(1995804, 6615), Aluno(2021608, 6615), Aluno(2078100, 6615), Aluno(1908279, 6615),
                       Aluno(2077000, 6429), Escola(10, 6429));
        var v0 = Dur(v, Custo167);

        // ModificarAlunos: 1950888 e 2021608 passam a 6788 no MESMO sequencial (B1); depois reposiciona.
        foreach (var codigo in new[] { 1950888, 2021608 })
        {
            var p = v.Paradas.Single(x => x.Codigo == codigo);
            p.Vertice = 6788;
            Insercao.ReposicionaParada(v, p, 10, Custo167, tempoMaximo: 120, distancia: Metros167);
        }

        Assert.Equal(0, Revisitas(v));                          // na v1 real foram 3 (6615 em 3 blocos, 6788 em 2)
        Assert.Equal(2, v.Paradas.Count(p => p.Vertice == 6788));
        Assert.True(Dur(v, Custo167) <= v0 + 0.01f);             // 21,37 min; a v1 gravou 9,31 + 4×1,31 + 12,06 = 26,62
    }

    [Fact]
    public void Real167_ZigueZagueJaGravado_AgrupaVerticesIguais()
    {
        // Ordem gravada na v1 (F5): 6524, 6615×2, 6788, 6615, 6788, 6615×2, 6429.
        var v = Viagem(167, 1, 2, Aluno(1, 6524), Aluno(2078027, 6615), Aluno(2078029, 6615), Aluno(1950888, 6788),
                       Aluno(1995804, 6615), Aluno(2021608, 6788), Aluno(2078100, 6615), Aluno(1908279, 6615),
                       Aluno(9, 6429), Escola(10, 6429));
        var antes = Dur(v, Custo167);
        Assert.Equal(3, Revisitas(v));
        Insercao.AgrupaVerticesIguais(v, _ => 10, Custo167, tempoMaximo: 120, distancia: Metros167);
        Assert.Equal(0, Revisitas(v));
        Assert.True(Dur(v, Custo167) < antes);
    }

    [Fact]
    public void Real127_Mover_AlunoEm4572_CaminhaAte443_DentroDoLimiar()
    {
        // B3: via 4572 -> 443 = 774,69 m (matriz índice 0); distancia_via 7,43–12,78 m; limiar 2000 m.
        float caminhada(int a, int b) => (Math.Min(a, b), Math.Max(a, b)) == (443, 4572) ? 774.6868f : 5000f;
        var v = Viagem(127, 1, 2, Aluno(1, 4252), Aluno(1962352, 443), Aluno(2077423, 443), Aluno(1945313, 6570),
                       Escola(10, 1078));
        var turnos = Insercao.MapaTurnoPorId(new[] { new Turnos { Id = 130148, Escola = 10, Turno = 2 } });
        var aluno = new Alunos { Seq = 2087826, Escola = 10, Turno = 130148, VerticeParada = 4572, VerticeVia = 4572, DistanciaVia = 12.777703f };

        var c = Insercao.MelhorNaViagem(v, aluno, turnos, Custo, caminhada, false, limiarCaminhada: 2000, tempoMaximo: 120);
        Assert.Equal(TipoInsercao.Mover, c!.Tipo);
        Assert.Equal(443, c.Vertice);
        Assert.Equal(787.46454f, aluno.DistanciaVia + c.Caminhada, 2); // = distancia_gravada do B3
        v.Paradas.Insert(c.Posicao, new Paradas { Tipo = 1, Codigo = aluno.Seq, Vertice = c.Vertice });
        Assert.Equal(0, Revisitas(v));                                  // colado ao bloco do 443

        // Com limiar 700 m o Mover não vale: entra no próprio vértice 4572.
        var c2 = Insercao.MelhorNaViagem(v, aluno, turnos, Custo, caminhada, false, limiarCaminhada: 700, tempoMaximo: 100000); // custo de teste = |a-b|
        Assert.Equal(4572, c2!.Vertice);
    }

    // ---------------- 0008: noturno pelo código do turno ----------------

    [Fact]
    public void Noturno_UsaCodigoDoTurno_NaoOId()
    {
        var req = new Requisicoes { CaminhadaMaximaEm = 1000, CaminhadaMaximaNoturno = 500 };
        var noite = new Alunos { Turno = 132058, TipoEnsino = 4, Horario = new Turnos { Id = 132058, Turno = 3 } };
        var manha = new Alunos { Turno = 132056, TipoEnsino = 4, Horario = new Turnos { Id = 132056, Turno = 1 } };
        var idTres = new Alunos { Turno = 3, TipoEnsino = 4, Horario = new Turnos { Id = 3, Turno = 1 } }; // id 3, mas manhã
        Assert.Equal(500, noite.LimiarCaminhada(req));
        Assert.Equal(1000, manha.LimiarCaminhada(req));
        Assert.Equal(1000, idTres.LimiarCaminhada(req));
        Assert.Equal(1000, new Alunos { Turno = 132058, TipoEnsino = 4 }.LimiarCaminhada(req)); // sem Horario: sem noturno
    }

    // ---------------- 0009: reordenação não pode piorar tempo nem km ----------------
    // Perfil das 119/120 (074cc73 × 1ed6d7d: +1,75 min e +1,16 km, sem aluno novo, dentro do TM 120):
    // aluno trocado in loco na 1ª parada da ida (posição 0 fica fora de JanelaInsercao, então a
    // MelhorPosicao não consegue devolvê-lo ao lugar). Valores sintéticos com o mesmo delta real
    // (as sequências reais de 119/120 dependem do E1 da q_diag5).
    private static float Min119(int a, int b) => (Math.Min(a, b), Math.Max(a, b)) switch
    {
        (7, 10) => 3f, (10, 20) => 10f, (20, 30) => 10f, (7, 20) => 11.75f, (7, 30) => 30f, _ => 99f
    };
    private static float M119(int a, int b) => (Math.Min(a, b), Math.Max(a, b)) switch
    {
        (7, 10) => 2000f, (10, 20) => 6000f, (20, 30) => 6000f, (7, 20) => 7160f, (7, 30) => 20000f, _ => 99000f
    };
    private static Viagens Viagem119(out Paradas trocado)
    {
        trocado = Aluno(1, 7);                         // vértice novo da troca in loco, na posição 0
        return Viagem(119, 1, 1, trocado, Aluno(2, 10), Aluno(3, 20), Escola(10, 30));
    }

    [Fact]
    public void Perfil119_ReordenacaoQuePioravaKmDentroDoTM_AgoraRecusada()
    {
        var v = Viagem119(out var p);
        var ordem = v.Paradas.Select(x => x.Vertice).ToArray();
        Assert.Equal(23f, Dur(v, Min119));
        // A melhor posição disponível (entre 10 e 20) dá 24,75 min (+1,75) e 15,16 km (+1,16):
        Assert.True(Insercao.RespeitaTempo(23f, 24.75f, 120));              // a guarda da 0007 aceitava (<= TM)
        Assert.False(Insercao.NaoPioraReordenacao(23f, 24.75f, 14000f, 15160f));

        Assert.False(Insercao.ReposicionaParada(v, p, 10, Min119, tempoMaximo: 120, distancia: M119));
        Assert.Equal(ordem, v.Paradas.Select(x => x.Vertice).ToArray());    // ordem mantida
        Assert.Equal(23f, Dur(v, Min119));
        Assert.Equal(14000f, Dur(v, M119));
    }

    // 119/120 REAIS (diag5 E1/E2): o aluno 1927012 trocou in loco 5646 -> 6690 e estava na 1ª parada da ida
    // (última da volta). Na v1 (074cc73) foi reposicionado para entre 6736 e 6743: +1,739 min e +1.159 m
    // por sentido (K4: +1,16 km). Tempos (veículo da viagem) e metros da distancias_req.
    private static readonly Dictionary<(int, int), (float min, float m)> Matriz119 = new()
    {
        [(6690, 6736)] = (1.7389524f, 1159f), [(6690, 6743)] = (5.0669765f, 3378f), [(6736, 6743)] = (3.3280241f, 2219f),
        [(6228, 6743)] = (1.4119087f, 941f), [(6228, 6309)] = (1.5505452f, 1034f), [(6309, 6412)] = (1.02616f, 684f),
        [(5630, 6412)] = (0.88515997f, 590f), [(5630, 6390)] = (4.0299106f, 2687f), [(5000, 6390)] = (35.514465f, 26556f),
        [(1537, 5000)] = (12.023805f, 8016f), [(1537, 4207)] = (3.205091f, 2137f), [(4207, 5784)] = (4.9252243f, 3283f),
        [(622, 5784)] = (4.473878f, 2983f),
    };
    private static float Min119Real(int a, int b) => Matriz119.TryGetValue((Math.Min(a, b), Math.Max(a, b)), out var t) ? t.min : float.PositiveInfinity;
    private static float M119Real(int a, int b) => Matriz119.TryGetValue((Math.Min(a, b), Math.Max(a, b)), out var t) ? t.m : float.PositiveInfinity;
    private static readonly int[] Ida119 = { 6690, 6736, 6743, 6228, 6309, 6412, 5630, 6390, 5000, 1537, 4207, 5784 };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Real119e120_TrocaInLocoNaPontaDaViagem_ReposicionamentoRecusado(short sentido)
    {
        var alunos = Ida119.Select((vx, i) => Aluno(i == 0 ? 1927012 : 100 + i, vx)).ToList();
        var trocado = alunos[0];
        var paradas = sentido == 1
            ? alunos.Append(Escola(21982, 622)).ToArray()
            : new[] { Escola(21982, 622) }.Concat(Enumerable.Reverse(alunos)).ToArray();
        var v = Viagem((short)(sentido == 1 ? 119 : 120), sentido, 1, paradas);
        var ordem = v.Paradas.Select(x => x.Vertice).ToArray();
        var (min, m) = (Dur(v, Min119Real), Dur(v, M119Real));

        // O que a 0007 fazia: entre 6736 e 6743 → +1,739 min e +1.159 m, aceito pela guarda de TM (<= 120).
        Assert.True(Insercao.RespeitaTempo(min, min + 1.7389524f, 120));
        Assert.False(Insercao.NaoPioraReordenacao(min, min + 1.7389524f, m, m + 1159f));

        Assert.False(Insercao.ReposicionaParada(v, trocado, 21982, Min119Real, tempoMaximo: 120, distancia: M119Real));
        Assert.Equal(ordem, v.Paradas.Select(x => x.Vertice).ToArray());    // ordem da troca in loco mantida
        Assert.Equal(0, Revisitas(v));
        Assert.Equal(m, Dur(v, M119Real));
    }

    [Fact]
    public void Reposiciona_TempoMelhorMasKmPior_Recusada()
    {
        // Mesma viagem, mas 7->20 agora é mais rápido (9 min): a nova ordem ganha 1 min e perde 1,16 km.
        float min(int a, int b) => (Math.Min(a, b), Math.Max(a, b)) == (7, 20) ? 9f : Min119(a, b);
        var v = Viagem119(out var p);
        Assert.False(Insercao.ReposicionaParada(v, p, 10, min, tempoMaximo: 120, distancia: M119));
        Assert.Equal(new[] { 7, 10, 20, 30 }, v.Paradas.Select(x => x.Vertice).ToArray());

        // Sem a função de km (comportamento da 0007 só com tempo) a mesma troca seria aceita.
        var w = Viagem119(out var q);
        Assert.True(Insercao.ReposicionaParada(w, q, 10, min, tempoMaximo: 120));
        Assert.Equal(new[] { 10, 7, 20, 30 }, w.Paradas.Select(x => x.Vertice).ToArray());
    }

    [Fact]
    public void Agrupa_TempoMelhorMasKmPior_MantemRevisita()
    {
        // 5–6–5–100: juntar os 5 reduz o tempo (com CustoTorto invertido) mas aumenta o km → mantém.
        float min(int a, int b) => (Math.Min(a, b), Math.Max(a, b)) switch
        { (0, 5) => 5, (5, 6) => 10, (5, 100) => 10, (6, 100) => 2, (0, 6) => 30, _ => 99 };
        float m(int a, int b) => (Math.Min(a, b), Math.Max(a, b)) switch
        { (0, 5) => 5000, (5, 6) => 1000, (5, 100) => 10000, (6, 100) => 30000, (0, 6) => 30000, _ => 99000 };
        var t = ViagemTorta();                                            // 0,5,6,5,100
        Assert.Equal(35f, Dur(t, min)); Assert.Equal(17000f, Dur(t, m));
        // juntando (0,5,5,6,100): 17 min (melhor), 36 km (pior)
        Assert.Equal(0, Insercao.AgrupaVerticesIguais(t, _ => 10, min, tempoMaximo: 120, distancia: m));
        Assert.Equal(1, Revisitas(t));
        Assert.Equal(1, Insercao.AgrupaVerticesIguais(ViagemTorta(), _ => 10, min, tempoMaximo: 120)); // só tempo: juntaria
    }

    [Fact]
    public void NaoPioraReordenacao_Regras()
    {
        Assert.True(Insercao.NaoPioraReordenacao(10, 10, 5000, 5000));      // empate
        Assert.True(Insercao.NaoPioraReordenacao(10, 9, 5000, 4000));       // melhora os dois
        Assert.False(Insercao.NaoPioraReordenacao(10, 10.01f, 5000, 4000)); // piora o tempo
        Assert.False(Insercao.NaoPioraReordenacao(10, 9, 5000, 5002));      // piora o km (> 1 m)
        Assert.True(Insercao.NaoPioraReordenacao(10, 9, 5000, 5000.5f));    // dentro da tolerância
        Assert.False(Insercao.NaoPioraReordenacao(10, float.PositiveInfinity, 5000, 5000));
        Assert.True(Insercao.NaoPioraReordenacao(10, 9, float.PositiveInfinity, 5000)); // km antes sem caminho
        Assert.False(Insercao.NaoPioraReordenacao(10, 9, 5000, float.PositiveInfinity));
    }

    [Fact]
    public void Real167_ComGuardaDeKm_KmCaiEQuedaDaRevisita()
    {
        var v = Viagem(167, 1, 2, Aluno(1, 6524), Aluno(2078027, 6615), Aluno(2078029, 6615), Aluno(1950888, 6788),
                       Aluno(1995804, 6615), Aluno(2021608, 6788), Aluno(2078100, 6615), Aluno(1908279, 6615),
                       Aluno(9, 6429), Escola(10, 6429));
        var kmAntes = Dur(v, Metros167);
        Insercao.AgrupaVerticesIguais(v, _ => 10, Custo167, tempoMaximo: 120, distancia: Metros167);
        Assert.Equal(0, Revisitas(v));
        Assert.True(Dur(v, Metros167) < kmAntes);                       // 17,75 km → 14,25 km
        Assert.Equal(14246.7f, Dur(v, Metros167), 0);
    }

    // ---------------- 0010: troca in loco -> melhor viagem (critério da 0006) ----------------
    // REAL (diag5 E1/E2/E4): 1956052 mudou de 6211 para 6787 (coordenada ~14,5 km; distancia_via 580,41 m).
    // 23/24 (v1 074cc73): ... 6211×2, 5851, 6787, escola 22578 (4791) e o espelho na volta; tempos da matriz.
    // 27/28 passam a 224,14 m do novo ponto (vértice 6517) e 87/88 a 1.199,51 m (6635).
    // Sintético: o resto da sequência de 27/28 e 87/88 e o limiar (2.000 m, o da req em B3).
    private static readonly Dictionary<(int, int), float> Min23 = new()
    {
        [(6262, 6337)] = 15.423322f, [(6189, 6337)] = 5.9998913f, [(2491, 6189)] = 14.82008f, [(2491, 5106)] = 4.366694f,
        [(5106, 6211)] = 3.6931262f, [(5851, 6211)] = 3.835498f, [(5851, 6787)] = 13.421587f, [(4791, 6787)] = 11.291571f,
        [(4791, 5851)] = 3.1030343f,
        // 27/28 e 87/88 (sintéticos)
        [(6517, 6600)] = 5f, [(6517, 6610)] = 4f, [(4791, 6610)] = 10f, [(6600, 6610)] = 8f,
        [(6635, 6700)] = 6f, [(4791, 6635)] = 9f, [(4791, 6700)] = 12f,
    };
    private static float CustoMin23(Viagens _, int a, int b) =>
        a == b ? 0 : Min23.TryGetValue((Math.Min(a, b), Math.Max(a, b)), out var t) ? t : 99f;
    private static float Caminhada23(int a, int b) => (Math.Min(a, b), Math.Max(a, b)) switch
    {
        (6517, 6787) => 224.13762f, (6635, 6787) => 1199.508f, _ => 15000f
    };
    private static readonly Dictionary<int, short> Turno23 = Insercao.MapaTurnoPorId(new[] { new Turnos { Id = 129554, Escola = 22578, Turno = 1 } });
    private static Alunos Aluno1956052() => new()
    {
        Seq = 1956052, Escola = 22578, Turno = 129554, VerticeParada = 6787, VerticeVia = 6787, DistanciaVia = 580.4138f, DistanciaParada = 580.4138f
    };
    private static (Viagens ida, Viagens volta) Viagens23_24()
    {
        Paradas[] ida = { Aluno(2024104, 6262), Aluno(2063178, 6262), Aluno(2053013, 6337), Aluno(2041064, 6189), Aluno(1953759, 2491),
                          Aluno(2072940, 5106), Aluno(2024109, 6211), Aluno(1907956, 6211), Aluno(1956802, 5851), Aluno(1956052, 6787),
                          Escola(22578, 4791) };
        var volta = new[] { Escola(22578, 4791) }.Concat(ida.Take(ida.Length - 1).Reverse().Select(p => Aluno(p.Codigo, p.Vertice))).ToArray();
        return (Viagem(23, 1, 1, ida), Viagem(24, 2, 1, volta));
    }
    private static (Viagens, Viagens) Viagens27_28() =>
        (Viagem(27, 1, 1, Aluno(1, 6600), Aluno(2, 6517), Aluno(3, 6610), Escola(22578, 4791)),
         Viagem(28, 2, 1, Escola(22578, 4791), Aluno(3, 6610), Aluno(2, 6517), Aluno(1, 6600)));
    private static (Viagens, Viagens) Viagens87_88() =>
        (Viagem(87, 1, 1, Aluno(11, 6700), Aluno(12, 6635), Escola(22578, 4791)),
         Viagem(88, 2, 1, Escola(22578, 4791), Aluno(12, 6635), Aluno(11, 6700)));

    [Fact]
    public void Real1956052_TrocaInLoco_VaiPara27e28_PorMover()
    {
        var (h23, h24) = Viagens23_24();
        var (v27, v28) = Viagens27_28();
        var (v87, v88) = Viagens87_88();
        var aluno = Aluno1956052();

        var r = Insercao.MelhorRealocacaoInLoco(h23, h24, aluno, new[] { h23, v27, v87 }, new[] { h24, v28, v88 }, Turno23,
                                                CustoMin23, Caminhada23, false, limiarCaminhada: 2000, tempoMaximo: 120);
        Assert.NotNull(r);
        // custo marginal real na herdada: 13,42 + 11,29 − 3,10 = 21,61 min por sentido
        Assert.Equal(43.22f, r!.DesvioHerdada, 1);
        Assert.Same(v27, r.Ida.Viagem);
        Assert.Same(v28, r.Volta.Viagem);                    // 27/28 (224 m) vence 87/88 (1,2 km) no empate do Mover
        Assert.Equal(TipoInsercao.Mover, r.Ida.Tipo);
        Assert.Equal(6517, r.Ida.Vertice);
        Assert.Equal(0f, r.DesvioNovo);
        Assert.Equal(804.551f, aluno.DistanciaVia + r.Ida.Caminhada, 2); // 580,41 + 224,14 <= 2000

        // Aplica como RealocaTrocasInLoco (Program.cs): tira das herdadas e insere nas novas.
        h23.Paradas.RemoveAll(p => p.Codigo == 1956052);
        h24.Paradas.RemoveAll(p => p.Codigo == 1956052);
        v27.Paradas.Insert(r.Ida.Posicao, Aluno(1956052, r.Ida.Vertice));
        v28.Paradas.Insert(r.Volta.Posicao, Aluno(1956052, r.Volta.Vertice));
        Assert.Equal(51.24f, Dur(h23, (a, b) => CustoMin23(h23, a, b)), 1); // volta à duração da v0 (51,2 min)
        foreach (var v in new[] { h23, h24, v27, v28 }) Assert.Equal(0, Revisitas(v));
        Assert.Equal(new[] { 6600, 6517, 6517, 6610, 4791 }, v27.Paradas.Select(p => p.Vertice).ToArray());
    }

    [Fact]
    public void TrocaInLoco_FicaNaHerdada_QuandoOutraViagemNaoECompativelOuEPior()
    {
        var aluno = Aluno1956052();

        // (1) 27/28 de outro turno (código 2) e 87/88 sem a escola do aluno → nenhuma compatível.
        var (h23, h24) = Viagens23_24();
        var (v27, v28) = Viagens27_28(); v27.Turno = 2; v28.Turno = 2;
        var w87 = Viagem(87, 1, 1, Aluno(11, 6700), Aluno(12, 6635), Escola(99999, 4791));
        var w88 = Viagem(88, 2, 1, Escola(99999, 4791), Aluno(12, 6635), Aluno(11, 6700));
        Assert.Null(Insercao.MelhorRealocacaoInLoco(h23, h24, aluno, new[] { h23, v27, w87 }, new[] { h24, v28, w88 }, Turno23,
                                                    CustoMin23, Caminhada23, false, 2000, 120));

        // (2) Sem Mover (limiar 100 m): inserir 6787 em 27/28 custa 99 + 99 − 5 min por sentido > 43,2 → fica.
        var (x27, x28) = Viagens27_28();
        Assert.Null(Insercao.MelhorRealocacaoInLoco(h23, h24, aluno, new[] { h23, x27 }, new[] { h24, x28 }, Turno23,
                                                    CustoMin23, Caminhada23, false, 100, 500));

        // (3) Ida e volta juntas: só a ida tem candidato (volta 28 lotada) → fica.
        var (y27, y28) = Viagens27_28(); y28.CapacidadeVeiculo = 3;
        Assert.Null(Insercao.MelhorRealocacaoInLoco(h23, h24, aluno, new[] { h23, y27 }, new[] { h24, y28 }, Turno23,
                                                    CustoMin23, Caminhada23, false, 2000, 120));
        Assert.Equal(11, h23.Paradas.Count);                 // nada foi mexido
    }

    [Fact]
    public void CustoMarginal_ParadaNoMesmoVerticeDeOutra_CustaZero()
    {
        var p = Aluno(2, 30);
        var v = Viagem(1, 1, 1, Aluno(1, 30), p, Aluno(3, 50), Escola(10, 100));
        Assert.Equal(0f, Insercao.CustoMarginal(v, p, Custo));
        Assert.Equal(20f, Insercao.CustoMarginal(v, v.Paradas[2], (a, b) => (Math.Min(a, b), Math.Max(a, b)) switch
        { (30, 50) => 20, (50, 100) => 50, (30, 100) => 50, _ => 99 }));
    }
}
