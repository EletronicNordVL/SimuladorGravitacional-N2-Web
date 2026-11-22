using MySql.Data.MySqlClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;
using TrabalhoN2;

namespace SimuladorGravitacional_N2;

/* Essa classe é responsável por gravar o estado do universo em um banco de dados MySQL. */
public class GravadorUniversoBD : IDisposable
{
    /* ID da simulação atual, usado para associar os corpos e seus estados à simulação correta no banco de dados. */
    private int idSimulacaoAtual;
    /* Mapa que associa cada objeto Corpo ao seu ID correspondente no banco de dados. 
     * Isso é necessário para gravar corretamente o estado de cada corpo em cada passo da simulação. */
    private Dictionary<Corpo, int> mapaIdCorpos =
        new Dictionary<Corpo, int>();

    /* Grava várias linhas de uma vez, em um único INSERT, para reduzir
     * overhead de comunicação com o banco. */
private const int LinhasPorBloco = 500;

    /* Conexão aberta para gravar os blocos de linhas. É aberta na primeira
     * vez que o DescarregarBuffer é chamado, e fechada no FinalizarSimulacao. */
    private MySqlConnection? conexaoGravacao;

    /* Buffer de linhas a gravar. Cada linha é uma "foto" do estado de um corpo
     * em um passo da simulação. */
    private readonly List<LinhaEstado> bufferLinhas = new List<LinhaEstado>();

    /* Indica se houve algum erro na gravação. 
     * Se houver, o FinalizarSimulacao não tenta gravar o que sobrou no buffer, 
     * porque provavelmente vai falhar de novo. */
    private volatile bool erroNaGravacao;

    /* Exceção que causou o erro na gravação, para ser mostrada no FinalizarSimulacao. */
    private Exception? excecaoGravacao;

    /* Blocos de linhas que já saíram do buffer e estão esperando para ir para o banco.
     * Quem grava é uma thread separada, assim a simulação não fica parada esperando o MySQL. */
    private BlockingCollection<List<LinhaEstado>> blocosEsperando = new BlockingCollection<List<LinhaEstado>>();

    /* Thread (Task) que grava os blocos no banco. É criada no primeiro DescarregarBuffer. */
    private Task? threadGravacao;

    /* Máximo de blocos esperando. Se o banco ficar para trás, a simulação para e espera. */
    private const int MaximoBlocosEsperando = 20;

    /* true quando o banco não está dando conta: a simulação deve esperar antes do próximo passo. */
    public bool BancoAtrasado => blocosEsperando.Count >= MaximoBlocosEsperando;

    /* true se a thread de gravação deu erro (a simulação pode continuar sem gravar). */
    public bool HouveErroGravacao => erroNaGravacao;

    /* Cronômetro para medir o tempo total gasto na gravação dos passos da simulação. */
    private readonly Stopwatch cronometroGravacao = new Stopwatch();

    /* Propriedade para obter o tempo total gasto na gravação dos passos da simulação. */
    public TimeSpan TempoTotalGravacao => cronometroGravacao.Elapsed;

    /* Estrutura para armazenar o estado de um corpo em um passo da simulação. */
    private readonly struct LinhaEstado
    {
        /* Campos públicos para armazenar as informações do estado do corpo em um passo da simulação. */
        public readonly int CorpoId;

        /* Campos públicos para armazenar as informações do estado do corpo em um passo da simulação. */
        public readonly int NumPasso;

        /* Campos públicos para armazenar as informações do estado do corpo em um passo da simulação. */
        public readonly double TempoDecorrido, PosX, PosY, VelX, VelY;

        /* Campos públicos para armazenar as informações do estado do corpo em um passo da simulação. */
        public readonly double ForcaX, ForcaY, AceleracaoX, AceleracaoY;

        /* Nome, massa e densidade também são gravados em cada passo, porque mudam quando dois corpos se fundem. */
        public readonly string Nome;
        public readonly double Massa, Densidade;

        /* Construtor da estrutura LinhaEstado, que inicializa os campos com os valores fornecidos. */
        public LinhaEstado(int corpoId, int numPasso, double tempoDecorrido, Corpo c)
        {
            CorpoId = corpoId;
            NumPasso = numPasso;
            TempoDecorrido = tempoDecorrido;
            Nome = c.Nome;
            Massa = c.Massa;
            Densidade = c.Densidade;
            PosX = c.PosX; PosY = c.PosY;
            VelX = c.VelX; VelY = c.VelY;
            ForcaX = c.ForcaX; ForcaY = c.ForcaY;
            AceleracaoX = c.AceleracaoX; AceleracaoY = c.AceleracaoY;
        }
    }

    /* Inicia uma nova simulação no banco de dados, criando registros para a simulação e seus corpos.
     * Retorna o ID da simulação criada. */
    public int IniciarSimulacao(
        string nomeSimulacao,
        List<Corpo> corpos,
        int qtdIteracoes,
        double tempoEntreIteracoes)
    {
        mapaIdCorpos.Clear();
        bufferLinhas.Clear();
        erroNaGravacao = false;
        excecaoGravacao = null;
        blocosEsperando = new BlockingCollection<List<LinhaEstado>>();
        threadGravacao = null;
        cronometroGravacao.Reset();

        /* Abre uma conexão com o banco de dados e inicia uma 
         * transação para garantir que todas as operações sejam atômicas. */
        using (MySqlConnection conexao = ConexaoBD.ObterConexao())
        {
            using (MySqlTransaction transacao = conexao.BeginTransaction())
            {
                try
                {
                    /* Insere um registro na tabela Simulacao com as informações fornecidas, 
                     * incluindo o nome da simulação, quantidade de corpos, quantidade de iterações 
                     * e tempo entre iterações. */
                    string sqlSimulacao = @"INSERT INTO Simulacao (nome, data_criacao, qtd_corpos, qtd_iteracoes, tempo_entre_iteracoes)
                    VALUES (@nome, NOW(), @qtdCorpos, @qtdIteracoes, @tempoEntreIteracoes);";

                    using (MySqlCommand cmd = new MySqlCommand(sqlSimulacao, conexao, transacao))
                    {
                        /* Adiciona os parâmetros necessários para a inserção da simulação no banco de dados, 
                         * incluindo o nome da simulação, quantidade de corpos, quantidade de iterações 
                         * e tempo entre iterações. */
                        cmd.Parameters.AddWithValue("@nome", nomeSimulacao);
                        cmd.Parameters.AddWithValue("@qtdCorpos", corpos.Count);
                        cmd.Parameters.AddWithValue("@qtdIteracoes", qtdIteracoes);
                        cmd.Parameters.AddWithValue("@tempoEntreIteracoes", tempoEntreIteracoes);
                        cmd.ExecuteNonQuery();

                        this.idSimulacaoAtual = (int)cmd.LastInsertedId;
                    }

                    /* Cria registros para cada corpo na tabela Corpo, associando-os à simulação atual. 
                     * O ID gerado para cada corpo é armazenado no mapa de corpos para referência futura. */
                    string sqlCorpo = @"INSERT INTO Corpo (id_simulacao, nome, massa, densidade) 
                    VALUES (@idSimulacao, @nome, @massa, @densidade);";

                    foreach (var corpo in corpos)
                    {
                        /* Para cada corpo, cria um registro no banco de dados e armazena o ID 
                         * gerado no mapa de corpos. */
                        using (MySqlCommand cmdCorpo = new MySqlCommand(sqlCorpo, conexao, transacao))
                        {
                            /* Adiciona os parâmetros necessários para a inserção do corpo no banco de dados, 
                             * incluindo o ID da simulação atual, nome do corpo, massa e densidade. */
                            cmdCorpo.Parameters.AddWithValue("@idSimulacao", this.idSimulacaoAtual);
                            cmdCorpo.Parameters.AddWithValue("@nome", corpo.Nome);
                            cmdCorpo.Parameters.AddWithValue("@massa", corpo.Massa);
                            cmdCorpo.Parameters.AddWithValue("@densidade", corpo.Densidade);
                            cmdCorpo.ExecuteNonQuery();

                            /* Obtém o ID gerado para o corpo recém-inserido e armazena no mapa de corpos. */
                            int idCorpoGerado = (int)cmdCorpo.LastInsertedId;

                            /* Guarda o ID no próprio corpo para preservá-lo entre requisições HTTP. */
                            corpo.IdCorpoBanco = idCorpoGerado;

                            /* Armazena o ID do corpo no mapa de corpos, associando o objeto Corpo 
                             * ao seu ID no banco de dados. */
                            mapaIdCorpos[corpo] = idCorpoGerado;
                        }
                    }

                    /* Se todas as operações foram bem-sucedidas, a transação é confirmada, 
                     * o cronômetro de gravação é parado e o ID da simulação atual é retornado. */
                    transacao.Commit();
                    cronometroGravacao.Stop();
                    Console.WriteLine(
                        $"[BD] Simulação '{nomeSimulacao}' criada com Sucesso! " +
                        $"(ID: {idSimulacaoAtual})");

                    return idSimulacaoAtual;
                }
                catch (Exception ex)
                {
                    /* Em caso de erro, a transação é revertida, 
                     * o mapa de corpos é limpo e o ID da simulação atual é redefinido para 0. */
                    cronometroGravacao.Stop();
                    transacao.Rollback();
                    mapaIdCorpos.Clear();
                    idSimulacaoAtual = 0;
                    Console.WriteLine($"[ERRO BD] Falha ao iniciar simulação: {ex.Message}");
                    throw;
                }
            }
        }
    }

    /* Exclui todas as simulações do banco de dados. Retorna o número de linhas afetadas. */
    public int ExcluirTodasSimulacoes()
    {
        /* Consulta SQL para excluir todas as simulações da tabela Simulacao. 
         * A exclusão é feita de forma que todas as linhas associadas a cada simulação 
         * sejam removidas devido às restrições de chave estrangeira. */
        string sql = "DELETE FROM Simulacao;";

        /* Abre uma conexão com o banco de dados e executa a exclusão de todas as simulações. 
         * Após a exclusão, o estado da simulação atual é limpo. */
        using (MySqlConnection conexao = ConexaoBD.ObterConexao())
        using (MySqlCommand cmd = new MySqlCommand(sql, conexao))
        {
            /* Executa a exclusão de todas as simulações no banco de dados e retorna o número de linhas afetadas. 
             * Após a exclusão, o estado da simulação atual é limpo. */
            int linhas = cmd.ExecuteNonQuery();
            LimparEstadoDaSimulacaoAtual(); 
            return linhas;
        }
    }

    /* Exclui uma simulação específica do banco de dados. Retorna true se a exclusão foi bem-sucedida, false caso contrário. */
    public bool ExcluirSimulacao(int idSimulacao)
    {
        /* Consulta SQL para excluir a simulação com o ID especificado. 
         * A exclusão é feita na tabela Simulacao, e todas as linhas associadas 
         * a essa simulação serão removidas devido às restrições de chave estrangeira. */
        string sql = @"
        DELETE FROM Simulacao
        WHERE id_simulacao = @idSimulacao;";

        /* Abre uma conexão com o banco de dados e executa a exclusão da simulação especificada. 
         * Se a simulação excluída for a simulação atual, o estado da simulação atual é limpo. */
        using (MySqlConnection conexao = ConexaoBD.ObterConexao())
        using (MySqlCommand cmd = new MySqlCommand(sql, conexao))
        {
            cmd.Parameters.AddWithValue("@idSimulacao", idSimulacao);

            int linhasAfetadas = cmd.ExecuteNonQuery();

            /* Se a simulação excluída for a simulação atual, limpa o estado da simulação atual. */
            if (idSimulacao == idSimulacaoAtual)
            {
                LimparEstadoDaSimulacaoAtual();
            }

            /* Retorna true se pelo menos uma linha foi afetada (ou seja, a simulação foi excluída com sucesso), 
             * caso contrário, retorna false. */
            return linhasAfetadas > 0;
        }
    }

    /* Limpa o estado da simulação atual, incluindo o mapa de corpos e o buffer de linhas. */
    private void LimparEstadoDaSimulacaoAtual()
    {
        /* Limpa o mapa de corpos e o buffer de linhas, e redefine o ID da simulação atual para 0. */
        mapaIdCorpos.Clear();
        bufferLinhas.Clear();
        idSimulacaoAtual = 0;
    }

    /* Classe para armazenar o resumo de uma simulação, incluindo informações como ID, nome, 
     * data de criação, quantidade de corpos, quantidade de iterações e tempo entre iterações. */
    public class ResumoSimulacao
    {
        /* Propriedade para armazenar o ID da simulação. */
        public int Id { get; set; }
        /* Propriedade para armazenar o nome da simulação. */
        public string Nome { get; set; }
        /* Propriedade para armazenar a data de criação da simulação. */
        public DateTime DataCriacao { get; set; }
        /* Propriedade para armazenar a quantidade de corpos na simulação. */
        public int QuantidadeCorpos { get; set; }
        /* Propriedade para armazenar a quantidade de iterações na simulação. */
        public int QuantidadeIteracoes { get; set; }
        /* Propriedade para armazenar o tempo entre iterações na simulação. */
        public double TempoEntreIteracoes { get; set; }

        /* Construtor da classe ResumoSimulacao, que inicializa o nome da simulação como uma string vazia. */
        public ResumoSimulacao()
        {
            Nome = string.Empty;
        }
    }

    /* Carrega o estado de todos os corpos em um determinado passo da simulação. 
     * Retorna uma lista de objetos Corpo representando o estado dos corpos nesse passo. */
    public List<Corpo> CarregarPasso(
    int idSimulacao,
    int numPasso)
    {
        /* Lista para armazenar os corpos carregados do banco de dados. */
        List<Corpo> corpos = new List<Corpo>();

        /* Consulta do MySQL para obter o estado dos corpos em um determinado passo da simulação. 
         * A consulta seleciona informações como nome, massa, densidade, posição, velocidade, 
         * força e aceleração. */
        string sql = @"
       SELECT c.id_corpo, ec.nome, ec.massa, ec.densidade,
       ec.pos_x, ec.pos_y, ec.vel_x, ec.vel_y,
       ec.forca_x, ec.forca_y,
       ec.aceleracao_x, ec.aceleracao_y
       FROM EstadoCorpo ec
       INNER JOIN Corpo c ON c.id_corpo = ec.corpo_id
       WHERE c.id_simulacao = @idSimulacao
       AND ec.num_passo = @numPasso
       ORDER BY c.id_corpo;";

        /* Abre uma conexão com o banco de dados e executa a consulta para obter o estado dos corpos. 
         * Os resultados são lidos e armazenados em uma lista de objetos Corpo. */
        using (MySqlConnection conexao = ConexaoBD.ObterConexao())
        using (MySqlCommand cmd = new MySqlCommand(sql, conexao))
        {
            /* Adiciona os parâmetros necessários para a consulta, incluindo o ID da simulação e o número do passo. */
            cmd.Parameters.AddWithValue("@idSimulacao", idSimulacao);
            cmd.Parameters.AddWithValue("@numPasso", numPasso);

            /* Executa a consulta e lê os resultados usando um MySqlDataReader. 
             * Para cada linha retornada, cria um objeto Corpo com 
             * as informações do estado do corpo. */
            using (MySqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    /* Cria um objeto Corpo com as informações obtidas do banco de dados. 
                     * O construtor do Corpo recebe os valores de nome, massa, densidade, posição e velocidade. 
                     * Em seguida, os valores de força e aceleração são atribuídos 
                     * às propriedades correspondentes. */
                    Corpo corpo = new Corpo(
                        reader.GetString("nome"),
                        reader.GetDouble("massa"),
                        reader.GetDouble("densidade"),
                        reader.GetDouble("pos_x"),
                        reader.GetDouble("pos_y"),
                        reader.GetDouble("vel_x"),
                        reader.GetDouble("vel_y"));

                    /* Recupera o ID original do corpo no banco de dados. */
                    corpo.IdCorpoBanco = reader.GetInt32("id_corpo");

                    /* Atribui os valores de força e aceleração obtidos do banco de dados 
                     * às propriedades correspondentes do objeto Corpo. */
                    corpo.ForcaX = reader.GetDouble("forca_x");
                    corpo.ForcaY = reader.GetDouble("forca_y");
                    corpo.AceleracaoX =
                        reader.GetDouble("aceleracao_x");
                    corpo.AceleracaoY =
                        reader.GetDouble("aceleracao_y");

                    corpos.Add(corpo);
                }
            }
        }
        /* Retorna a lista de corpos carregados do banco de dados para o passo especificado da simulação. */
        return corpos;
    }

    /* Retorna o número do último passo gravado de uma simulação (0 se só o estado inicial foi gravado).
 * Todos os corpos têm os mesmos passos, então basta consultar o primeiro corpo: a consulta usa só o
 * índice (corpo_id, num_passo) e continua rápida mesmo com milhões de linhas. */
    public int ObterUltimoPasso(int idSimulacao)
    {
        string sql = @"
        SELECT COALESCE(MAX(num_passo), 0) AS ultimo_passo
        FROM EstadoCorpo
        WHERE corpo_id = (SELECT MIN(id_corpo) FROM Corpo WHERE id_simulacao = @idSimulacao);";

        using (MySqlConnection conexao = ConexaoBD.ObterConexao())
        using (MySqlCommand cmd = new MySqlCommand(sql, conexao))
        {
            cmd.Parameters.AddWithValue("@idSimulacao", idSimulacao);

            using (MySqlDataReader reader = cmd.ExecuteReader())
            {
                return reader.Read() ? reader.GetInt32("ultimo_passo") : 0;
            }
        }
    }

    /* Retoma a gravação de uma simulação que já foi iniciada (IniciarSimulacao) em uma chamada anterior.
     * Isso é necessário na API Web, que recria os objetos Corpo a cada requisição HTTP: o mapa de IDs é
     * refeito associando cada corpo da lista ao ID gravado, na mesma ordem em que foram inseridos.
     * Também apaga os passos posteriores a "ultimoPasso", o que deixa a chamada repetível: se uma
     * requisição falhar no meio, repeti-la não gera passos duplicados. */
    public void RetomarSimulacao(int idSimulacao, List<Corpo> corpos, int ultimoPasso)
    {
        HashSet<int> idsCorpos = new HashSet<int>();

        using (MySqlConnection conexao = ConexaoBD.ObterConexao())
        {
            /* Lê os IDs de todos os corpos originalmente registrados na simulação. */
            using (MySqlCommand cmd = new MySqlCommand(
                "SELECT id_corpo FROM Corpo WHERE id_simulacao = @idSimulacao;", conexao))
            {
                cmd.Parameters.AddWithValue("@idSimulacao", idSimulacao);

                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        idsCorpos.Add(reader.GetInt32("id_corpo"));
                    }
                }
            }

            /* Confere se cada corpo atual pertence à simulação gravada.
             * Permite que existam menos corpos após uma ou mais fusões. */
            HashSet<int> idsAtuais = new HashSet<int>();

            foreach (var corpo in corpos)
            {
                if (corpo.IdCorpoBanco <= 0 ||
                    !idsCorpos.Contains(corpo.IdCorpoBanco) ||
                    !idsAtuais.Add(corpo.IdCorpoBanco))
                {
                    throw new ArgumentException(
                        "Os corpos do universo não coincidem com os da simulação gravada.");
                }
            }

            /* Remove estados posteriores ao último passo confirmado.
             * Os registros históricos anteriores à fusão são preservados. */
            using (MySqlCommand cmd = new MySqlCommand(@"
            DELETE ec FROM EstadoCorpo ec
            INNER JOIN Corpo c ON c.id_corpo = ec.corpo_id
            WHERE c.id_simulacao = @idSimulacao
              AND ec.num_passo > @ultimoPasso;", conexao))
            {
                cmd.Parameters.AddWithValue("@idSimulacao", idSimulacao);
                cmd.Parameters.AddWithValue("@ultimoPasso", ultimoPasso);
                cmd.ExecuteNonQuery();
            }
        }

        /* Reconstrói o mapa usando os IDs que acompanham os próprios corpos. */
        mapaIdCorpos.Clear();
        bufferLinhas.Clear();
        erroNaGravacao = false;
        idSimulacaoAtual = idSimulacao;

        foreach (var corpo in corpos)
        {
            mapaIdCorpos[corpo] = corpo.IdCorpoBanco;
        }
    }

    /* Um quadro (frame) do replay: o número do passo, o tempo decorrido e, para cada corpo (na ordem de id_corpo),
     * o que a tela precisa para desenhar: id, posição X, posição Y, velocidade X, velocidade Y, massa, densidade e nome. */
    public class QuadroReplay
    {
        public int Passo { get; set; }
        public double Tempo { get; set; }
        public List<object[]> Corpos { get; set; } = new List<object[]>();
    }

    /* Carrega do banco, em uma única consulta, um bloco de passos consecutivos de uma simulação
     * (de "passoInicial" até "passoInicial + quantidade - 1"), para o replay no navegador. */
    public List<QuadroReplay> CarregarQuadros(int idSimulacao, int passoInicial, int quantidade)
    {
        List<QuadroReplay> quadros = new List<QuadroReplay>();

        string sql = @"
SELECT ec.num_passo, ec.tempo_decorrido,
       c.id_corpo,
       ec.pos_x, ec.pos_y, ec.vel_x, ec.vel_y,
       ec.massa, ec.densidade, ec.nome
        FROM EstadoCorpo ec
        INNER JOIN Corpo c ON c.id_corpo = ec.corpo_id
        WHERE c.id_simulacao = @idSimulacao
          AND ec.num_passo >= @passoInicial
          AND ec.num_passo < @passoFinal
        ORDER BY ec.num_passo, c.id_corpo;";

        using (MySqlConnection conexao = ConexaoBD.ObterConexao())
        using (MySqlCommand cmd = new MySqlCommand(sql, conexao))
        {
            cmd.Parameters.AddWithValue("@idSimulacao", idSimulacao);
            cmd.Parameters.AddWithValue("@passoInicial", passoInicial);
            cmd.Parameters.AddWithValue("@passoFinal", passoInicial + quantidade);

            using (MySqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    /* Quando o número do passo muda, começa um novo quadro. */
                    int passo = reader.GetInt32("num_passo");

                    if (quadros.Count == 0 || quadros[quadros.Count - 1].Passo != passo)
                    {
                        quadros.Add(new QuadroReplay
                        {
                            Passo = passo,
                            Tempo = reader.GetDouble("tempo_decorrido")
                        });
                    }

                    /* Adiciona os dados do corpo ao quadro atual. */
                    quadros[quadros.Count - 1].Corpos.Add(new object[]
                    {
                        reader.GetInt32("id_corpo"),
                        reader.GetDouble("pos_x"),
                        reader.GetDouble("pos_y"),
                        reader.GetDouble("vel_x"),
                        reader.GetDouble("vel_y"),
                        reader.GetDouble("massa"),
                        reader.GetDouble("densidade"),
                        reader.GetString("nome")
                    });
                }
            }
        }

        return quadros;
    }

    /* Lista todas as simulações armazenadas no banco de dados, 
     * retornando uma lista de objetos ResumoSimulacao contendo 
     * informações resumidas sobre cada simulação. */
    public List<ResumoSimulacao> ListarSimulacoes()
    {
        /* Lista para armazenar os resumos das simulações encontradas no banco de dados. */
        List<ResumoSimulacao> simulacoes =
            new List<ResumoSimulacao>();

        /* Consulta SQL para selecionar informações resumidas de todas as simulações, 
         * ordenadas por data de criação e ID da simulação em ordem decrescente. */
        string sql = @"
        SELECT id_simulacao, nome, data_criacao,
               qtd_corpos, qtd_iteracoes, tempo_entre_iteracoes
        FROM Simulacao
        ORDER BY data_criacao DESC, id_simulacao DESC;";

        /* Abre uma conexão com o banco de dados, executa a consulta e lê os resultados usando um 
         * MySqlDataReader. Para cada linha retornada, cria um objeto ResumoSimulacao 
         * com as informações da simulação. */
        using (MySqlConnection conexao = ConexaoBD.ObterConexao())
        using (MySqlCommand cmd = new MySqlCommand(sql, conexao))
        using (MySqlDataReader reader = cmd.ExecuteReader())
        {
            /* Lê os resultados da consulta e cria objetos ResumoSimulacao para cada simulação encontrada. 
             * Cada objeto é preenchido com informações como ID, nome, data de criação, quantidade de corpos, 
             * quantidade de iterações e tempo entre iterações. */
            while (reader.Read())
            {
                /* Cria um objeto ResumoSimulacao e preenche suas propriedades com os valores obtidos do banco de dados. */
                ResumoSimulacao simulacao =
                    new ResumoSimulacao();

                /* Atribui os valores obtidos do banco de dados às propriedades do objeto ResumoSimulacao. */
                simulacao.Id =
                    reader.GetInt32("id_simulacao");
                /* Atribui os valores obtidos do banco de dados às propriedades do objeto ResumoSimulacao. */
                simulacao.Nome =
                    reader.GetString("nome");
                /* Atribui os valores obtidos do banco de dados às propriedades do objeto ResumoSimulacao. */
                simulacao.DataCriacao =
                    reader.GetDateTime("data_criacao");
                /* Atribui os valores obtidos do banco de dados às propriedades do objeto ResumoSimulacao. */
                simulacao.QuantidadeCorpos =
                    reader.GetInt32("qtd_corpos");
                /* Atribui os valores obtidos do banco de dados às propriedades do objeto ResumoSimulacao. */
                simulacao.QuantidadeIteracoes =
                    reader.GetInt32("qtd_iteracoes");
                /* Atribui os valores obtidos do banco de dados às propriedades do objeto ResumoSimulacao. */
                simulacao.TempoEntreIteracoes =
                    reader.GetDouble("tempo_entre_iteracoes");
                /* Adiciona o objeto ResumoSimulacao à lista de simulações encontradas no banco de dados. */
                simulacoes.Add(simulacao);
            }
        }

        /* Retorna a lista de simulações encontradas no banco de dados. */
        return simulacoes;
    }

    /* Método para gravar o estado de todos os corpos em um determinado passo da simulação */
    public void GravarPasso(int numPasso, double tempoDecorrido, List<Corpo> corpos)
    {
        if (idSimulacaoAtual == 0)
        {
            /* Se a simulação atual não foi iniciada (idSimulacaoAtual é 0), 
             * lança uma exceção indicando que é necessário iniciar uma simulação antes de gravar os passos. */
            throw new InvalidOperationException(
                "Inicie uma simulação antes de gravar os passos.");
        }

        /* Se a gravação já deu erro, não adianta continuar juntando linhas. */
        if (erroNaGravacao) return;

        /* Para cada corpo na lista de corpos fornecida, verifica se o corpo pertence à simulação atual 
         * (usando o mapa de IDs de corpos). Se o corpo não pertencer à simulação, lança uma exceção. 
         * Caso contrário, cria uma nova linha de estado (LinhaEstado) com as informações do corpo e adiciona 
         * ao buffer de linhas a serem gravadas no banco de dados. */
        foreach (var corpo in corpos)
        {
            /* Verifica se o corpo pertence à simulação atual, obtendo seu ID do mapa de corpos. 
             * Se o corpo não estiver no mapa, lança uma exceção indicando que o corpo não pertence à simulação. */
            if (!mapaIdCorpos.TryGetValue(corpo, out int idCorpo))
            {
                /* Lança uma exceção indicando que o corpo não pertence à simulação atual. */
                throw new InvalidOperationException(
                    "Um dos corpos não pertence à simulação iniciada.");
            }

            /* Cria uma nova linha de estado (LinhaEstado) com as informações do corpo e adiciona ao buffer de linhas. */
            bufferLinhas.Add(new LinhaEstado(idCorpo, numPasso, tempoDecorrido, corpo));
        }

        /* Se o buffer de linhas acumuladas atingir ou exceder o limite definido por LinhasPorBloco, 
         * chama o método DescarregarBuffer para gravar as linhas no banco de dados. */
        if (bufferLinhas.Count >= LinhasPorBloco)
        {
            DescarregarBuffer();
        }
    }

    /* Tira as linhas do buffer e as coloca na fila de blocos (no máximo LinhasPorBloco linhas
     * por bloco). Não espera o banco: quem grava é a thread de gravação. */
    public void DescarregarBuffer()
    {
        if (bufferLinhas.Count == 0)
        {
            return;
        }

        for (int inicio = 0; inicio < bufferLinhas.Count; inicio += LinhasPorBloco)
        {
            int quantidade = Math.Min(LinhasPorBloco, bufferLinhas.Count - inicio);
            blocosEsperando.Add(bufferLinhas.GetRange(inicio, quantidade));
        }
        bufferLinhas.Clear();

        /* Cria a thread de gravação na primeira vez. */
        threadGravacao ??= Task.Factory.StartNew(GravarBlocosEsperando, TaskCreationOptions.LongRunning);
    }

    /* Executado pela thread de gravação: grava os blocos, um por vez, até a fila ser fechada
     * no FinalizarSimulacao. Depois de um erro, os blocos restantes são descartados. */
    private void GravarBlocosEsperando()
    {
        try
        {
            foreach (List<LinhaEstado> bloco in blocosEsperando.GetConsumingEnumerable())
            {
                if (erroNaGravacao) continue;

                try
                {
                    GravarBloco(bloco);
                }
                catch (Exception ex)
                {
                    excecaoGravacao = ex;
                    erroNaGravacao = true;
                }
            }
        }
        finally
        {
            FecharConexao();
        }
    }

    /* Grava no banco um bloco de linhas, com UM INSERT de várias linhas
     * dentro de uma transação (ou grava o bloco inteiro, ou nada dele). */
    private void GravarBloco(List<LinhaEstado> linhas)
    {
        /* Inicia o cronômetro para medir o tempo gasto na gravação do bloco de linhas. */
        cronometroGravacao.Start();
        try
        {
            /* Abre a conexão só na primeira vez; depois ela é reaproveitada. */
            conexaoGravacao ??= ConexaoBD.ObterConexao();

            /* Inicia uma transação para garantir que todas as operações de inserção sejam atômicas.
             * Se ocorrer algum erro durante a inserção, a transação será revertida. */
            using (MySqlTransaction transacao = conexaoGravacao.BeginTransaction())
            {
                try
                {
                    StringBuilder sql = new StringBuilder(
                        "INSERT INTO EstadoCorpo (corpo_id, num_passo, tempo_decorrido, nome, massa, densidade, " +
                        "pos_x, pos_y, vel_x, vel_y, forca_x, forca_y, aceleracao_x, aceleracao_y) VALUES ");

                    using (MySqlCommand cmd = new MySqlCommand())
                    {
                        cmd.Connection = conexaoGravacao;
                        cmd.Transaction = transacao;

                        /* Cada linha ganha parâmetros com sufixo próprio (@c0, @n0, @c1, ...) */
                        for (int i = 0; i < linhas.Count; i++)
                        {
                            LinhaEstado l = linhas[i];

                            /* Adiciona uma vírgula entre os conjuntos de valores, exceto antes do primeiro conjunto. */
                            if (i > 0) sql.Append(',');
                            sql.Append($"(@c{i},@n{i},@t{i},@nm{i},@m{i},@d{i},@px{i},@py{i},@vx{i},@vy{i},@fx{i},@fy{i},@ax{i},@ay{i})");

                            cmd.Parameters.AddWithValue($"@c{i}", l.CorpoId);
                            cmd.Parameters.AddWithValue($"@n{i}", l.NumPasso);
                            cmd.Parameters.AddWithValue($"@t{i}", l.TempoDecorrido);
                            cmd.Parameters.AddWithValue($"@nm{i}", l.Nome);
                            cmd.Parameters.AddWithValue($"@m{i}", l.Massa);
                            cmd.Parameters.AddWithValue($"@d{i}", l.Densidade);
                            cmd.Parameters.AddWithValue($"@px{i}", l.PosX);
                            cmd.Parameters.AddWithValue($"@py{i}", l.PosY);
                            cmd.Parameters.AddWithValue($"@vx{i}", l.VelX);
                            cmd.Parameters.AddWithValue($"@vy{i}", l.VelY);
                            cmd.Parameters.AddWithValue($"@fx{i}", l.ForcaX);
                            cmd.Parameters.AddWithValue($"@fy{i}", l.ForcaY);
                            cmd.Parameters.AddWithValue($"@ax{i}", l.AceleracaoX);
                            cmd.Parameters.AddWithValue($"@ay{i}", l.AceleracaoY);
                        }

                        cmd.CommandText = sql.ToString();
                        cmd.ExecuteNonQuery();
                    }

                    transacao.Commit();
                }
                catch (Exception ex)
                {
                    transacao.Rollback();
                    Console.WriteLine($"[ERRO BD] Falha ao gravar um bloco de passos: {ex.Message}");
                    throw;
                }
            }
        }
        finally
        {
            /* Para garantir que o cronômetro de gravação seja parado, mesmo em caso de erro. */
            cronometroGravacao.Stop();
        }
    }

    /* Deve ser chamado ao fim da simulação: manda o último bloco (quase sempre menor que
     * LinhasPorBloco), espera a thread de gravação terminar tudo e fecha a conexão. Sem isso,
     * as últimas linhas ficariam só na memória e o replay as perderia.
     * Se a gravação deu erro, a exceção é lançada aqui. */
    public void FinalizarSimulacao()
    {
        try
        {
            if (!erroNaGravacao)
            {
                DescarregarBuffer();
            }
        }
        finally
        {
            bufferLinhas.Clear();
            if (!blocosEsperando.IsAddingCompleted) blocosEsperando.CompleteAdding();

            /* Espera a thread de gravação terminar (ela fecha a própria conexão). */
            if (threadGravacao is not null) threadGravacao.GetAwaiter().GetResult();
            else FecharConexao();
        }

        if (excecaoGravacao is not null)
        {
            ExceptionDispatchInfo.Capture(excecaoGravacao).Throw();
        }
    }

    /* Fecha a conexão com o banco de dados, se estiver aberta. */
    private void FecharConexao()
    {
        /* Se a conexão de gravação estiver aberta, ela é descartada e definida como nula para liberar recursos. */
        conexaoGravacao?.Dispose();
        conexaoGravacao = null;
    }

    /* Rede de segurança caso o FinalizarSimulacao não seja chamado. */
    public void Dispose()
    {
        /* Fecha a fila: a thread de gravação termina o que já recebeu e fecha a própria conexão. */
        if (!blocosEsperando.IsAddingCompleted) blocosEsperando.CompleteAdding();
        if (threadGravacao is null) FecharConexao();
        bufferLinhas.Clear();
    }
}
