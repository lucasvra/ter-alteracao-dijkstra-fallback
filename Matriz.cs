using LinqToDB.Data;
using Otimizacao;
using System.Collections.Concurrent;

namespace ter_otimizacao
{
    public class Matriz
    {
        readonly Dictionary<(int, int), float?[]> dados = new();
        private int requisicao { get; set; }
        private bool consideraSentido { get; set; }
        public bool dijkstra { get; internal set; } = false;

        public Matriz(Requisicoes req)
        {
            requisicao = req.Requisicao;
            consideraSentido = req.ConsideraSentido;
            using var db = new TranscolarDB("Transcolar");
            var distancias = db.Distancias.Where(d => d.Requisicao == req.Requisicao);

            foreach (var distancia in distancias)
            {
                var v = (distancia.VerticeOrigem, distancia.VerticeDestino);

                var tempos = new float?[]
                {
                    distancia.Distancia,
                    distancia.TempoVeiculo1,
                    distancia.TempoVeiculo2,
                    distancia.TempoVeiculo3,
                    distancia.TempoVeiculo4
                };

                dados.Add(v, tempos);
            }
        }

        public float this[int v1, int v2, int veiculo]
        {
            get
            {
                if (v1 == v2) return 0;
                if (!consideraSentido && v2 < v1) (v1, v2) = (v2, v1);
                lock (dados)
                {  
                    if (dados.TryGetValue((v1, v2), out var tempos) && tempos[veiculo] is float t) return t;
                    return dijkstra ? DistanciaDijkstra(v1, v2, veiculo) : float.PositiveInfinity;
                }
            }
        }

        public float this[IEnumerable<int> vertices, int veiculo]
        {
            get
            {
                float tempo = 0;
                var v1 = vertices.First();

                foreach (var v2 in vertices.Skip(1))
                {
                    tempo += this[v1, v2, veiculo];
                    v1 = v2;

                    if (float.IsInfinity(tempo)) break;
                }

                return tempo;
            }
        }

        public float TempoCiclo(int inicio, IEnumerable<int> vertices, int veiculo, bool volta = true)
        {
            float tempo = 0;
            var v1 = inicio;

            foreach (var v2 in vertices)
            {
                tempo += this[v1, v2, veiculo];
                v1 = v2;

                if (float.IsInfinity(tempo)) break;
            }


            if (volta) tempo += this[v1, inicio, veiculo];

            return tempo;
        }

        private sealed class ResultadoSequenciaSql
        {
            public int ParIdx { get; set; }
            public int Seq { get; set; }
            public int Vertice { get; set; }
        }

        internal List<int> SequenciaVerticesOtimizada(int[] vertices, int veiculo)
        {
            if (vertices.Length < 2) return vertices.ToList();

            using var db = new TranscolarDB("Transcolar");
            var arrayPostgres = $"{{{string.Join(",", vertices)}}}";
            var sql = $@"
                SELECT par_idx, seq, vertice
                FROM requisicao.TER_VerticesSequencia_Otimizada(
                    '{arrayPostgres}'::int4[],
                    {requisicao},
                    {veiculo},
                    {(consideraSentido ? "true" : "false")}
                )
                ORDER BY par_idx, seq";

            var resultados = db.Query<ResultadoSequenciaSql>(sql).ToList();
            if (resultados.Count == 0)
            {
                Log.WriteLine(
                    $"Aviso: TER_VerticesSequencia_Otimizada não retornou resultados para {vertices.Length} vértices",
                    ConsoleColor.Yellow);
                return SequenciaVertices(vertices, veiculo);
            }

            var sequencia = new List<int>();
            foreach (var grupo in resultados.GroupBy(r => r.ParIdx).OrderBy(g => g.Key))
            {
                var verticesPar = grupo.OrderBy(r => r.Seq).Select(r => r.Vertice);
                sequencia.AddRange(sequencia.Count == 0 ? verticesPar : verticesPar.Skip(1));
            }

            return sequencia;
        }

        internal List<int> SequenciaVertices(int[] v, int veiculo)
        {
            var vertices = new List<int>() { v[0] };

            for (var i = 0; i < v.Length - 1; i++) vertices.AddRange(PontosVertices(v[i], v[i + 1], veiculo).Skip(1));

            return vertices;
        }

        public int[] PontosVertices(int v1, int v2, int veiculo = 0)
        {
            lock (dados)
            {
                using var db = new TranscolarDB("Transcolar");
                return db.Query<int>($"SELECT requisicao.TER_VerticesPontos({v1}, {v2}, {requisicao}, {veiculo}, {consideraSentido});").ToArray();
            }
        }

        private readonly ConcurrentDictionary<(int v1, int v2, int veiculo), float> _cache = new();

        internal float DistanciaDijkstra(int v1, int v2, int veiculo)
        {
            // Verificar se já existe no cache
            if (_cache.TryGetValue((v1, v2, veiculo), out var distancia))
            {
                return distancia; // Retornar o valor já calculado
            }

            lock (dados) // Sincronizar acesso aos dados
            {
                using var db = new TranscolarDB("Transcolar");
                distancia = db.Execute<float?>($"SELECT requisicao.TER_DistanciaPontosDijkstra({v1}, {v2}, {requisicao}, {veiculo}, {consideraSentido});") ?? float.PositiveInfinity;
            }

            // Adicionar ao cache
            _cache.TryAdd((v1, v2, veiculo), distancia);
            return distancia;
        }
    }
}
