using Otimizacao;

namespace ter_otimizacao
{
    internal class Rota
    {
        List<Alunos> alunos;
        private DateTime tempoIda;
        private DateTime tempoVolta;
        readonly Requisicoes requisicao;
        public int TipoVeiculo { get; private set; }
        public float Ocupacao { get; }
        public float Custo { get; private set; }
        public float CustoAluno { get; private set; }
        public int Capacidade { get; private set; }
        public bool AtravessaEscola { get; private set; }
        public bool Factivel => TipoVeiculo > 0 && !float.IsInfinity(Tempo) && !float.IsInfinity(Custo) && Tempo <= requisicao.TempoMaximo;
        public int AlunosEspeciais { get; private set; }
        public bool Monitor => alunos.Any(a => a.TipoEnsino is 1 or 2); //VERIFICAR
        public Escolas[] Escolas { get; }
        public Alunos[] Alunos => alunos.ToArray();
        public float Tempo { get; private set; }
        public short Turno { get; }


        public Rota(Requisicoes req, Escolas[] escolap, IEnumerable<Alunos> alunosp, short turno)
        {
            alunosp = alunosp.Select(a => a.Clone()).ToList();
            AlunosEspeciais = alunosp.Count(a => a.VeiculoEspecial);
            requisicao = req;
            Escolas = escolap;
            Turno = turno;
            alunos = new List<Alunos>(alunosp.Count());

            (TipoVeiculo, Capacidade) = requisicao.TipoVeiculo(alunosp.Count(), alunosp.Any(a => (a.TipoEnsino is 1 or 2) || (a.PossuiDeficiencia)), AlunosEspeciais);

            var tempoIdaAcumulado = 0.0;
            var tempoVoltaAcumulado = 0.0;

            if (TipoVeiculo == 0)
            {
                Tempo = float.PositiveInfinity;
                Custo = float.PositiveInfinity;

                return;
            }

            var transportaveis = alunosp.ToList();
            var vertice = escolap[0].Vertice;

            var ida = escolap[0].ChegadaTurno(turno);
            var volta = escolap[0].SaidaTurno(turno);

            for (int i = 1; i < Escolas.Length; i++)
            {
                var auxTmpIda = req.Distancias[escolap[i].Vertice, vertice, TipoVeiculo];
                var auxTmpVolta = req.Distancias[vertice, escolap[i].Vertice, TipoVeiculo];

                tempoIdaAcumulado += auxTmpIda;
                tempoVoltaAcumulado += auxTmpVolta;

                if (float.IsInfinity(auxTmpIda) || float.IsInfinity(auxTmpVolta) ||
                    auxTmpIda > req.TempoMaximo || auxTmpVolta > req.TempoMaximo)
                {
                    Custo = float.PositiveInfinity;
                    Tempo = float.PositiveInfinity;
                    return;
                }

                ida = (ida - new TimeSpan(0, (int)auxTmpIda, 0)) | escolap[i].ChegadaTurno(turno);
                volta = (volta + new TimeSpan(0, (int)auxTmpVolta, 0)) | escolap[i].SaidaTurno(turno);

                if (ida == null || volta == null)
                {
                    Custo = float.PositiveInfinity;
                    Tempo = float.PositiveInfinity;
                    return;
                }

                vertice = escolap[i].Vertice;
            }

            tempoIda = (ida - new TimeSpan(0, (int)tempoIdaAcumulado, 0)).Inicio;
            tempoVolta = (volta + new TimeSpan(0, (int)tempoVoltaAcumulado, 0)).Inicio;

            while (transportaveis.Any())
            {
                var proximo = transportaveis.OrderBy(a => req.ConsideraSentido
                    ? Math.Max(
                        req.Distancias[a.VerticeParada, vertice, TipoVeiculo],
                        req.Distancias[vertice, a.VerticeParada, TipoVeiculo])
                    : req.Distancias[a.VerticeParada, vertice, TipoVeiculo]).First();
                alunos.Add(proximo);
                transportaveis.Remove(proximo);

                if (!AtravessaEscola)
                {
                    foreach (var esc in Escolas)
                    {
                        var atravessaIda = req.Distancias[proximo.VerticeParada, vertice, TipoVeiculo] ==
                            req.Distancias[proximo.VerticeParada, esc.Vertice, TipoVeiculo] + req.Distancias[esc.Vertice, vertice, TipoVeiculo];
                        var atravessaVolta = req.Distancias[vertice, proximo.VerticeParada, TipoVeiculo] ==
                            req.Distancias[vertice, esc.Vertice, TipoVeiculo] + req.Distancias[esc.Vertice, proximo.VerticeParada, TipoVeiculo];

                        AtravessaEscola |=
                            ((req.ConsideraSentido ? atravessaIda || atravessaVolta : atravessaIda)
                            && esc.Vertice != vertice
                            && proximo.VerticeParada != esc.Vertice);
                    }
                }

                tempoIdaAcumulado += req.Distancias[proximo.VerticeParada, vertice, TipoVeiculo];
                tempoVoltaAcumulado += req.Distancias[vertice, proximo.VerticeParada, TipoVeiculo];
                vertice = proximo.VerticeParada;
            }

            Ocupacao = (Capacidade - (float)alunos.Count) / Capacidade;
            Tempo = req.ConsideraSentido
                ? (float)Math.Max(tempoIdaAcumulado, tempoVoltaAcumulado)
                : (float)tempoIdaAcumulado;

            var tempoBaseCusto = req.ConsideraSentido
                ? (tempoIdaAcumulado + tempoVoltaAcumulado) / 2
                : tempoIdaAcumulado;
            Custo = (float)CustoTempo(tempoBaseCusto, TipoVeiculo, Monitor, false);
            CustoAluno = Custo / Ocupacao;

            if (AlunosEspeciais > 3)
                Log.WriteLine($"Erro ... {alunos.Count} alunos, {AlunosEspeciais} especiais, veiculo tipo {TipoVeiculo} e custo {Custo}", ConsoleColor.Red);
        }

        private float CalculaCustoRota(int[] verticesParadas, int escolaVertice)
        {
            float tempoIda = 0;
            float tempoVolta = 0;
            var verticeIda = escolaVertice;
            var verticeVolta = escolaVertice;

            foreach (var vertice in verticesParadas)
            {
                tempoIda += requisicao.Distancias[vertice, verticeIda, TipoVeiculo];
                tempoVolta += requisicao.Distancias[verticeVolta, vertice, TipoVeiculo];
                verticeIda = vertice;
                verticeVolta = vertice;
            }

            if (!requisicao.ConsideraSentido) return tempoVolta;
            if (float.IsInfinity(tempoIda) || float.IsInfinity(tempoVolta)) return float.PositiveInfinity;
            return tempoIda + tempoVolta;
        }

        private double CustoTempo(double tempo, int tipoVeiculo, bool monitor, bool penalidade)
        {
            double custoFixo = double.PositiveInfinity, precoCombustivel = 0, velocidade = 0, diasMes = 20, consumo = 1;

            switch (tipoVeiculo)
            {
                // custo fixo + preco_combust [litro] x (tempo [horas] x velocidade [km/h] x dias_mes / consumo [km/L] 
                case 1:
                    custoFixo = penalidade ? 3000 : 1290; //1466 p/ 100km
                    custoFixo += monitor ? 950 : 0;
                    precoCombustivel = 4.4;
                    velocidade = 40.0;
                    diasMes = 20.0;
                    consumo = 6.0;
                    break;
                case 2: //return custofixo + 3.5 * ((tempo/60) * 40 * 20)/ 9;
                    custoFixo = penalidade ? 2500 : 1290; //1750 p/ 100km
                    custoFixo += monitor ? 950 : 0;
                    precoCombustivel = 3.5;
                    velocidade = 40.0;
                    diasMes = 20.0;
                    consumo = 4.0;
                    break;
                case 3: //return custofixo + 3.5 * ((tempo/60) * 40 * 20)/ 10;
                    custoFixo = penalidade ? 2000 : 1290; //2187 p/ 100km
                    custoFixo += monitor ? 950 : 0;
                    precoCombustivel = 3.5;
                    velocidade = 40.0;
                    diasMes = 20.0;
                    consumo = 3.2;
                    break;
                case 4: //return custofixo + 3.5 * ((tempo/60) * 40 * 20)/ 2.5;
                    custoFixo = penalidade ? 1500 : 1290; //2500 p/ 100km
                    custoFixo += monitor ? 950 : 0;
                    precoCombustivel = 3.5;
                    velocidade = 40.0;
                    diasMes = 20.0;
                    consumo = 2.8;
                    break;
            }
            return custoFixo + precoCombustivel * (tempo / 60) * velocidade * diasMes / consumo;
        }

        public bool Reordenacao(bool log = true, int changes = 500)
        {
            var vertices = alunos.Select(a => a.VerticeParada).Distinct().ToArray();
            int num = vertices.Length;
            var escola = Escolas.Last();
            var custo = CalculaCustoRota(vertices, escola.Vertice);

            var rnd = new Random(requisicao.Requisicao);
            for (int it = 0; it < changes * num; it++)
            {
                var v1 = rnd.Next(num);
                var v2 = rnd.Next(num);

                if (v1 == v2) continue;
                if (v1 > v2) (v1, v2) = (v2, v1);

                var custo2 = custo + CalcularDeltaTroca(vertices, escola.Vertice, v1, v2);

                if (custo2 < custo)
                {
                    if (log) Log.WriteLine($"Otm. por Reordenação gerou ganho de custo de {custo - custo2: 0.00} min");
                    (vertices[v1], vertices[v2]) = (vertices[v2], vertices[v1]);
                    custo = custo2;
                }
            }

            var dic = vertices.Select((v, ind) => (v, ind)).ToDictionary(o => o.v, o => o.ind);
            alunos = alunos.OrderBy(a => dic[a.VerticeParada]).ToList();

            AtualizaParametros();

            return true;
        }

        private float CalcularDeltaTroca(int[] vertices, int escolaVertice, int pos1, int pos2)
        {
            int VerticeCaminho(int posicao, bool troca)
            {
                if (posicao == 0) return escolaVertice;

                var indice = posicao - 1;
                if (!troca) return vertices[indice];
                if (indice == pos1) return vertices[pos2];
                if (indice == pos2) return vertices[pos1];
                return vertices[indice];
            }

            float CustoAresta(int origem, int destino)
            {
                var custo = requisicao.Distancias[origem, destino, TipoVeiculo];
                return requisicao.ConsideraSentido
                    ? custo + requisicao.Distancias[destino, origem, TipoVeiculo]
                    : custo;
            }

            var arestasAfetadas = new[] { pos1 + 1, pos1 + 2, pos2 + 1, pos2 + 2 }
                .Where(posicao => posicao >= 1 && posicao <= vertices.Length)
                .Distinct();

            float custoAntigo = 0;
            float custoNovo = 0;
            foreach (var posicao in arestasAfetadas)
            {
                custoAntigo += CustoAresta(
                    VerticeCaminho(posicao - 1, false),
                    VerticeCaminho(posicao, false));
                custoNovo += CustoAresta(
                    VerticeCaminho(posicao - 1, true),
                    VerticeCaminho(posicao, true));
            }

            return custoNovo - custoAntigo;
        }

        public bool TroncoPrincipal1(bool log = true)
        {
            var escola = Escolas.Last();
            var custo1 = CalculaCustoRota(alunos.Select(a => a.VerticeParada).ToArray(), escola.Vertice);

            var vertices = new List<int>();
            int vertice = 0;

            foreach (var aluno in alunos)
            {
                if (aluno.VerticeParada == vertice) continue;

                vertice = aluno.VerticeParada;
                vertices.Add(vertice);

            }

            if (vertices.Count < 4) return false;

            bool mudou = true;

            while (mudou)
            {
                mudou = false;
                int n = vertices.Count;
                for (int i = 0; i < n - 2; i++)
                {
                    var ganhoVolta = requisicao.Distancias[vertices[i], vertices[i + 1], TipoVeiculo] +
                                      requisicao.Distancias[vertices[i + 1], vertices[i + 2], TipoVeiculo] -
                                      requisicao.Distancias[vertices[i], vertices[i + 2], TipoVeiculo];
                    var ganhoIda = requisicao.Distancias[vertices[i + 1], vertices[i], TipoVeiculo] +
                                   requisicao.Distancias[vertices[i + 2], vertices[i + 1], TipoVeiculo] -
                                   requisicao.Distancias[vertices[i + 2], vertices[i], TipoVeiculo];
                    var ganho = requisicao.ConsideraSentido ? Math.Min(ganhoIda, ganhoVolta) : ganhoVolta;

                    var alunosVertice = alunos.Where(a => a.VerticeParada == vertices[i + 1]).ToList();

                    if (ganho > 0 && alunosVertice.Any())
                    {
                        var d1Ida = requisicao.Distancias[vertices[i + 2], vertices[i + 1], 0];
                        var d1Volta = requisicao.Distancias[vertices[i + 1], vertices[i + 2], 0];
                        var d1 = requisicao.ConsideraSentido ? Math.Max(d1Ida, d1Volta) : d1Volta;
                        var d2Ida = requisicao.Distancias[vertices[i + 1], vertices[i], 0];
                        var d2Volta = requisicao.Distancias[vertices[i], vertices[i + 1], 0];
                        var d2 = requisicao.ConsideraSentido ? Math.Max(d2Ida, d2Volta) : d2Volta;

                        var (distanciaCaminhada, verticeDestino) = (new[]
                        {
                            (d1, vertices[i + 2]),
                            (d2, vertices[i])
                        }).OrderBy(t => t.Item1).First();

                        if (alunosVertice.Any(a => a.LimiarCaminhada(requisicao) < a.DistanciaVia + distanciaCaminhada)) continue;

                        foreach (var aluno in alunosVertice)
                        {
                            aluno.VerticeParada = verticeDestino;
                            aluno.DistanciaParada = aluno.DistanciaVia + distanciaCaminhada;
                        }

                        mudou = true;
                        vertices.RemoveAt(i + 1);
                        n--;
                    }

                }
            }
            var custo2 = CalculaCustoRota(alunos.Select(a => a.VerticeParada).ToArray(), escola.Vertice);
            if (custo1 - custo2 > 1 && log) Log.WriteLine($"Otm. do Tronco Principal 1 gerou ganho de {custo1 - custo2: 0.00} min");
            AtualizaParametros();
            return custo2 < custo1;
        }

        public bool TroncoPrincipal2(bool log = true, int maxIteracoes = 10)
        {
            requisicao.Distancias.dijkstra = true;
            var escola = Escolas.Last();
            var custo1 = CalculaCustoRota(alunos.Select(a => a.VerticeParada).ToArray(), escola.Vertice);
            var alunosBkp = alunos.Select(a => a.Clone()).ToList();

            var vertices = new List<int>();
            int vertice = 0;
            foreach (var aluno in alunos)
            {
                if (aluno.VerticeParada == vertice) continue;

                vertice = aluno.VerticeParada;
                vertices.Add(vertice);

            }

            if (vertices.Count < 4) return false;

            vertices = requisicao.Distancias.SequenciaVerticesOtimizada(vertices.ToArray(), TipoVeiculo);
            bool mudou = true;
            int iteracao = 0;

            while (mudou && iteracao < maxIteracoes)
            {
                mudou = false;
                iteracao++;
                int n = vertices.Count;
                for (int i = 0; i < n - 2; i++)
                {
                    var alunosVertice = alunos.Where(a => a.VerticeParada == vertices[i + 1]).ToList();

                    if (vertices[i] == vertices[i + 2] && alunosVertice.Any())
                    {
                        float distanciaIda(int v) => requisicao.Distancias.DistanciaDijkstra(v, vertices[i + 2], 0);
                        float distanciaVolta(int v) => requisicao.Distancias.DistanciaDijkstra(vertices[i + 2], v, 0);
                        float distanciaCaminhada(int v) => requisicao.ConsideraSentido
                            ? Math.Max(distanciaIda(v), distanciaVolta(v))
                            : distanciaVolta(v);
                        var dIda = requisicao.Distancias.DistanciaDijkstra(vertices[i + 1], vertices[i + 2], 0);
                        var dVolta = requisicao.Distancias.DistanciaDijkstra(vertices[i + 2], vertices[i + 1], 0);
                        var d = requisicao.ConsideraSentido ? Math.Max(dIda, dVolta) : dVolta;

                        if (float.IsInfinity(d) || float.IsNaN(d) || d < 10) continue;
                        if (alunosVertice.Any(a =>
                                a.DistanciaVia + distanciaCaminhada(a.VerticeVia) > a.LimiarCaminhada(requisicao)))
                            continue;

                        foreach (var aluno in alunosVertice)
                        {
                            aluno.VerticeParada = vertices[i];
                            aluno.DistanciaParada = aluno.DistanciaVia + distanciaCaminhada(aluno.VerticeVia);
                        }

                        mudou = true;
                        vertices.RemoveAt(i + 1);
                        vertices.RemoveAt(i + 1);
                        n = n - 2;
                    }

                }
            }
            var custo2 = CalculaCustoRota(alunos.Select(a => a.VerticeParada).ToArray(), escola.Vertice);
            if (custo1 - custo2 > 1)
            {
                if (log) Log.WriteLine($"Otm. do Tronco Principal 2 gerou ganho de {custo1 - custo2: 0.00} min");
                AtualizaParametros();
                return true;
            }
            else
            {
                alunos = alunosBkp;
                return false;
            }
        }

        private void AtualizaParametros()
        {
            var tempoIdaAcumulado = 0.0;
            var tempoVoltaAcumulado = 0.0;

            var alunosOrdenados = alunos.ToList();
            var vertice = Escolas[0].Vertice;

            var ida = Escolas[0].ChegadaTurno(Turno);
            var volta = Escolas[0].SaidaTurno(Turno);

            for (int i = 1; i < Escolas.Length; i++)
            {
                var auxTmpIda = requisicao.Distancias[Escolas[i].Vertice, vertice, TipoVeiculo];
                var auxTmpVolta = requisicao.Distancias[vertice, Escolas[i].Vertice, TipoVeiculo];

                tempoIdaAcumulado += auxTmpIda;
                tempoVoltaAcumulado += auxTmpVolta;

                if (float.IsInfinity(auxTmpIda) || float.IsInfinity(auxTmpVolta) ||
                    auxTmpIda > requisicao.TempoMaximo || auxTmpVolta > requisicao.TempoMaximo)
                {
                    Custo = float.PositiveInfinity;
                    Tempo = float.PositiveInfinity;
                    return;
                }

                ida = (ida - new TimeSpan(0, (int)auxTmpIda, 0)) | Escolas[i].ChegadaTurno(Turno);
                volta = (volta + new TimeSpan(0, (int)auxTmpVolta, 0)) | Escolas[i].SaidaTurno(Turno);

                if (ida == null || volta == null)
                {
                    Custo = float.PositiveInfinity;
                    Tempo = float.PositiveInfinity;
                    return;
                }

                vertice = Escolas[i].Vertice;
            }

            tempoIda = (ida - new TimeSpan(0, (int)tempoIdaAcumulado, 0)).Inicio;
            tempoVolta = (volta + new TimeSpan(0, (int)tempoVoltaAcumulado, 0)).Inicio;

            foreach (var proximo in alunosOrdenados)
            {
                tempoIdaAcumulado += requisicao.Distancias[proximo.VerticeParada, vertice, TipoVeiculo];
                tempoVoltaAcumulado += requisicao.Distancias[vertice, proximo.VerticeParada, TipoVeiculo];
                vertice = proximo.VerticeParada;
            }

            Tempo = requisicao.ConsideraSentido
                ? (float)Math.Max(tempoIdaAcumulado, tempoVoltaAcumulado)
                : (float)tempoIdaAcumulado;
            var tempoBaseCusto = requisicao.ConsideraSentido
                ? (tempoIdaAcumulado + tempoVoltaAcumulado) / 2
                : tempoIdaAcumulado;
            Custo = (float)CustoTempo(tempoBaseCusto, TipoVeiculo, Monitor, alunos.Count < Capacidade / 2);
        }

        public Viagens ToViagem(short sentido, short turno) // sentido 1 = ida, 2 = volta
        {
            var alunosViagem = alunos.ToList();
            var alunosInseridos = new List<Alunos>(alunosViagem.Count);
            int numViagem = requisicao.NovaViagem;
            var paradas = new List<Paradas>();
            var duracao = 0.0;

            int numParadas = sentido == 1 ? alunosViagem.Count + Escolas.Length : 1;

            var tempo = sentido == 1 ? tempoIda : tempoVolta;
            var parada = Escolas[0].ToParada(requisicao.Requisicao, requisicao.Anobase, numViagem, numParadas, tempo, sentido);
           
            var vertice = parada.Vertice;
            paradas.Add(parada);

            foreach (var escola in Escolas.Skip(1))
            {
                var duracaoTrecho = sentido == 1
                    ? requisicao.Distancias[escola.Vertice, vertice, TipoVeiculo]
                    : requisicao.Distancias[vertice, escola.Vertice, TipoVeiculo];
                if (float.IsInfinity(duracaoTrecho))
                    throw new InvalidOperationException(
                        $"Escola {escola.Inep} inacessível na viagem {numViagem} da requisição {requisicao.Requisicao}");

                duracao += duracaoTrecho;
                numParadas = sentido == 1 ? numParadas - 1 : numParadas + 1;
                tempo = sentido == 1
                    ? tempo.AddMinutes(-duracaoTrecho)
                    : tempo.AddMinutes(duracaoTrecho);
                parada = escola.ToParada(requisicao.Requisicao, requisicao.Anobase, numViagem, numParadas, tempo, sentido);
                vertice = parada.Vertice;
                paradas.Add(parada);

            }

            foreach (var aluno in alunosViagem)
            {
                var duracaoTrecho = sentido == 1
                    ? requisicao.Distancias[aluno.VerticeParada, vertice, TipoVeiculo]
                    : requisicao.Distancias[vertice, aluno.VerticeParada, TipoVeiculo];
                if (float.IsInfinity(duracaoTrecho))
                {
                    Log.WriteLine($"Erro na geração da viagem {numViagem} da requisição {requisicao.Requisicao}: aluno {aluno.Seq} com parada em vértice {aluno.VerticeParada} inacessível a partir do vértice {vertice}", ConsoleColor.Red);
                    continue;
                }

                duracao += duracaoTrecho;
                numParadas = sentido == 1 ? numParadas - 1 : numParadas + 1;
                tempo = sentido == 1
                    ? tempo.AddMinutes(-duracaoTrecho)
                    : tempo.AddMinutes(duracaoTrecho);

                parada = aluno.ToParada(requisicao.Requisicao, requisicao.Anobase, numViagem, numParadas, tempo, sentido);
                vertice = parada.Vertice;
                paradas.Add(parada);
                alunosInseridos.Add(aluno);
            }

            paradas = paradas.OrderBy(p => p.Sequencial).ToList();
            for (var i = 0; i < paradas.Count; i++)
            {
                paradas[i].Sequencial = (short)(i + 1);
                paradas[i].Versao = requisicao.Versao ?? 0;
            }

            vertice = -1;
            int numVertice = 0;
            foreach (var par in paradas)
            {
                if (vertice != par.Vertice)
                {
                    vertice = par.Vertice;
                    numVertice++;
                }

                par.SequencialVertices = (short)numVertice;

            }
            if (Escolas.Length > 1) Console.WriteLine($"Viagem {numViagem} da requisição {requisicao.Requisicao} é multiciclo");
            var viagem = new Viagens()
            {
                Requisicao = requisicao.Requisicao,
                Versao = requisicao.Versao ?? 0,
                Ano = requisicao.Anobase,
                Viagem = (short)numViagem,
                Tipo = 1,
                Turno = turno,
                Sentido = sentido,
                Monitor = alunosInseridos.Any(a => a.TipoEnsino is 1 or 2),
                Motorista = true,
                VeiculoEspecial = alunosInseridos.Any(a => a.VeiculoEspecial),
                PlacaVeiculo = $"TER-{numViagem:0000}",
                TipoVeiculo = (short)TipoVeiculo,
                SequencialVeiculo = 1,
                AlunosAtendidos = (short)alunosInseridos.Count,
                OcupacaoMaxima = (short)alunosInseridos.Count,
                AlunosEstaduais = (short)alunosInseridos.Count(a => a.Administracao == 1),
                AlunosMunicipais = (short)alunosInseridos.Count(a => a.Administracao == 2),
                Duracao = TimeSpan.FromMinutes(duracao),
                Paradas = paradas
            };

            Log.WriteLine($"Viagem {viagem.Viagem} tem {viagem.AlunosAtendidos} alunos e dura {duracao:0.0} minutos");
            return viagem;
        }

        public Rota Mistura(Requisicoes req, short turno, Rota c2)
        {
            Rota c1 = this;
            return new Rota(req, c1.Escolas.Union(c2.Escolas).ToArray(), c1.Alunos.Concat(c2.Alunos).ToArray(), turno);
        }
    }
}
