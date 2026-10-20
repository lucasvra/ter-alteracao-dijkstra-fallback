using Otimizacao;

namespace ter_otimizacao
{
    public enum TipoInsercao { Mover = 0, Inserir = 1 }

    /// <summary>Candidato de entrada de um aluno numa viagem existente (reaproveitada).</summary>
    /// <param name="Posicao">Índice da lista Paradas em que a nova parada será inserida.</param>
    /// <param name="Desvio">Minutos acrescentados à viagem (0 no Mover: mesmo vértice de uma parada existente).</param>
    /// <param name="DuracaoAtual">Soma da matriz sobre as paradas atuais (antes da inserção).</param>
    /// <param name="Caminhada">Distância a pé até o vértice (Mover); 0 no Inserir.</param>
    public sealed record CandidatoInsercao(Viagens Viagem, TipoInsercao Tipo, int Posicao, int Vertice,
                                           float Desvio, float DuracaoAtual, float Caminhada);

    /// <summary>
    /// Lógica pura (sem banco) da entrada de alunos em viagens reaproveitadas (0006):
    /// filtro obrigatório de escola e turno, janela de posições respeitando a escola do aluno
    /// (ida: antes da escola; volta: depois), duração ATUAL + desvio &lt;= TempoMaximo e escolha
    /// da viagem de MENOR desvio entre as compatíveis (não a primeira que aceita).
    /// </summary>
    public static class Insercao
    {
        public const short SentidoIda = 1;
        public const short SentidoVolta = 2;

        /// <summary>
        /// alunos_req.turno é o id de turnos (Turnos.Id); viagem_saida.turno é o código
        /// (Turnos.Turno, o mesmo usado no GroupBy de AtualizaSentido/OtimizaTurno).
        /// </summary>
        public static Dictionary<int, short> MapaTurnoPorId(IEnumerable<Turnos> turnos) =>
            turnos.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First().Turno);

        public static int IndiceEscola(Viagens viagem, int escola) =>
            viagem.Paradas.FindIndex(p => p.Tipo == 2 && p.Codigo == escola);

        /// <summary>Viagem atende a escola do aluno (parada tipo 2) e o turno dele.</summary>
        public static bool ViagemCompativel(Viagens viagem, Alunos aluno, IReadOnlyDictionary<int, short> turnoPorId)
        {
            if (aluno.Escola is not int escola) return false;
            if (!turnoPorId.TryGetValue(aluno.Turno, out var turno) || viagem.Turno != turno) return false;
            return IndiceEscola(viagem, escola) >= 0;
        }

        /// <summary>
        /// Intervalo [ini, fim] de i para inserir ENTRE Paradas[i] e Paradas[i+1].
        /// Ida: o aluno embarca antes da escola dele (i+1 &lt;= idxEscola).
        /// Volta: desembarca depois da escola dele (i &gt;= idxEscola).
        /// Para viagem de uma escola só, coincide com o intervalo antigo (0..Count-2).
        /// </summary>
        public static (int ini, int fim) JanelaInsercao(Viagens viagem, int idxEscola) =>
            viagem.Sentido == SentidoVolta
                ? (idxEscola, viagem.Paradas.Count - 2)
                : (0, Math.Min(idxEscola - 1, viagem.Paradas.Count - 2));

        public static float Duracao(IReadOnlyList<int> vertices, Func<int, int, float> custo)
        {
            float total = 0;
            for (var i = 1; i < vertices.Count; i++)
            {
                total += vertices[i - 1] == vertices[i] ? 0 : custo(vertices[i - 1], vertices[i]);
                if (float.IsInfinity(total)) break;
            }
            return total;
        }

        /// <summary>
        /// Melhor candidato de UMA viagem (null se incompatível, lotada ou se nada cabe).
        /// custo(v1, v2) = minutos para o tipo de veículo da viagem; caminhada(v1, v2) = índice 0.
        /// </summary>
        public static CandidatoInsercao? MelhorNaViagem(Viagens viagem, Alunos aluno,
            IReadOnlyDictionary<int, short> turnoPorId, Func<int, int, float> custo,
            Func<int, int, float> caminhada, bool consideraSentido, double limiarCaminhada, float tempoMaximo)
        {
            if (!ViagemCompativel(viagem, aluno, turnoPorId)) return null;
            if (viagem.Paradas.Count(p => p.Tipo == 1) >= viagem.CapacidadeVeiculo) return null;

            var idxEscola = IndiceEscola(viagem, aluno.Escola!.Value);
            var vertices = viagem.Paradas.Select(p => p.Vertice).ToList();
            var duracao = Duracao(vertices, custo);
            if (!float.IsFinite(duracao)) return null;

            // Mover: o aluno caminha até o vértice de uma parada existente (desvio 0 no veículo).
            // Ida: parada antes da escola; volta: depois da escola.
            CandidatoInsercao? mover = null;
            for (var j = 0; j < viagem.Paradas.Count; j++)
            {
                var p = viagem.Paradas[j];
                if (p.Tipo != 1) continue;
                if (viagem.Sentido == SentidoVolta ? j <= idxEscola : j >= idxEscola) continue;

                var ida = caminhada(aluno.VerticeVia, p.Vertice);
                var dist = consideraSentido ? Math.Max(ida, caminhada(p.Vertice, aluno.VerticeVia)) : ida;
                if (!float.IsFinite(dist) || aluno.DistanciaVia + dist > limiarCaminhada) continue;
                if (mover == null || dist < mover.Caminhada)
                    mover = new CandidatoInsercao(viagem, TipoInsercao.Mover, j + 1, p.Vertice, 0f, duracao, dist);
            }
            if (mover != null) return mover;

            // Inserir: novo vértice entre Paradas[i] e Paradas[i+1], menor desvio em minutos.
            var (ini, fim) = JanelaInsercao(viagem, idxEscola);
            var minDesvio = float.PositiveInfinity;
            var pos = -1;
            for (var i = ini; i <= fim; i++)
            {
                var a = vertices[i];
                var b = vertices[i + 1];
                var d = (a == aluno.VerticeParada ? 0 : custo(a, aluno.VerticeParada))
                      + (aluno.VerticeParada == b ? 0 : custo(aluno.VerticeParada, b))
                      - (a == b ? 0 : custo(a, b));
                if (d < minDesvio) { minDesvio = d; pos = i; }
            }

            if (pos < 0 || !float.IsFinite(minDesvio)) return null;
            if (!(minDesvio < tempoMaximo) || duracao + minDesvio > tempoMaximo) return null;

            return new CandidatoInsercao(viagem, TipoInsercao.Inserir, pos + 1, aluno.VerticeParada, minDesvio, duracao, 0f);
        }

        /// <summary>
        /// Melhor candidato entre todas as viagens: menor desvio; empate → Mover antes de
        /// Inserir, menor caminhada, menor ocupação, menor número de viagem (determinístico).
        /// </summary>
        public static CandidatoInsercao? MelhorCandidato(IEnumerable<Viagens> viagens, Alunos aluno,
            IReadOnlyDictionary<int, short> turnoPorId, Func<Viagens, int, int, float> custo,
            Func<int, int, float> caminhada, bool consideraSentido, double limiarCaminhada, float tempoMaximo)
        {
            return viagens
                .Select(v => MelhorNaViagem(v, aluno, turnoPorId, (a, b) => custo(v, a, b), caminhada,
                                            consideraSentido, limiarCaminhada, tempoMaximo))
                .Where(c => c != null)
                .Select(c => c!)
                .OrderBy(c => c.Desvio)
                .ThenBy(c => c.Tipo)
                .ThenBy(c => c.Caminhada)
                .ThenBy(c => c.Viagem.Paradas.Count(p => p.Tipo == 1))
                .ThenBy(c => c.Viagem.Viagem)
                .FirstOrDefault();
        }
    }
}
