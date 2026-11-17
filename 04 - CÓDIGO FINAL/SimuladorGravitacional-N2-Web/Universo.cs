using SimuladorGravitacional_N2;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
namespace TrabalhoN2;

/* Essa classe representa o universo, contendo uma lista de corpos e métodos para calcular forças, atualizar posições e tratar colisões. */
public class Universo
{
    /* Constante gravitacional utilizada no cálculo da força entre os corpos. */
    private const double G = 6.674184e-11;

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

    /* Tratar colisões entre os corpos, ajustando posições e velocidades conforme necessário. */
    public void TratarColisoes()
    {
        /* O raio depende só de massa e densidade (não muda no laço), mas Raio é uma
         * propriedade calculada com raiz cúbica. Calculá-lo uma vez por corpo troca
         * n² raízes cúbicas por n. */
        double[] raios = new double[Corpos.Count];
        for (int k = 0; k < Corpos.Count; k++)
        {
            raios[k] = Corpos[k].Raio;
        }

        /* Percorre todos os pares de corpos para verificar colisões. */
        for (int i = 0; i < Corpos.Count; i++)
        {
            for (int j = i + 1; j < Corpos.Count; j++)
            {
                /* Obtém os corpos que estão sendo comparados na iteração atual. */
                Corpo corpo1 = Corpos[i];
                Corpo corpo2 = Corpos[j];

                /* A soma dos raios define o limite para ocorrer uma colisão. */
                double somaRaios = raios[i] + raios[j];

                /* Compara os QUADRADOS para evitar a raiz quadrada nos pares que não
                 * colidem (a maioria): d <= r equivale a d² <= r², pois ambos são positivos. */
                double deltaX = corpo2.PosX - corpo1.PosX;
                double deltaY = corpo2.PosY - corpo1.PosY;
                double distanciaQuadrada = deltaX * deltaX + deltaY * deltaY;

                if (distanciaQuadrada <= somaRaios * somaRaios)
                {
                    /* Só agora calcula a distância real, necessária para a normal. */
                    double distancia = Math.Sqrt(distanciaQuadrada);

                    /* Calcula a normal da colisão: direção do corpo 1 para o corpo 2. */
                    double normalX;
                    double normalY;

                    if (distancia > 0)
                    {
                        /* Normaliza a direção da colisão para obter a normal. */
                        normalX = (corpo2.PosX - corpo1.PosX) / distancia;
                        normalY = (corpo2.PosY - corpo1.PosY) / distancia;
                    }
                    else
                    {
                        /* Quando os centros coincidem, usa a velocidade relativa para
                         * definir uma direção de separação e evita divisão por zero. */
                        double diferencaVelX = corpo2.VelX - corpo1.VelX;
                        double diferencaVelY = corpo2.VelY - corpo1.VelY;

                        /* Calcula o módulo da velocidade relativa para normalizar a direção. */
                        double moduloVelocidade = Math.Sqrt(
                            diferencaVelX * diferencaVelX +
                            diferencaVelY * diferencaVelY);

                        /* Normaliza a direção da velocidade relativa para obter a normal da colisão. */
                        if (moduloVelocidade > 0)
                        {
                            /* Normaliza a direção da velocidade relativa para obter a normal da colisão. */
                            normalX = diferencaVelX / moduloVelocidade;
                            normalY = diferencaVelY / moduloVelocidade;
                        }
                        else
                        {
                            /* Se a velocidade relativa também for zero, define uma normal arbitrária. */
                            normalX = 1;
                            normalY = 0;
                        }
                    }

                    /* Dados do par usados na conservação de energia. */
                    double somaMassas = corpo1.Massa + corpo2.Massa;
                    double massaReduzida = corpo1.Massa * corpo2.Massa / somaMassas;

                    /* Energia potencial gravitacional do par ANTES de separar os corpos
                     * (a distância mínima evita divisão por zero se os centros coincidirem). */
                    double distanciaAntes = Math.Max(distancia, somaRaios * 1e-6);
                    double potencialAntes = -G * corpo1.Massa * corpo2.Massa / distanciaAntes;

                    /* Resolve a sobreposição. A correção é dividida conforme a massa
                     * (o corpo mais leve se move mais), então o centro de massa do par não se desloca. */
                    double sobreposicao = somaRaios - distancia;

                    if (sobreposicao > 0)
                    {
                        double fracao1 = corpo2.Massa / somaMassas;
                        double fracao2 = corpo1.Massa / somaMassas;

                        corpo1.PosX -= sobreposicao * fracao1 * normalX;
                        corpo1.PosY -= sobreposicao * fracao1 * normalY;

                        corpo2.PosX += sobreposicao * fracao2 * normalX;
                        corpo2.PosY += sobreposicao * fracao2 * normalY;
                    }

                    /* Energia potencial DEPOIS de separar. Afastar os corpos realiza trabalho contra
                     * a gravidade: sem descontar isso, a separação cria energia do nada e os corpos
                     * podem ganhar energia suficiente para escapar um do outro. */
                    double potencialDepois = -G * corpo1.Massa * corpo2.Massa / Math.Max(distanciaAntes, somaRaios);
                    double trabalhoContraGravidade = potencialDepois - potencialAntes;

                    /* Velocidade relativa projetada na normal (negativa = os corpos estão se aproximando). */
                    double velocidadeNaNormal =
                        (corpo2.VelX - corpo1.VelX) * normalX +
                        (corpo2.VelY - corpo1.VelY) * normalY;

                    /* Só é um impacto novo se os corpos estão se aproximando. Corpos em contato que
                     * já se afastam, ou que repousam um sobre o outro, não geram nova mensagem. */
                    if (velocidadeNaNormal < 0)
                    {
                        Console.WriteLine(
                            $"Colisão detectada entre {corpo1.Nome} e {corpo2.Nome}");
                    }

                    /* Colisão elástica: se aproximavam, a componente normal da velocidade relativa inverte;
                     * se já se afastavam, é mantida. Depois, retira-se da energia cinética relativa o
                     * trabalho realizado contra a gravidade. */
                    double energiaRelativa = 0.5 * massaReduzida * velocidadeNaNormal * velocidadeNaNormal
                                             - trabalhoContraGravidade;
                    double velocidadeNormalApos = energiaRelativa > 0
                        ? Math.Sqrt(2.0 * energiaRelativa / massaReduzida)
                        : 0.0;

                    /* Impulso que leva a componente normal da velocidade relativa ao valor calculado.
                     * Conserva a quantidade de movimento do par (impulsos iguais e opostos). */
                    double impulso = massaReduzida * (velocidadeNormalApos - velocidadeNaNormal);

                    corpo1.VelX -= impulso * normalX / corpo1.Massa;
                    corpo1.VelY -= impulso * normalY / corpo1.Massa;

                    corpo2.VelX += impulso * normalX / corpo2.Massa;
                    corpo2.VelY += impulso * normalY / corpo2.Massa;
                }
            }
        }
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

            /* Atualiza as posições dos corpos com base no passo calculado. */
            tempoPosicoes?.Start();
            AtualizarPosicoes(passo);
            tempoPosicoes?.Stop();

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