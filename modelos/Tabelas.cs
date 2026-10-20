using LinqToDB;
using LinqToDB.Configuration;
using LinqToDB.Data;
using LinqToDB.Mapping;
using ter_otimizacao;

#pragma warning disable 1573, 1591
#nullable enable

namespace Otimizacao
{

    public partial class TranscolarDB : DataConnection
    {
        public ITable<Alunos> Alunos => this.GetTable<Alunos>();
        public ITable<Distancias> Distancias => this.GetTable<Distancias>();
        public ITable<Escolas> Escolas => this.GetTable<Escolas>();
        public ITable<Paradas> Paradas => this.GetTable<Paradas>();
        public ITable<Requisicoes> Requisicoes => this.GetTable<Requisicoes>();
        public ITable<Turnos> Turnos => this.GetTable<Turnos>();
        public ITable<Viagens> Viagens => this.GetTable<Viagens>();
        public ITable<Veiculos> Veiculos => this.GetTable<Veiculos>();
        public ITable<TiposVeiculo> TiposVeiculo => this.GetTable<TiposVeiculo>();
    }

    public class ConnectionStringSettings : IConnectionStringSettings
    {
        public string ConnectionString { get; set; }
        public string Name { get; set; }
        public string ProviderName { get; set; }
        public bool IsGlobal => false;
    }

    public class TranscolarConfig : ILinqToDBSettings
    {
        public IEnumerable<IDataProviderSettings> DataProviders => Enumerable.Empty<IDataProviderSettings>();

        public string DefaultConfiguration => "Transcolar";
        public string DefaultDataProvider => "PostgreSQL";

        public IEnumerable<IConnectionStringSettings> ConnectionStrings
        {
            get
            {
                var host = Environment.GetEnvironmentVariable("DB_HOST") ?? "109.199.124.40";
                var port = Environment.GetEnvironmentVariable("DB_PORT") ?? "30990";
                var database = Environment.GetEnvironmentVariable("DB_DATABASE") ?? "transcolar";
                var username = Environment.GetEnvironmentVariable("DB_USERNAME") ?? "postgres";
                var password = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "[REDACTED]";
                yield return
                    new ConnectionStringSettings
                    {
                        Name = "Transcolar",
                        ProviderName = "PostgreSQL",
                        ConnectionString = @$"Server={host};Port={port};Database={database};User Id={username};Password={password};Command Timeout=0;Timeout=0;Keepalive=10;Tcp Keepalive=true;Tcp Keepalive Time=30;Tcp Keepalive Interval=10;Read Buffer Size=81920;Max Auto Prepare=10;Application Name=ter-alteracao;Include Error Detail=true"
                    };
            }
        }
    }

    [Table("tipo_veiculo_req", Schema = "requisicao")]
    public class TiposVeiculo
    {
        [Column("requisicao"), PrimaryKey, NotNull]
        public int Requisicao { get; set; }

        [Column("tipo_veiculo"), PrimaryKey, NotNull]
        public short Tipo { get; set; }

        [Column("capacidade_veiculo")]
        public short Capacidade { get; set; }

        [Column("capacidade_veiculo_necessidade_especial")]
        public short Cadeirantes { get; set; }

        [Column("ano_fabricacao")]
        public short AnoFabricacao { get; set; }
    }

    [Table("alunos_req", Schema = "requisicao")]
    public class Alunos
    {
        [Column("requisicao"), PrimaryKey, NotNull]
        public int Requisicao { get; set; }

        [Column("versao"), PrimaryKey, NotNull]
        public short Versao { get; set; }

        [Column("seq"), PrimaryKey, NotNull]
        public int Seq { get; set; }

        [Column("nome")]
        public string Nome { get; set; }

        [Column("administracao"), NotNull]
        public short Administracao { get; set; }

        [Column("escola")]
        public int? Escola { get; set; }

        [Column("inep")]
        public long? Inep { get; set; }

        [Column("registro_aluno")]
        public long? RegistroAluno { get; set; }

        [Column("codigo_matricula")]
        public long? CodigoMatricula { get; set; }

        [Column("mobilidade_reduzida")]
        public bool MobilidadeReduzida { get; set; }

        [Column("veiculo_especial")]
        public bool VeiculoEspecial { get; set; }

        [Column("possui_deficiencia")]
        public bool PossuiDeficiencia { get; set; }

        [Column("frota_propria")]
        public bool FrotaPropria { get; set; }

        [Column("forca_otm")]
        public bool ForcaOtm { get; set; }

        [Column("motivo_forca_otm")]
        public string MotivoForcaOtm { get; set; }

        [Column("turno")]
        public int Turno { get; set; }

        [Column("tipo_ensino")]
        public short TipoEnsino { get; set; }

        [Column("modo_transporte")]
        public short? ModoTransporte { get; set; }

        [Column("vertice_via")]
        public int VerticeVia { get; set; }

        [Column("vertice_parada")]
        public int VerticeParada { get; set; }

        [Column("vertice_escola")]
        public int VerticeEscola { get; set; }

        [Column("distancia_via")]
        public float DistanciaVia { get; set; }

        [Column("distancia_parada")]
        public float DistanciaParada { get; set; }

        [Column("sentido"), PrimaryKey, NotNull]
        public short Sentido { get; set; }

        [Column("distancia_escola")]
        public float DistanciaEscola { get; set; }

        [Column("tipo_outlier")]
        public short TipoOutlier { get; set; }

        public Turnos Horario { get; set; }

        /// <summary>0008: código do turno (Turnos.Turno: 1 manhã, 2 tarde, 3 noite, ...); null sem Horario.</summary>
        public short? TurnoCodigo => Horario?.Turno;

        public string TurnoIda => Horario.HrInicio.Hour switch
        {
            >= 0 and < 12 => "M",
            >= 12 and < 18 => "T",
            _ => "N"
        };

        public string TurnoVolta => Horario.HrFim.Hour switch
        {
            >= 4 and < 14 => "M",
            >= 14 and < 20 => "T",
            _ => "N"
        };

        public Alunos Clone() => (Alunos)MemberwiseClone();

        public double LimiarCaminhada(Requisicoes requisicao)
        {
            double limiar = 0;

            if (VeiculoEspecial || MobilidadeReduzida || ForcaOtm) return 0;

            switch (TipoEnsino)
            {
                case 1: // Pré-escola
                    limiar = requisicao.CaminhadaMaximaPre;
                    break;
                case 6: // EJA
                case 4: // Ensino Médio
                    limiar = requisicao.CaminhadaMaximaEm;
                    break;
                case 2: //Ensino Fundamental 1
                case 3: //Ensino Fundamental 1
                    limiar = (Administracao == 1) ? requisicao.CaminhadaMaximaFundamentalEst : requisicao.CaminhadaMaximaFundamentalMun;
                    break;
                case 5: // Educação Especial
                    limiar = requisicao.CaminhadaMaximaEspecial;
                    break;
                default:
                    Log.WriteLine($"Tipo de aluno {TipoEnsino} é inválido");
                    limiar = 0;
                    break;
            }

            // 0008: Turno é o id de turnos_req; o noturno é o CÓDIGO Turnos.Turno == 3 (via Horario).
            // Antes comparava o id (ex.: 132058) com 3 e o limite noturno nunca era aplicado.
            if (TurnoCodigo == 3 && requisicao.CaminhadaMaximaNoturno < limiar)
                return requisicao.CaminhadaMaximaNoturno ?? 0;

            return limiar;
        }

        public Paradas ToParada(int requisicao, int ano, int viagem, int sequecial, DateTime tempo, short sentido)
        {
            return new Paradas()
            {
                Requisicao = requisicao,
                Viagem = viagem,
                Sequencial = (short)sequecial,
                Tempo = tempo.TimeOfDay,
                Tipo = 1, // 1 - aluno, 2 - escola
                Carregamento = (short)(sentido == 1 ? 1 : 2), //1 - embarque, 2 - desembarque
                Vertice = VerticeParada,
                Codigo = Seq,
                Distancia = DistanciaParada,
                Nome = Nome // Adicionar nome na tabela
            };
        }


    }

    [Table("distancias_req", Schema = "requisicao")]
    public class Distancias
    {
        [Column("requisicao"), PrimaryKey, NotNull]
        public int Requisicao { get; set; }

        [Column("vertice_origem"), PrimaryKey, NotNull]
        public int VerticeOrigem { get; set; }

        [Column("vertice_destino"), PrimaryKey, NotNull]
        public int VerticeDestino { get; set; }

        [Column("distancia")]
        public float? Distancia { get; set; }

        [Column("tempo_veiculo1")]
        public float? TempoVeiculo1 { get; set; }

        [Column("tempo_veiculo2")]
        public float? TempoVeiculo2 { get; set; }

        [Column("tempo_veiculo3")]
        public float? TempoVeiculo3 { get; set; }

        [Column("tempo_veiculo4")]
        public float? TempoVeiculo4 { get; set; }
    }

    [Table("escolas_req", Schema = "requisicao")]
    public class Escolas
    {
        [Column("requisicao"), PrimaryKey, NotNull]
        public int Requisicao { get; set; }

        [Column("versao"), PrimaryKey, NotNull]
        public short Versao { get; set; }

        [Column("seq"), PrimaryKey, NotNull]
        public int Seq { get; set; }

        [Column("municipio"), NotNull]
        public int Municipio { get; set; }

        [Column("nome"), NotNull]
        public string Nome { get; set; }

        [Column("inep")]
        public long? Inep { get; set; }

        [Column("administracao")]
        public short? Administracao { get; set; }

        [Column("vertice")]
        public int Vertice { get; set; }

        public List<Turnos> turnos { get; set; }

        public IntervaloTempo ChegadaTurno(short turno)
        {
            var horarios = turnos.First(t => t.Turno == turno);
            return new IntervaloTempo(horarios.HrInicio - new TimeSpan(0, horarios.ToleranciaChegada, 0), horarios.HrInicio);
        }

        public IntervaloTempo SaidaTurno(short turno)
        {
            var horarios = turnos.First(t => t.Turno == turno);
            return new IntervaloTempo(horarios.HrFim, horarios.HrFim + new TimeSpan(0, horarios.ToleranciaChegada, 0));
        }

        public Paradas ToParada(int requisicao, int ano, int viagem, int sequecial, DateTime tempo, short sentido)
        {
            return new Paradas()
            {
                Requisicao = requisicao,
                Viagem = viagem,
                Sequencial = (short)sequecial,
                Tempo = tempo.TimeOfDay,
                Tipo = 2, // 1 - aluno, 2 - escola
                Carregamento = (short)(sentido == 1 ? 2 : 1), //1 - embarque, 2 - desembarque
                Vertice = Vertice,
                Codigo = Seq,
                Nome = Nome
            };
        }
    }

    [Table("paradas_saida", Schema = "requisicao")]
    public class Paradas
    {
        [Column("requisicao"), NotNull]
        public int Requisicao { get; set; }

        [Column("versao"), NotNull]
        public short Versao { get; set; }

        [Column("viagem"), NotNull]
        public int Viagem { get; set; }

        [Column("vertice")]
        public int Vertice { get; set; }

        [Column("sequencial"), NotNull]
        public short Sequencial { get; set; }

        [Column("sequencial_vertices")]
        public short? SequencialVertices { get; set; }

        [Column("tempo")]
        public TimeSpan Tempo { get; set; }

        [Column("distancia")]
        public float? Distancia { get; set; }

        [Column("tipo")]
        public short? Tipo { get; set; }

        [Column("carregamento")]
        public short? Carregamento { get; set; }

        [Column("codigo"), NotNull]
        public int Codigo { get; set; }

        [Column("nome")]
        public string Nome { get; set; }
    }

    [Table("turnos_req", Schema = "requisicao")]
    public class Turnos
    {
        [Column("requisicao"), PrimaryKey, NotNull]
        public int Requisicao { get; set; }

        [Column("id"), NotNull]
        public int Id { get; set; }

        [Column("escola"), PrimaryKey, NotNull]
        public int Escola { get; set; }

        [Column("turno"), PrimaryKey, NotNull]
        public short Turno { get; set; }

        [Column("hr_inicio"), NotNull]
        public DateTime HrInicio { get; set; }

        [Column("hr_fim"), NotNull]
        public DateTime HrFim { get; set; }

        [Column("tolerancia_chegada"), NotNull]
        public short ToleranciaChegada { get; set; }

        [Column("descricao"), NotNull]
        public string Descricao { get; set; }
    }

    [Table("viagem_saida", Schema = "requisicao")]
    public class Viagens
    {
        [Column("requisicao"), NotNull]
        public int Requisicao { get; set; }

        [Column("versao"), NotNull]
        public short Versao { get; set; }

        [Column("ano")]
        public short Ano { get; set; }

        [Column("viagem")]
        public short Viagem { get; set; }

        [Column("tipo")]
        public short Tipo { get; set; }

        [Column("turno")]
        public short Turno { get; set; }

        [Column("sentido")]
        public short Sentido { get; set; }

        [Column("monitor")]
        public bool Monitor { get; set; }

        [Column("motorista")]
        public bool Motorista { get; set; }

        [Column("veiculo_especial")]
        public bool VeiculoEspecial { get; set; }

        [Column("placa_veiculo")]
        public string PlacaVeiculo { get; set; }

        [Column("tipo_veiculo")]
        public short TipoVeiculo { get; set; }

        [Column("sequencial_veiculo")]
        public short SequencialVeiculo { get; set; }

        [Column("ano_fabricacao_veiculo")]
        public short AnoFabricacaoVeiculo { get; set; }

        [Column("capacidade_veiculo")]
        public short CapacidadeVeiculo { get; set; }

        [Column("propriedade_veiculo")]
        public short PropriedadeVeiculoOtm { get; set; }

        [Column("alunos_atendidos")]
        public short AlunosAtendidos { get; set; }

        [Column("alunos_estaduais")]
        public short AlunosEstaduais { get; set; }

        [Column("alunos_municipais")]
        public short AlunosMunicipais { get; set; }

        [Column("ocupacao_maxima")]
        public short OcupacaoMaxima { get; set; }

        [Column("distancia")]
        public float Distancia { get; set; }

        [Column("duracao")]
        public TimeSpan? Duracao { get; set; }

        [Column("roteiro")]
        public string Roteiro { get; set; }

        [Column("pavimento_poliedro")]
        public float? PavimentoPoliedro { get; set; }

        [Column("pavimento_asfalto")]
        public float? PavimentoAsfalto { get; set; }

        [Column("pavimento_terra")]
        public float? PavimentoTerra { get; set; }

        [Column("fluvial"), NotNull]
        public bool Fluvial { get; set; }

        public List<Paradas> Paradas { get; set; }

        public IntervaloTempo IntervaloViagem => new(Paradas.Min(p => p.Tempo), Paradas.Max(p => p.Tempo));

        public bool TrafegaViagem(int veiculo, Matriz distancias)
        {
            // Checa trechos consecutivos na ordem atual da lista (caminho após Insert/Mover).
            // Não usa OrderBy(Sequencial): InserirAluno/MoverAluno criam paradas com Sequencial=0
            // e OrderBy quebraria a sequência. Indexador Matriz[IEnumerable,veiculo] soma os pares.
            return !float.IsInfinity(distancias[Paradas.Select(p => p.Vertice), veiculo]);
        }

        /// <summary>
        /// Renumera Sequencial e SequencialVertices na ORDEM ATUAL da lista (= ordem do caminho
        /// validada por TrafegaViagem). Paradas criadas por InserirAluno/MoverAluno nascem com
        /// Sequencial=0; sem renumerar, qualquer OrderBy(Sequencial) as levaria para o início.
        /// </summary>
        public void RenumeraParadas()
        {
            short sequencialVertice = 0;
            for (var i = 0; i < Paradas.Count; i++)
            {
                if (i == 0 || Paradas[i - 1].Vertice != Paradas[i].Vertice)
                    sequencialVertice++;

                Paradas[i].Sequencial = (short)(i + 1);
                Paradas[i].SequencialVertices = sequencialVertice;
            }
        }

        public void AtualizaPosProcessamento(int veiculo, Matriz distancias)
        {
            if (veiculo == TipoVeiculo) return;

            var paradas = Paradas.OrderBy(p => p.Sequencial).ToList();
            var vertice = paradas.First().Vertice;
            var tempo = paradas.First().Tempo;

            foreach (var parada in paradas.Skip(1))
            {
                tempo = tempo + TimeSpan.FromMinutes(distancias[vertice, parada.Vertice, veiculo]);
                parada.Tempo = tempo;
                vertice = parada.Vertice;
            }
        }

        public void Insert()
        {
            Log.WriteLine($"\tSalvando viagem {Viagem:000} com {AlunosAtendidos} alunos de {CapacidadeVeiculo}", AlunosAtendidos < 2 * CapacidadeVeiculo / 3 ? ConsoleColor.Red : ConsoleColor.Green);
            using var db = new TranscolarDB("Transcolar");
            db.Insert(this);
            foreach (var parada in Paradas) db.Insert(parada);
        }
    }

    [Table("requisicoes", Schema = "requisicao")]
    public class Requisicoes
    {
        [Column("requisicao", IsPrimaryKey = true, IsIdentity = true, SkipOnInsert = true, SkipOnUpdate = true)] public int Requisicao { get; set; } // integer
        [Column("situacao")] public short Situacao { get; set; } // smallint
        [Column("anobase")] public short Anobase { get; set; } // smallint
        [Column("municipio")] public int Municipio { get; set; } // integer
        [Column("tempo_maximo")] public short TempoMaximo { get; set; } // smallint
        [Column("distancia_escola_municipal")] public double DistanciaEscolaMunicipal { get; set; } // double precision
        [Column("distancia_escola_estadual")] public double DistanciaEscolaEstadual { get; set; } // double precision
        [Column("caminhada_maxima_fundamental_est")] public double CaminhadaMaximaFundamentalEst { get; set; } // double precision
        [Column("caminhada_maxima_fundamental_mun")] public double CaminhadaMaximaFundamentalMun { get; set; } // double precision
        [Column("caminhada_maxima_pre")] public double CaminhadaMaximaPre { get; set; } // double precision
        [Column("caminhada_maxima_especial")] public double CaminhadaMaximaEspecial { get; set; } // double precision
        [Column("caminhada_maxima_em")] public double CaminhadaMaximaEm { get; set; } // double precision
        [Column("velocidade_media_calculo")] public double VelocidadeMediaCalculo { get; set; } // double precision
        [Column("caminhada_maxima_noturno")] public double? CaminhadaMaximaNoturno { get; set; } // double precision
        [Column("dt_atualiza")] public DateTime? DtAtualiza { get; set; } // date
        [Column("descricao_error")] public string? DescricaoError { get; set; } // text
        [Column("login_solicitante")] public string? LoginSolicitante { get; set; } // text
        [Column("tipo_aluno")] public short? TipoAluno { get; set; } // smallint
        [Column("tempo_chegada_aluno_antes_aula")] public int? TempoChegadaAlunoAntesAula { get; set; } // integer
        [Column("tipo_otimizacao")] public short? TipoOtimizacao { get; set; } // smallint
        [Column("requisicao_ativa")] public bool? RequisicaoAtiva { get; set; } // boolean
        [Column("dt_requisicao_ativa")] public DateTime? DtRequisicaoAtiva { get; set; } // date
        [Column("versao")] public short? Versao { get; set; } // smallint
        [Column("excluir_zona_urbana")] public bool? ExcluirZonaUrbana { get; set; } // boolean
        [Column("tipo_ambiente")] public short? TipoAmbiente { get; set; } // smallint
        [Column("prioridade")] public short? Prioridade { get; set; } // smallint

        [Column("considera_sentido")] public bool ConsideraSentido { get; set; } // boolean

        public Matriz Distancias { get; internal set; }

        public IEnumerable<TiposVeiculo> TiposVeiculo { get; internal set; }

        public (short tipo, short capacidade) TipoVeiculo(int numPassageiros, bool monitor, int numCadeirantes = 0)
        {
            if (monitor) numPassageiros++;
            return TiposVeiculo.Where(v => v.Capacidade >= numPassageiros && v.Cadeirantes >= numCadeirantes)
                               .OrderBy(v => v.Capacidade)
                               .Select(v => (v.Tipo, v.Capacidade))
                               .FirstOrDefault(((short)0, (short)0));
        }

        public (short capacidade, short ano) DadosVeiculo(int tipo)
        {
            return TiposVeiculo.Where(v => v.Tipo == tipo)
                               .Select(v => (v.Capacidade, v.AnoFabricacao))
                               .FirstOrDefault(((short)0, (short)0));
        }

        /// <summary>Alunos cuja adaptação in loco foi rejeitada (vale para a viagem pareada do outro sentido).</summary>
        public HashSet<int> AdaptacoesRejeitadas { get; } = new();

        int numViagem = 1;
        public int NovaViagem
        {
            get
            {
                int aux;
                lock (this) aux = numViagem++;
                return aux;
            }
            set
            {
                lock (this) numViagem = value;
            }
        }

        int numVeiculo = 1;
        public string NovoVeiculo
        {
            get
            {
                string aux;
                lock (this) aux = $"TER-{numVeiculo++:0000}";
                return aux;
            }
        }
    }

    [Table("veiculos_req", Schema = "requisicao")]
    public class Veiculos
    {
        [Column("requisicao"), PrimaryKey, NotNull]
        public int Requisicao { get; set; }

        [Column("seq"), PrimaryKey, NotNull]
        public int Seq { get; set; }

        [Column("municipio")]
        public int Municipio { get; set; }

        [Column("placa")]
        public string Placa { get; set; }

        [Column("proprietario")]
        public string Proprietario { get; set; }

        [Column("cpf_cnpj_proprietario")]
        public string CpfCnpjProprietario { get; set; }

        [Column("administracao")]
        public short Administracao { get; set; }

        [Column("tipo_veiculo")]
        public short TipoVeiculo { get; set; }

        [Column("numero_passageiros")]
        public short Capacidade { get; set; }

        [Column("ano_fabricacao")]
        public short AnoFabricacao { get; set; }

        [Column("tipo_contrato")]
        public short TipoContrato { get; set; }

        [Column("tipo_combustivel")]
        public short TipoCombustivel { get; set; }

        public bool VeiculoEspecial { get; internal set; }

        public List<(IntervaloTempo intervalo, int verticeIni, int verticeFim, Viagens viagem)> Intervalos { get; set; }

        public int NumViagem { get; internal set; }

        public static short TipoVeiculoPelaCapacidade(int capacidade, Requisicoes req)
        {
            return req.TiposVeiculo.Where(v => v.Capacidade >= capacidade)
                                   .OrderBy(v => v.Capacidade)
                                   .Select(v => v.Tipo)
                                   .FirstOrDefault((short)0);
        }

        public bool CriaAproveitamento(Requisicoes req)
        {
            if (Intervalos == null || Intervalos.Count == 0) return false;

            Intervalos.Sort((a, b) => a.intervalo.Inicio.CompareTo(b.intervalo.Inicio));
            (IntervaloTempo intervalo, int verticeIni, int verticeFim, Viagens viagem) v1, v2;
            int numViagem;
            for (int i = 0; i < Intervalos.Count - 1; i++)
            {
                v1 = Intervalos[i];
                v2 = Intervalos[i + 1];

                if (v1.verticeFim == v2.verticeIni) continue;

                numViagem = req.NovaViagem;

                Log.WriteLine($"\tCriando Viagem {numViagem} AP trecho ({v1.verticeFim}, {v2.verticeIni}, {req.Distancias[v1.verticeFim, v2.verticeIni, TipoVeiculo]} min)");
                if (float.IsInfinity(req.Distancias[v1.verticeFim, v2.verticeIni, TipoVeiculo]))
                {
                    Log.WriteLine($"\t\tTrecho invalido para o tipo de veiculo {TipoVeiculo} com capacidade {Capacidade}", ConsoleColor.Yellow);
                    continue;
                }

                var aproveitamento = new Viagens()
                {
                    Requisicao = req.Requisicao,
                    OcupacaoMaxima = 0,
                    AlunosAtendidos = 0,
                    CapacidadeVeiculo = Capacidade,
                    Viagem = (short)numViagem,
                    PlacaVeiculo = Placa,
                    TipoVeiculo = TipoVeiculo,
                    AnoFabricacaoVeiculo = AnoFabricacao,
                    SequencialVeiculo = (short)(v1.viagem.SequencialVeiculo + 1),
                    Tipo = 2,
                    AlunosEstaduais = 0,
                    AlunosMunicipais = 0,
                    Ano = req.Anobase,
                    Versao = req.Versao ?? 0,
                    Fluvial = false,
                    VeiculoEspecial = false,
                    Duracao = v2.intervalo.Inicio - v1.intervalo.Fim,
                    Paradas = new[]
                    {
                            new Paradas()
                            {
                                Requisicao = req.Requisicao,
                                Versao = req.Versao ?? 0,
                                Viagem = numViagem,
                                Sequencial = (short)1,
                                Tipo = 0,
                                Vertice = v1.verticeFim,
                                Tempo = v1.intervalo.Fim.TimeOfDay,
                                SequencialVertices = (short)1,
                            },
                            new Paradas()
                            {
                                Requisicao = req.Requisicao,
                                Versao = req.Versao ?? 0,
                                Viagem = numViagem,
                                Sequencial = (short)2,
                                Tipo = 0,
                                Vertice = v2.verticeIni,
                                Tempo = v2.intervalo.Inicio.TimeOfDay,
                                SequencialVertices = (short)2,
                            },
                        }.ToList(),
                };

                using var db1 = new TranscolarDB("Transcolar");
                db1.Insert(aproveitamento);
                foreach (var parada in aproveitamento.Paradas) db1.Insert(parada);
            }

            v1 = Intervalos[^1];
            v2 = Intervalos[0];

            if (v1.verticeFim == v2.verticeIni) return false;

            numViagem = req.NovaViagem;
            Log.WriteLine($"\tCriando Viagem {numViagem} AR trecho ({v1.verticeFim}, {v2.verticeIni}, {req.Distancias[v1.verticeFim, v2.verticeIni, TipoVeiculo]} min)");
            if (float.IsInfinity(req.Distancias[v1.verticeFim, v2.verticeIni, TipoVeiculo]))
            {
                Log.WriteLine($"\t\tTrecho invalido para o tipo de veiculo {TipoVeiculo} com capacidade {Capacidade}", ConsoleColor.Yellow);
                return false;
            }

            var retorno = new Viagens()
            {
                Requisicao = req.Requisicao,
                OcupacaoMaxima = 0,
                AlunosAtendidos = 0,
                CapacidadeVeiculo = Capacidade,
                AnoFabricacaoVeiculo = AnoFabricacao,
                Viagem = (short)numViagem,
                PlacaVeiculo = Placa,
                TipoVeiculo = TipoVeiculo,
                SequencialVeiculo = (short)(v1.viagem.SequencialVeiculo + 1),
                Tipo = 3, // 1 - tr, 2 - ap, 3 - ar
                Duracao = TimeSpan.FromMinutes(req.Distancias[v1.verticeFim, v2.verticeIni, TipoVeiculo]),
                AlunosEstaduais = 0,
                AlunosMunicipais = 0,
                Ano = req.Anobase,
                Versao = req.Versao ?? 0,
                Fluvial = false,
                VeiculoEspecial = false,
                Paradas = new[]
                    {
                            new Paradas()
                            {
                                Requisicao = req.Requisicao,
                                Versao = req.Versao ?? 0,
                                Viagem = numViagem,
                                Sequencial = (short)1,
                                Tipo = 0,
                                Vertice = v1.verticeFim,
                                Tempo = (v1.intervalo.Fim).TimeOfDay,
                                SequencialVertices = (short)1,
                            },
                            new Paradas()
                            {
                                Requisicao = req.Requisicao,
                                Versao = req.Versao ?? 0,
                                Viagem = numViagem,
                                Sequencial = (short)2,
                                Tipo = 0,
                                Vertice = v2.verticeIni,
                                Tempo = v1.intervalo.Fim.AddMinutes(req.Distancias[v1.verticeFim,v2.verticeIni,TipoVeiculo]).TimeOfDay,
                                SequencialVertices = (short)2,
                            },
                        }.ToList(),
            };

            using var db2 = new TranscolarDB("Transcolar");
            db2.Insert(retorno);
            foreach (var parada in retorno.Paradas) db2.Insert(parada);
            return true;
        }
    }
}
