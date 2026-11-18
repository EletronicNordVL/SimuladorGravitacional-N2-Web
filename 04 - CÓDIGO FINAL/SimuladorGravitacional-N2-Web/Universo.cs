using SimuladorGravitacional_N2;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Linq;
namespace TrabalhoN2;

/* Essa classe representa o universo, contendo uma lista de corpos e métodos para calcular forças, atualizar posições e tratar colisões. */
public class Universo
{
    /* Constante gravitacional utilizada no cálculo da força entre os corpos. */
    private const double G = 6.674184e-11;

    /* Raio máximo permitido para o universo, em metros. */
    private const double RaioLimiteUniverso = 1e10;

    /* Lista de corpos presentes no universo. */
    public List<Corpo> Corpos { get; private set; }

    /* Quantidade de iterações que a simulação irá executar. */
    public int QuantidadeIteracoes { get; set; }

    /* Tempo entre cada iteração da simulação, em segundos. */
    public double TempoEntreIteracoes { get; set; }

    /* Instância do gravador do MySQL */
    private GravadorUniversoBD gravador;

    /* Construtor da classe Universo. */
    public Universo()
    {
        Corpos = new List<Corpo>();
        gravador = new GravadorUniversoBD();
    }

    /* Adiciona um corpo ao universo. */
    public void AdicionarCorpo(Corpo corpo)
    {
        /* Adiciona o corpo à lista de corpos do universo. */
        Corpos.Add(corpo);
    }

    /* Calcula a distância entre dois corpos. */
    private double CalcularDistancia(Corpo corpo1, Corpo corpo2)
    {
        double deltaX = corpo2.PosX - corpo1.PosX;
        double deltaY = corpo2.PosY - corpo1.PosY;

        /* Utiliza o Teorema de Pitágoras para calcular a distância entre os corpos. */
        return Math.Sqrt(
            deltaX * deltaX +
            deltaY * deltaY
        );
    }

    /* Calcula a força gravitacional entre dois corpos. */
    private double CalcularForcaGravitacional(
        Corpo corpo1,
        Corpo corpo2)
    {
        /* Calcula a distância entre os corpos usando o método CalcularDistancia. */
        double distancia = CalcularDistancia(corpo1, corpo2);

        /* Evita divisão por zero caso os corpos estejam na mesma posição. */
        if (distancia == 0)
        {
            return 0;
        }

        /* Aplica a fórmula da força gravitacional: F = G * (m1 * m2) / r² */
        return G * corpo1.Massa * corpo2.Massa
               / (distancia * distancia);
    }

    /* Calcula a força gravitacional resultante sobre os corpos usando Parallel.For. */
    public void CalcularForcas()
    {
        /* Zera as forças resultantes antes de calcular as novas forças. */
        Parallel.For(0, Corpos.Count, i =>
        {
            /* Obtém o corpo atual da iteração. */
            Corpo corpo1 = Corpos[i];
            double acumuladorForcaX = 0;
            double acumuladorForcaY = 0;

            /* Calcula a força gravitacional entre o corpo atual e todos os outros corpos. */
            for (int j = 0; j < Corpos.Count; j++)
            {
                if (i == j) continue;

                Corpo corpo2 = Corpos[j];

                /* Calcula a diferença de posição entre os corpos para determinar a direção da força. */
                double deltaX = corpo2.PosX - corpo1.PosX;
                double deltaY = corpo2.PosY - corpo1.PosY;
                double distancia = CalcularDistancia(corpo1, corpo2);

                if (distancia == 0) continue;

                /* Calcula a força gravitacional entre os dois corpos e a projeta nas direções X e Y. */
                double forca = CalcularForcaGravitacional(corpo1, corpo2);
                double direcaoX = deltaX / distancia;
                double direcaoY = deltaY / distancia;

                /* Acumula as componentes da força resultante sobre o corpo atual. */
                acumuladorForcaX += forca * direcaoX;
                acumuladorForcaY += forca * direcaoY;
            }

            /* Atualiza as forças resultantes do corpo atual com os valores acumulados. */
            corpo1.ForcaX = acumuladorForcaX;
            corpo1.ForcaY = acumuladorForcaY;

            /* Atualiza as acelerações com base nas forças resultantes. */
            corpo1.AceleracaoX = corpo1.ForcaX / corpo1.Massa;
            corpo1.AceleracaoY = corpo1.ForcaY / corpo1.Massa;
        });

    }

    /* Atualiza posições e velocidades utilizando o método Velocity Verlet. */
    public void AtualizarPosicoes(double tempoEntreIteracoes)
    {
        /* Validar o intervalo de tempo antes de iniciar os cálculos. */
        if (tempoEntreIteracoes <= 0 ||
            !double.IsFinite(tempoEntreIteracoes))
        {
            throw new ArgumentException(
                "O intervalo de tempo deve ser positivo e finito.");
        }

        /* Armazenar as acelerações anteriores de cada corpo. */
        double[] aceleracoesAnterioresX = new double[Corpos.Count];
        double[] aceleracoesAnterioresY = new double[Corpos.Count];

        /* Atualizar as posições dos corpos. */
        Parallel.For(0, Corpos.Count, i =>
        {
            Corpo corpo = Corpos[i];

            /* Guardar as acelerações calculadas antes da atualização. */
            aceleracoesAnterioresX[i] = corpo.AceleracaoX;
            aceleracoesAnterioresY[i] = corpo.AceleracaoY;

            /* Atualizar a posição utilizando a aceleração anterior. */
            corpo.PosX += corpo.VelX * tempoEntreIteracoes
                + 0.5 * corpo.AceleracaoX
                * tempoEntreIteracoes * tempoEntreIteracoes;

            corpo.PosY += corpo.VelY * tempoEntreIteracoes
                + 0.5 * corpo.AceleracaoY
                * tempoEntreIteracoes * tempoEntreIteracoes;
        });

        /* Recalcular as forças nas novas posições. */
        CalcularForcas();

        /* Atualiza as velocidades usando a média
         * entre as acelerações anteriores e as novas acelerações. */
        Parallel.For(0, Corpos.Count, i =>
        {
            Corpo corpo = Corpos[i];

            corpo.VelX += 0.5 *
                (aceleracoesAnterioresX[i] + corpo.AceleracaoX)
                * tempoEntreIteracoes;

            corpo.VelY += 0.5 *
                (aceleracoesAnterioresY[i] + corpo.AceleracaoY)
                * tempoEntreIteracoes;
        });
    }


    /* Impede que os corpos escapem indefinidamente do universo. */
    private void ConterCorposNoUniverso()
    {
        foreach (Corpo corpo in Corpos)
        {
            double distancia = Math.Sqrt(
                corpo.PosX * corpo.PosX +
                corpo.PosY * corpo.PosY
            );

            double limite = RaioLimiteUniverso - corpo.Raio;

            if (distancia <= limite || distancia == 0)
                continue;

            /* Direção radial da posição do corpo. */
            double direcaoX = corpo.PosX / distancia;
            double direcaoY = corpo.PosY / distancia;

            /* Posiciona o corpo exatamente dentro do limite. */
            corpo.PosX = direcaoX * limite;
            corpo.PosY = direcaoY * limite;

            /* Calcula a velocidade total do corpo. */
            double velocidade = Math.Sqrt(
                corpo.VelX * corpo.VelX +
                corpo.VelY * corpo.VelY
            );

            /* Redireciona o movimento diretamente para o centro. */
            corpo.VelX = -velocidade * direcaoX;
            corpo.VelY = -velocidade * direcaoY;
        }
    }

    /* Detecta colisões mesmo quando os corpos atravessam um ao outro
     * entre dois subpassos da simulação. */
    private bool TratarColisoesNoPercurso(
        double[] posXAnterior,
        double[] posYAnterior)
    {
        for (int i = 0; i < Corpos.Count; i++)
        {
            for (int j = i + 1; j < Corpos.Count; j++)
            {
                Corpo corpo1 = Corpos[i];
                Corpo corpo2 = Corpos[j];

                double xInicial = posXAnterior[j] - posXAnterior[i];
                double yInicial = posYAnterior[j] - posYAnterior[i];

                double xFinal = corpo2.PosX - corpo1.PosX;
                double yFinal = corpo2.PosY - corpo1.PosY;

                double dx = xFinal - xInicial;
                double dy = yFinal - yInicial;

                double comprimento = dx * dx + dy * dy;

                double t = comprimento > 0
                    ? -(xInicial * dx + yInicial * dy) / comprimento
                    : 0;

                t = Math.Clamp(t, 0, 1);

                double menorX = xInicial + t * dx;
                double menorY = yInicial + t * dy;

                double somaRaios = corpo1.Raio + corpo2.Raio;

                if (menorX * menorX + menorY * menorY
                    <= somaRaios * somaRaios)
                {
                    /* Reposiciona os corpos no instante aproximado
                     * em que ocorreu o contato. */
                    corpo1.PosX = posXAnterior[i]
                        + (corpo1.PosX - posXAnterior[i]) * t;

                    corpo1.PosY = posYAnterior[i]
                        + (corpo1.PosY - posYAnterior[i]) * t;

                    corpo2.PosX = posXAnterior[j]
                        + (corpo2.PosX - posXAnterior[j]) * t;

                    corpo2.PosY = posYAnterior[j]
                        + (corpo2.PosY - posYAnterior[j]) * t;

                    TratarColisoes();
                    return true;
                }
            }
        }

        return false;
    }

    /* Trata colisões entre os corpos, fundindo os corpos que se encostarem. */
    public void TratarColisoes()
    {
        bool houveFusao;
        do
        {
            houveFusao = false;

            for (int i = 0; i < Corpos.Count && !houveFusao; i++)
            {
                for (int j = i + 1; j < Corpos.Count; j++)
                {
                    Corpo corpo1 = Corpos[i];
                    Corpo corpo2 = Corpos[j];

                    double somaRaios = corpo1.Raio + corpo2.Raio;

                    double dx = corpo2.PosX - corpo1.PosX;
                    double dy = corpo2.PosY - corpo1.PosY;

                    double distanciaQuadrada = dx * dx + dy * dy;

                    if (distanciaQuadrada <= somaRaios * somaRaios)
                    {
                        /* Conservação da massa. */
                        double massaTotal = corpo1.Massa + corpo2.Massa;

                        /* Conservação do momento linear. */
                        double novaVelX =
                            (corpo1.Massa * corpo1.VelX +
                             corpo2.Massa * corpo2.VelX) / massaTotal;

                        double novaVelY =
                            (corpo1.Massa * corpo1.VelY +
                             corpo2.Massa * corpo2.VelY) / massaTotal;

                        /* Centro de massa. */
                        double novaPosX =
                            (corpo1.Massa * corpo1.PosX +
                             corpo2.Massa * corpo2.PosX) / massaTotal;

                        double novaPosY =
                            (corpo1.Massa * corpo1.PosY +
                             corpo2.Massa * corpo2.PosY) / massaTotal;

                        /* Conservação do volume para calcular a nova densidade. */
                        double volumeTotal =
                            corpo1.Massa / corpo1.Densidade +
                            corpo2.Massa / corpo2.Densidade;

                        double novaDensidade = massaTotal / volumeTotal;

                        /* Nome do corpo resultante da fusão. */
                        string novoNome =
                            $"Fusão ({corpo1.Nome} + {corpo2.Nome})";

                        if (novoNome.Length > 60)
                            novoNome = novoNome.Substring(0, 60);

                        /* Atualiza o primeiro corpo com os dados da fusão.
                         * Mantém a mesma instância para preservar seu ID no gravador do banco. */
                        corpo1.Nome = novoNome;
                        corpo1.Massa = massaTotal;
                        corpo1.Densidade = novaDensidade;

                        corpo1.PosX = novaPosX;
                        corpo1.PosY = novaPosY;

                        corpo1.VelX = novaVelX;
                        corpo1.VelY = novaVelY;

                        corpo1.ForcaX = 0;
                        corpo1.ForcaY = 0;
                        corpo1.AceleracaoX = 0;
                        corpo1.AceleracaoY = 0;

                        /* Remove o segundo corpo, pois agora existe apenas um. */
                        Corpos.RemoveAt(j);

                        Console.WriteLine(
                            $"Fusão realizada: {novoNome}");

                        /* Reinicia a verificação para detectar novas colisões
                         * envolvendo o corpo resultante. */
                        houveFusao = true;
                        break;
                    }
                }
            }

        } while (houveFusao);
    }

    /* Maior passo de tempo que ainda resolve o encontro mais próximo do universo.
     * Para cada par usa o menor entre (a) o tempo para percorrer a distância atual na velocidade relativa
     * e (b) o tempo dinâmico sqrt(d³ / G(m1+m2)). Corpos distantes quase nunca limitam o passo. */
    private double PassoSeguro(double passoMaximo, double fator)
    {
        double passo = passoMaximo;

        for (int i = 0; i < Corpos.Count; i++)
        {
            for (int j = i + 1; j < Corpos.Count; j++)
            {
                /* Calcula a diferença de posição entre os corpos para determinar a distância entre eles. */
                double dx = Corpos[j].PosX - Corpos[i].PosX;
                double dy = Corpos[j].PosY - Corpos[i].PosY;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d == 0) continue;

                /* Calcula a diferença de velocidade entre os corpos para determinar a velocidade relativa. */
                double dvx = Corpos[j].VelX - Corpos[i].VelX;
                double dvy = Corpos[j].VelY - Corpos[i].VelY;
                double v = Math.Sqrt(dvx * dvx + dvy * dvy);

                /* Calcula o tempo de travessia baseado na distância e velocidade relativa. Se a velocidade for zero, define como infinito. */
                double tempoTravessia = v > 0 ? d / v : double.MaxValue;
                double tempoDinamico = Math.Sqrt(d * d * d / (G * (Corpos[i].Massa + Corpos[j].Massa)));
                passo = Math.Min(passo, fator * Math.Min(tempoTravessia, tempoDinamico));
            }
        }

        return passo;
    }

    /* Avança UMA iteração (tempoEntreIteracoes), dividindo-a em subpassos quando há corpos próximos.
     * O número de iterações gravadas e o tempo entre elas não mudam; só a precisão interna melhora.
     * Cada subpasso: posições (Velocity Verlet) -> colisões -> forças. Antes da primeira chamada,
     * CalcularForcas() precisa ter sido executado (o Velocity Verlet usa as acelerações atuais).
     * Os cronômetros são opcionais e acumulam o tempo de cada fase, para o relatório de desempenho. */
    public void AvancarIteracao(
        double tempoEntreIteracoes,
        Stopwatch? tempoForcas = null,
        Stopwatch? tempoPosicoes = null,
        Stopwatch? tempoColisoes = null)
    {
        const double fatorSeguranca = 0.02;  /* fração do tempo característico usada em cada subpasso */
        const int maximoSubpassos = 500;     /* limite para que o custo nunca exploda */

        /* Calcula o passo mínimo permitido para cada subpasso, garantindo que não haja mais de maximoSubpassos. */
        double minimo = tempoEntreIteracoes / maximoSubpassos;
        double restante = tempoEntreIteracoes;

        /* Enquanto houver tempo restante para avançar, 
         * realiza subpassos de atualização de posições, 
         * tratamento de colisões e cálculo de forças. */
        while (restante > tempoEntreIteracoes * 1e-12)
        {
            tempoForcas?.Start();
            double passo = Math.Min(restante,
                Math.Max(PassoSeguro(tempoEntreIteracoes, fatorSeguranca), minimo));
            tempoForcas?.Stop();

            /* Guarda as posições anteriores para detectar colisões no percurso. */
            double[] posXAnterior = Corpos.Select(c => c.PosX).ToArray();
            double[] posYAnterior = Corpos.Select(c => c.PosY).ToArray();

            /* Atualiza as posições dos corpos com base no passo calculado. */
            tempoPosicoes?.Start();
            AtualizarPosicoes(passo);
            tempoPosicoes?.Stop();

            /* Detecta colisões ocorridas durante o deslocamento. */
            tempoColisoes?.Start();
            TratarColisoesNoPercurso(posXAnterior, posYAnterior);
            tempoColisoes?.Stop();

            /* Mantém todos os corpos dentro dos limites do universo. */
            ConterCorposNoUniverso();

            /* Trata as colisões entre os corpos após a atualização das posições. */
            tempoColisoes?.Start();
            TratarColisoes();
            tempoColisoes?.Stop();

            /* Recalcula as forças após o tratamento das colisões. */
            tempoForcas?.Start();
            CalcularForcas();
            tempoForcas?.Stop();

            /* Reduz o tempo restante pelo passo que acabou de ser processado. */
            restante -= passo;
        }
    }

    /* Exibe o estado atual dos corpos. */
    public void ExibirEstado()
    {
        foreach (Corpo corpo in Corpos)
        {
            /* Exibe o nome, posição e velocidade de cada corpo com quatro casas decimais. */
            Console.WriteLine(
                $"{corpo.Nome} - " +
                $"Posição: ({corpo.PosX:F4}, {corpo.PosY:F4}) - " +
                $"Velocidade: ({corpo.VelX:F4}, {corpo.VelY:F4})");
        }
    }

    /* Método que executa as iterações da simulação, mede o tempo de cada fase e grava no MySQL. */
    public void ExecutarSimulacao(
        int iteracoes,
        double tempoEntreIteracoes,
        string? nomeSimulacao = null)
    {
        /* Configura a quantidade de iterações e o tempo entre elas. */
        this.QuantidadeIteracoes = iteracoes;
        this.TempoEntreIteracoes = tempoEntreIteracoes;

        /* Nome padrão com data e hora, para distinguir as simulações na listagem do replay. */
        nomeSimulacao ??= $"Simulação {DateTime.Now:dd/MM/yyyy HH:mm:ss}";

        /* Cronômetro total: começa ANTES de iniciar o registro no banco, porque criar
         * a simulação também conta como tempo de gravação. */
        Stopwatch cronometro = Stopwatch.StartNew();

        /* Cronômetros por fase: acumulam o tempo de todas as iterações. */
        Stopwatch tempoForcas = new Stopwatch();
        Stopwatch tempoPosicoes = new Stopwatch();
        Stopwatch tempoColisoes = new Stopwatch();

        /* Bloco de Try -Finally para garantir que o gravador finalize a simulação e 
         * feche a conexão com o banco, mesmo se ocorrer algum erro durante a execução. */
        try
        {
            /* Iniciar o registro no Banco de Dados MySQL */
            gravador.IniciarSimulacao(nomeSimulacao, Corpos, iteracoes, tempoEntreIteracoes);

            /* Gravar o estado inicial, antes de qualquer iteração. */
            gravador.GravarPasso(0, 0, Corpos);

            /* Variável para acumular o tempo decorrido da simulação. */
            double tempoDecorrido = 0;

            /* Calcular as forças iniciais antes da primeira iteração.
             * O Velocity Verlet precisa das acelerações iniciais. */
            tempoForcas.Start();
            CalcularForcas();
            tempoForcas.Stop();

            /* Executar as iterações da simulação utilizando Velocity Verlet. */
            for (int i = 1; i <= iteracoes; i++)
            {
                /* Atualizar o tempo decorrido com base no tempo entre iterações. */
                tempoDecorrido += tempoEntreIteracoes;

                /* Avançar UMA iteração. O método divide o intervalo em subpassos quando há corpos próximos
                 * (posições Velocity Verlet -> colisões -> forças) e acumula o tempo de cada fase. */
                AvancarIteracao(tempoEntreIteracoes, tempoForcas, tempoPosicoes, tempoColisoes);

                /* Gravar o histórico do passo no MySQL. */
                gravador.GravarPasso(i, tempoDecorrido, Corpos);
            }
        }
        /* Finally garante que o gravador finalize a simulação e 
         * feche a conexão com o banco, mesmo se ocorrer algum erro durante a execução. */
        finally
        {
            /* Grava o último bloco pendente e fecha a conexão, mesmo se der erro no meio. */
            gravador.FinalizarSimulacao();
            cronometro.Stop();
        }

        /* Calcula o tempo total gasto nas fases de cálculo de forças, atualização de posições e tratamento de colisões. */
        TimeSpan calculo = tempoForcas.Elapsed + tempoPosicoes.Elapsed + tempoColisoes.Elapsed;

        /* Exibe o tempo total da simulação e o tempo gasto em cada fase (cálculo de forças, 
         * atualização de posições, tratamento de colisões e gravação no banco). */
        Console.WriteLine();
        Console.WriteLine($"[DESEMPENHO N2] Tempo total: {cronometro.Elapsed.TotalMilliseconds:F2} ms ({cronometro.Elapsed.TotalSeconds:F2} s)");
        Console.WriteLine($"  Cálculo (forças + posições + colisões): {calculo.TotalMilliseconds:F2} ms");
        Console.WriteLine($"    Forças   : {tempoForcas.Elapsed.TotalMilliseconds:F2} ms");
        Console.WriteLine($"    Posições : {tempoPosicoes.Elapsed.TotalMilliseconds:F2} ms");
        Console.WriteLine($"    Colisões : {tempoColisoes.Elapsed.TotalMilliseconds:F2} ms");
        Console.WriteLine($"  Gravação no banco: {gravador.TempoTotalGravacao.TotalMilliseconds:F2} ms");
        Console.WriteLine();
    }

    /* Gera corpos aleatórios para a simulação. */
    public void GerarCorposAleatorios(int quantidade)
    {
        Random random = new Random();

        /* Gera corpos com propriedades aleatórias dentro de intervalos definidos. */
        for (int i = 0; i < quantidade; i++)
        {
            /* Gera um nome único para cada corpo. */
            string nome = $"Corpo {i + 1}";
            double massa = random.NextDouble() * (1e25 - 1e22) + 1e22;      /* 10^22 a 10^25 kg */
            double densidade = random.NextDouble() * (6000 - 3000) + 3000;  /* 3000 a 6000 kg/m³ */
            double posX = random.NextDouble() * (5e9 - (-5e9)) + (-5e9);    /* ±5 bilhões de metros */
            double posY = random.NextDouble() * (5e9 - (-5e9)) + (-5e9);
            double velX = random.NextDouble() * (200 - (-200)) + (-200);    /* ±200 m/s */
            double velY = random.NextDouble() * (200 - (-200)) + (-200);

            /* Cria um novo corpo com as propriedades geradas aleatoriamente. */
            Corpo corpo = new Corpo(
                nome,
                massa,
                densidade,
                posX,
                posY,
                velX,
                velY
            );

            /* Adiciona o corpo gerado aleatoriamente ao universo. */
            AdicionarCorpo(corpo);
        }
    }
}