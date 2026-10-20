using Otimizacao;
using ter_otimizacao;
using Xunit;

namespace ter_alteracao.Tests;

public class InsercaoTests
{
    // Vértices numa reta: custo (min) = |a - b|. Caminhada idem (índice 0).
    private static float Custo(int a, int b) => Math.Abs(a - b);
    private static float CustoV(Viagens _, int a, int b) => Custo(a, b);

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
        Assert.True(Insercao.ReposicionaParada(v, meio, 10, Custo));
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
        Assert.False(Insercao.ReposicionaParada(v, p, 10, Custo));
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
        var n = Insercao.AgrupaVerticesIguais(v, _ => 10, Custo);
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
        Insercao.AgrupaVerticesIguais(v, _ => 10, Custo);
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
        Insercao.AgrupaVerticesIguais(v, _ => 10, custo);
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
        Assert.Equal(0, Insercao.AgrupaVerticesIguais(v, esc, Custo));
        Assert.Equal(new[] { 40, 50, 60, 40 }, v.Paradas.Select(p => p.Vertice).ToArray());

        // Já o caso "aluno de outra escola embarca antes" é factível e junta:
        var w = Viagem(2, 1, 1, Aluno(1, 30), Escola(20, 40), Aluno(2, 30), Escola(10, 100));
        int? esc2(int seq) => seq == 1 ? 20 : 10;
        Assert.Equal(1, Insercao.AgrupaVerticesIguais(w, esc2, Custo));
        Assert.Equal(new[] { 30, 30, 40, 100 }, w.Paradas.Select(p => p.Vertice).ToArray());
    }

    [Fact]
    public void VerticeDeEscola_AlunoVaiParaOBlocoDaEscola()
    {
        // Volta: escola 10 em 0, aluno em 0 lá na frente → junta ao bloco da escola (depois dela).
        var v = Viagem(2, 2, 1, Escola(10, 0), Aluno(1, 20), Aluno(2, 0), Aluno(3, 40));
        Assert.Equal(1, Insercao.AgrupaVerticesIguais(v, _ => 10, Custo));
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

        // Dentro do limite também aceita piorar, desde que fique <= TempoMaximo (17 → 36, limite 40).
        var t = ViagemTorta();
        Assert.Equal(1, Insercao.AgrupaVerticesIguais(t, _ => 10, CustoTorto, tempoMaximo: 40));
        Assert.Equal(0, Revisitas(t));
        Assert.Equal(36f, Dur(t, CustoTorto));
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
            Insercao.ReposicionaParada(v, p, 10, Custo167, tempoMaximo: 120);
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
        Insercao.AgrupaVerticesIguais(v, _ => 10, Custo167, tempoMaximo: 120);
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
}
