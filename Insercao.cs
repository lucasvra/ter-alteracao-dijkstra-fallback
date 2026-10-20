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
            // 0007: desvio negativo (matriz não métrica: matriz × fallback Dijkstra, arredondamento)
            // vira 0 e, no empate, vence a posição COLADA a uma parada no mesmo vértice — evita
            // revisitar um vértice que a viagem já tem (D5).
            var (ini, fim) = JanelaInsercao(viagem, idxEscola);
            var (pos, minDesvio) = MelhorPosicao(vertices, ini, fim, aluno.VerticeParada, custo);

            if (pos < 0 || !float.IsFinite(minDesvio)) return null;
            if (!(minDesvio < tempoMaximo) || duracao + minDesvio > tempoMaximo) return null;

            return new CandidatoInsercao(viagem, TipoInsercao.Inserir, pos + 1, aluno.VerticeParada, minDesvio, duracao, 0f);
        }

        public const float EpsilonDesvio = 1e-3f;

        /// <summary>
        /// 0007: melhor i em [ini, fim] para inserir o vértice x entre vertices[i] e vertices[i+1].
        /// Desvio &lt; 0 é tratado como 0 (desigualdade triangular violada); em empate (± eps),
        /// prefere a posição colada a um vértice igual a x, depois a que não parte um grupo de
        /// paradas no mesmo vértice, depois a primeira.
        /// </summary>
        public static (int pos, float desvio) MelhorPosicao(IReadOnlyList<int> vertices, int ini, int fim, int x, Func<int, int, float> custo)
        {
            // qualidade no empate: 0 = colado a um vértice igual a x; 1 = neutro;
            // 2 = parte um grupo de paradas no mesmo vértice (a == b != x) → revisita certa.
            static int Qualidade(int a, int b, int x) => a == x || b == x ? 0 : a == b ? 2 : 1;

            var minDesvio = float.PositiveInfinity;
            var minQ = int.MaxValue;
            var pos = -1;
            for (var i = ini; i <= fim; i++)
            {
                var a = vertices[i];
                var b = vertices[i + 1];
                var d = (a == x ? 0 : custo(a, x)) + (x == b ? 0 : custo(x, b)) - (a == b ? 0 : custo(a, b));
                if (float.IsNaN(d)) continue;
                d = Math.Max(d, 0f);
                var q = Qualidade(a, b, x);
                if (d < minDesvio - EpsilonDesvio || (d <= minDesvio + EpsilonDesvio && q < minQ))
                {
                    minDesvio = d;
                    minQ = q;
                    pos = i;
                }
            }
            return (pos, minDesvio);
        }

        /// <summary>
        /// 0007: reposiciona uma parada JÁ existente (troca in loco de vértice) na melhor posição da
        /// mesma viagem, respeitando o lado da escola. Guarda de tempo: <see cref="RespeitaTempo"/>.
        /// 0009: e <see cref="NaoPioraReordenacao"/> — só aceita a nova ordem se a duração E o km
        /// (distancia = índice 0 da matriz, metros) não piorarem em relação à ordem atual.
        /// Resolve o caso Y–X–Y (aluno do meio de um grupo em Y trocado para X) e X duplicado.
        /// </summary>
        public static bool ReposicionaParada(Viagens viagem, Paradas parada, int escola, Func<int, int, float> custo,
            float tempoMaximo = float.PositiveInfinity, Func<int, int, float>? distancia = null)
        {
            var original = viagem.Paradas.IndexOf(parada);
            if (original < 0) return false;
            var antes = Duracao(viagem.Paradas.Select(p => p.Vertice).ToList(), custo);
            var kmAntes = distancia == null ? 0f : Duracao(viagem.Paradas.Select(p => p.Vertice).ToList(), distancia);

            viagem.Paradas.RemoveAt(original);
            var idxEscola = IndiceEscola(viagem, escola);
            if (idxEscola < 0 || viagem.Paradas.Count < 2) { viagem.Paradas.Insert(original, parada); return false; }

            var vertices = viagem.Paradas.Select(p => p.Vertice).ToList();
            var (ini, fim) = JanelaInsercao(viagem, idxEscola);
            var (pos, _) = MelhorPosicao(vertices, ini, fim, parada.Vertice, custo);
            if (pos < 0) { viagem.Paradas.Insert(original, parada); return false; }

            viagem.Paradas.Insert(pos + 1, parada);
            var depois = Duracao(viagem.Paradas.Select(p => p.Vertice).ToList(), custo);
            if (pos + 1 == original) return false;
            var kmDepois = distancia == null ? 0f : Duracao(viagem.Paradas.Select(p => p.Vertice).ToList(), distancia);
            if (RespeitaTempo(antes, depois, tempoMaximo) && NaoPioraReordenacao(antes, depois, kmAntes, kmDepois)) return true;

            Log.WriteLine($"\tViagem {viagem.Viagem:000}: reposicionamento do vértice {parada.Vertice} recusado " +
                          $"(duração {antes:0.0} -> {depois:0.0} min, km {kmAntes / 1000:0.00} -> {kmDepois / 1000:0.00}, " +
                          $"máximo {tempoMaximo}; reordenação não pode piorar tempo nem km)", ConsoleColor.Yellow);
            viagem.Paradas.Remove(parada);
            viagem.Paradas.Insert(original, parada);
            return false;
        }

        /// <summary>
        /// 0007: ordem estável que junta itens do mesmo vértice na posição da 1ª ocorrência
        /// (usada em Rota.ToViagem). Numa métrica, tirar uma visita repetida nunca aumenta o percurso.
        /// </summary>
        public static List<T> AgrupaPorVertice<T>(IEnumerable<T> itens, Func<T, int> vertice) =>
            itens.Select((x, i) => (x, i))
                 .GroupBy(t => vertice(t.x))
                 .OrderBy(g => g.Min(t => t.i))
                 .SelectMany(g => g.OrderBy(t => t.i).Select(t => t.x))
                 .ToList();

        /// <summary>
        /// 0007: garante UM bloco por vértice na viagem (D5 = 0). Para cada vértice em mais de um bloco,
        /// as paradas de aluno (tipo 1) desse vértice são levadas para um dos blocos existentes dele
        /// (ida: início do bloco; volta: fim do bloco), escolhendo o de menor duração entre os que
        /// respeitam o lado da escola de cada aluno (ida: antes da escola; volta: depois).
        /// Paradas de escola (tipo 2/0) não se movem: se o vértice tem escola, o alvo é o bloco dela.
        /// Não junta (e registra) quando nenhuma opção é factível (ex.: aluno de A no vértice de uma
        /// parada que fica depois da escola A) ou quando a guarda de TempoMaximo (RespeitaTempo) barra.
        /// Vale para viagens herdadas, inserções e trocas in loco.
        /// </summary>
        /// <returns>Número de vértices fundidos.</returns>
        public static int AgrupaVerticesIguais(Viagens viagem, Func<int, int?> escolaDoAluno, Func<int, int, float> custo,
            float tempoMaximo = float.PositiveInfinity, Func<int, int, float>? distancia = null)
        {
            var fundidos = 0;
            var impossiveis = new HashSet<int>();
            var volta = viagem.Sentido == SentidoVolta;

            for (var guarda = 0; guarda < 1000; guarda++)
            {
                var blocos = Blocos(viagem.Paradas);
                var dup = blocos.GroupBy(b => b.vertice)
                                .FirstOrDefault(g => g.Count() > 1 && !impossiveis.Contains(g.Key));
                if (dup == null) break;

                var v = dup.Key;
                var fixos = dup.Where(b => viagem.Paradas.Skip(b.ini).Take(b.fim - b.ini + 1).Any(p => p.Tipo != 1)).ToList();
                if (fixos.Count > 1) { impossiveis.Add(v); continue; }
                var alvos = fixos.Count == 1 ? fixos : dup.ToList();

                var antes = Duracao(viagem.Paradas.Select(p => p.Vertice).ToList(), custo);
                var kmAntes = distancia == null ? 0f : Duracao(viagem.Paradas.Select(p => p.Vertice).ToList(), distancia);
                List<Paradas>? melhor = null;
                var melhorDur = float.PositiveInfinity;
                var melhorRecusada = float.PositiveInfinity; // menor duração factível barrada pela guarda (log)
                var kmRecusada = float.NaN;
                foreach (var alvo in alvos)
                {
                    var noAlvo = viagem.Paradas.Skip(alvo.ini).Take(alvo.fim - alvo.ini + 1).ToList();
                    var mover = viagem.Paradas.Where((p, i) => p.Vertice == v && p.Tipo == 1 && (i < alvo.ini || i > alvo.fim)).ToList();
                    var lista = viagem.Paradas.Where(p => !mover.Contains(p)).ToList();
                    var pos = volta ? lista.IndexOf(noAlvo[^1]) + 1 : lista.IndexOf(noAlvo[0]);
                    lista.InsertRange(pos, mover);

                    if (!mover.All(p => LadoDaEscolaOk(lista, p, escolaDoAluno(p.Codigo), volta))) continue;
                    var dur = Duracao(lista.Select(p => p.Vertice).ToList(), custo);
                    if (!float.IsFinite(dur)) continue;
                    var km = distancia == null ? 0f : Duracao(lista.Select(p => p.Vertice).ToList(), distancia);
                    if (!RespeitaTempo(antes, dur, tempoMaximo) || !NaoPioraReordenacao(antes, dur, kmAntes, km))
                    {
                        if (dur < melhorRecusada) { melhorRecusada = dur; kmRecusada = km; }
                        continue;
                    }
                    if (dur < melhorDur - EpsilonDesvio) { melhorDur = dur; melhor = lista; }
                }

                if (melhor == null)
                {
                    if (float.IsFinite(melhorRecusada))
                        Log.WriteLine($"\tViagem {viagem.Viagem:000}: vértice {v} não reagrupado — duração {antes:0.0} -> {melhorRecusada:0.0} min, " +
                                      $"km {kmAntes / 1000:0.00} -> {kmRecusada / 1000:0.00}, máximo {tempoMaximo} " +
                                      "(reordenação não pode piorar tempo nem km)", ConsoleColor.Yellow);
                    impossiveis.Add(v);
                    continue;
                }
                viagem.Paradas.Clear();
                viagem.Paradas.AddRange(melhor);
                fundidos++;
            }

            foreach (var v in impossiveis)
                Log.WriteLine($"\tViagem {viagem.Viagem:000}: vértice {v} fica em mais de um bloco (escola, lado da escola ou TempoMaximo impedem juntar)", ConsoleColor.Yellow);
            return fundidos;
        }

        /// <summary>
        /// 0007: guarda de tempo do reagrupamento/reposicionamento. Viagem dentro do limite
        /// (antes &lt;= TempoMaximo): aceita se depois &lt;= TempoMaximo. Viagem que já estourava:
        /// aceita só se não piorar (depois &lt;= antes, ± eps).
        /// </summary>
        public static bool RespeitaTempo(float antes, float depois, float tempoMaximo)
        {
            if (!float.IsFinite(depois)) return false;
            if (float.IsFinite(antes) && antes > tempoMaximo + EpsilonDesvio) return depois <= antes + EpsilonDesvio;
            return depois <= tempoMaximo + EpsilonDesvio;
        }

        public const float EpsilonMetros = 1f;

        /// <summary>
        /// 0009: guarda de reordenação pura (reposicionamento após a troca in loco e AgrupaVerticesIguais).
        /// Manter a ordem atual é sempre viável, então a nova ordem só é aceita se a duração (min) E o
        /// km (metros, índice 0 da matriz / fallback) forem &lt;= os atuais (± eps). Complementa
        /// <see cref="RespeitaTempo"/> (TempoMaximo). km infinito antes (par sem caminho) não bloqueia.
        /// </summary>
        public static bool NaoPioraReordenacao(float minAntes, float minDepois, float mAntes, float mDepois)
        {
            if (!float.IsFinite(minDepois)) return false;
            if (float.IsFinite(minAntes) && minDepois > minAntes + EpsilonDesvio) return false;
            if (!float.IsFinite(mAntes)) return true;
            return float.IsFinite(mDepois) && mDepois <= mAntes + EpsilonMetros;
        }

        /// <summary>Blocos de paradas consecutivas no mesmo vértice (índices inclusivos).</summary>
        public static List<(int vertice, int ini, int fim)> Blocos(IReadOnlyList<Paradas> paradas)
        {
            var blocos = new List<(int vertice, int ini, int fim)>();
            for (var i = 0; i < paradas.Count; i++)
            {
                if (blocos.Count > 0 && blocos[^1].vertice == paradas[i].Vertice)
                    blocos[^1] = (blocos[^1].vertice, blocos[^1].ini, i);
                else
                    blocos.Add((paradas[i].Vertice, i, i));
            }
            return blocos;
        }

        private static bool LadoDaEscolaOk(List<Paradas> lista, Paradas p, int? escola, bool volta)
        {
            if (escola is not int e) return true;
            var idxEscola = lista.FindIndex(x => x.Tipo == 2 && x.Codigo == e);
            if (idxEscola < 0) return true;
            var idx = lista.IndexOf(p);
            return volta ? idx > idxEscola : idx < idxEscola;
        }

        /// <summary>0010: proposta de mover um aluno trocado in loco da viagem herdada (ida+volta) para outras.</summary>
        public sealed record RealocacaoInLoco(Viagens HerdadaIda, Viagens HerdadaVolta, CandidatoInsercao Ida,
                                              CandidatoInsercao Volta, float DesvioHerdada, float DesvioNovo);

        /// <summary>0010: minutos que a parada p acrescenta à viagem (duração com p − duração sem p).</summary>
        public static float CustoMarginal(Viagens viagem, Paradas p, Func<int, int, float> custo)
        {
            var com = viagem.Paradas.Select(x => x.Vertice).ToList();
            var sem = viagem.Paradas.Where(x => !ReferenceEquals(x, p)).Select(x => x.Vertice).ToList();
            return Duracao(com, custo) - Duracao(sem, custo);
        }

        /// <summary>
        /// 0010: troca in loco (aluno mudou de vértice, mesma escola e turno). Compara o desvio que o
        /// aluno custa na viagem herdada (ida + volta, custo marginal na posição atual) com o melhor
        /// candidato da 0006 (<see cref="MelhorCandidato"/>: escola e turno, lotação, janela da escola,
        /// TempoMaximo, Mover antes de Inserir no empate) nas OUTRAS viagens de cada sentido.
        /// Ida e volta decididas juntas: só propõe se houver candidato nos dois sentidos e a soma dos
        /// desvios for menor que a da herdada. Null = fica na herdada. Não muta nada.
        /// </summary>
        public static RealocacaoInLoco? MelhorRealocacaoInLoco(Viagens herdadaIda, Viagens herdadaVolta, Alunos aluno,
            IEnumerable<Viagens> viagensIda, IEnumerable<Viagens> viagensVolta, IReadOnlyDictionary<int, short> turnoPorId,
            Func<Viagens, int, int, float> custo, Func<int, int, float> caminhada, bool consideraSentido,
            double limiarCaminhada, float tempoMaximo)
        {
            var pi = herdadaIda.Paradas.FirstOrDefault(p => p.Tipo == 1 && p.Codigo == aluno.Seq);
            var pv = herdadaVolta.Paradas.FirstOrDefault(p => p.Tipo == 1 && p.Codigo == aluno.Seq);
            if (pi == null || pv == null) return null;

            var dh = CustoMarginal(herdadaIda, pi, (a, b) => custo(herdadaIda, a, b))
                   + CustoMarginal(herdadaVolta, pv, (a, b) => custo(herdadaVolta, a, b));
            if (!float.IsFinite(dh) || dh <= EpsilonDesvio) return null;

            var ci = MelhorCandidato(viagensIda.Where(v => !ReferenceEquals(v, herdadaIda)), aluno, turnoPorId, custo,
                                     caminhada, consideraSentido, limiarCaminhada, tempoMaximo);
            if (ci == null) return null;
            var cv = MelhorCandidato(viagensVolta.Where(v => !ReferenceEquals(v, herdadaVolta)), aluno, turnoPorId, custo,
                                     caminhada, consideraSentido, limiarCaminhada, tempoMaximo);
            if (cv == null) return null;

            var dn = ci.Desvio + cv.Desvio;
            return dn < dh - EpsilonDesvio ? new RealocacaoInLoco(herdadaIda, herdadaVolta, ci, cv, dh, dn) : null;
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
