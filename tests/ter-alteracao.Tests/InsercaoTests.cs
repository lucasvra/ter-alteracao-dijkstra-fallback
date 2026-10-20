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
}
