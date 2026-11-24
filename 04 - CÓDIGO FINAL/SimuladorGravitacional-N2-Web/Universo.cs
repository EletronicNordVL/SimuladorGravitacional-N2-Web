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
    private const double G = 6.67430e-11;

    // A escala converte o passo de 0,016 s da referencia para 3.600 s na Web.
    private const double MetrosPorUnidade = 1e7;
    private const double SegundosPorUnidade = 3600 / 0.016;
    private const double Suavizacao = 15 * MetrosPorUnidade;
    private const double MassaPorUnidade = (800 / G) *
        (MetrosPorUnidade * MetrosPorUnidade * MetrosPorUnidade) /
        (SegundosPorUnidade * SegundosPorUnidade);

    /* Lista de corpos presentes no universo. */
    public List<Corpo> Corpos { get; private set; }

    /* Quantidade de iterações que a simulação irá executar. */
    public int QuantidadeIteracoes { get; set; }

    /* Tempo entre cada iteração da simulação, em segundos. */
    public double TempoEntreIteracoes { get; set; }

    /* Define como os cálculos são feitos: true usa várias threads (N2) e false usa
     * uma única thread, como no trabalho N1. Serve para comparar o desempenho das duas. */
    public bool UsarParalelismo { get; set; } = true;

    /* Quantas fusões aconteceram na última chamada de TratarColisoes. */
    public int FusoesUltimaIteracao { get; private set; }

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

    public void CalcularForcas()
    {
        /* Sem paralelismo, usa a versão de uma thread só (N1). */
        if (!UsarParalelismo)
        {
            CalcularForcasUmaThread();
            return;
        }

        int quantidade = Corpos.Count;
        var total = new (double X, double Y)[quantidade];
        var trava = new object();

        Parallel.For(0, quantidade,
            () => new (double X, double Y)[quantidade],
            (i, _, local) =>
            {
                Corpo a = Corpos[i];
                for (int j = i + 1; j < quantidade; j++)
                {
                    Corpo b = Corpos[j];
                    double dx = b.PosX - a.PosX;
                    double dy = b.PosY - a.PosY;
                    double d2 = dx * dx + dy * dy + Suavizacao * Suavizacao;
                    double fator = G / (d2 * Math.Sqrt(d2));
                    local[i].X += fator * dx * b.Massa;
                    local[i].Y += fator * dy * b.Massa;
                    local[j].X -= fator * dx * a.Massa;
                    local[j].Y -= fator * dy * a.Massa;
                }
                return local;
            },
            local =>
            {
                lock (trava)
                {
                    for (int i = 0; i < quantidade; i++)
                    {
                        total[i].X += local[i].X;
                        total[i].Y += local[i].Y;
                    }
                }
            });

        for (int i = 0; i < quantidade; i++)
        {
            Corpo corpo = Corpos[i];
            (corpo.AceleracaoX, corpo.AceleracaoY) = total[i];
            corpo.ForcaX = corpo.Massa * corpo.AceleracaoX;
            corpo.ForcaY = corpo.Massa * corpo.AceleracaoY;
        }
    }

    /* Mesmo cálculo de CalcularForcas, mas em uma única thread (versão N1).
     * Percorre cada par de corpos uma única vez utilizando o (j = i + 1). */
    private void CalcularForcasUmaThread()
    {
        /* Zera as acelerações da iteração anterior para não acumular com o novo ciclo. */
        foreach (Corpo corpo in Corpos)
        {
            corpo.AceleracaoX = 0;
            corpo.AceleracaoY = 0;
        }

        for (int i = 0; i < Corpos.Count; i++)
        {
            for (int j = i + 1; j < Corpos.Count; j++)
            {
                Corpo corpo1 = Corpos[i];
                Corpo corpo2 = Corpos[j];

                double deltaX = corpo2.PosX - corpo1.PosX;
                double deltaY = corpo2.PosY - corpo1.PosY;
                double d2 = deltaX * deltaX + deltaY * deltaY + Suavizacao * Suavizacao;
                double fator = G / (d2 * Math.Sqrt(d2));

                /* Terceira Lei de Newton (Ação e Reação): cada corpo é puxado na direção do outro. */
                corpo1.AceleracaoX += fator * deltaX * corpo2.Massa;
                corpo1.AceleracaoY += fator * deltaY * corpo2.Massa;
                corpo2.AceleracaoX -= fator * deltaX * corpo1.Massa;
                corpo2.AceleracaoY -= fator * deltaY * corpo1.Massa;
            }
        }

        foreach (Corpo corpo in Corpos)
        {
            corpo.ForcaX = corpo.Massa * corpo.AceleracaoX;
            corpo.ForcaY = corpo.Massa * corpo.AceleracaoY;
        }
    }

    public void AtualizarPosicoes(double passo)
    {
        if (!double.IsFinite(passo) || passo <= 0)
            throw new ArgumentException("O intervalo de tempo deve ser positivo e finito.");

        foreach (Corpo corpo in Corpos)
        {
            corpo.VelX += corpo.AceleracaoX * passo;
            corpo.VelY += corpo.AceleracaoY * passo;
            corpo.PosX += corpo.VelX * passo;
            corpo.PosY += corpo.VelY * passo;
        }
    }

    private static double RaioColisao(Corpo corpo) =>
        MetrosPorUnidade * Math.Max(2, 2.8 * Math.Cbrt(corpo.Massa / MassaPorUnidade * 3000 / corpo.Densidade));

    private static bool Sobrepostos(Corpo a, Corpo b, double somaRaios)
    {
        double dx = b.PosX - a.PosX;
        double dy = b.PosY - a.PosY;
        return dx * dx + dy * dy < somaRaios * somaRaios;
    }

    public void TratarColisoes()
    {
        FusoesUltimaIteracao = 0;
        int quantidade = Corpos.Count;
        if (quantidade < 2) return;

        var raios = new double[quantidade];
        for (int i = 0; i < quantidade; i++)
            raios[i] = RaioColisao(Corpos[i]);

        /* Na versão N2 a busca pelos pares em contato roda em paralelo.
         * Na versão N1 ela é feita dentro do próprio laço das fusões, abaixo. */
        var candidatos = new List<int>?[quantidade];
        if (UsarParalelismo)
        {
            Parallel.For(0, quantidade, i =>
            {
                for (int j = i + 1; j < quantidade; j++)
                {
                    if (Sobrepostos(Corpos[i], Corpos[j], raios[i] + raios[j]))
                        (candidatos[i] ??= new List<int>()).Add(j);
                }
            });
        }

        // As fusoes ocorrem em ordem e revalidam o contato.
        var absorvidos = new HashSet<Corpo>();
        for (int i = 0; i < quantidade; i++)
        {
            Corpo a = Corpos[i];
            if (absorvidos.Contains(a)) continue;

            /* N2: se a busca paralela não achou nenhum contato, não há o que fundir.
             * Se achou, começa do primeiro contato encontrado. A partir dele os corpos
             * seguintes são conferidos de novo, porque depois de uma fusão o corpo "a"
             * fica maior e pode alcançar outros. Assim N1 e N2 dão o mesmo resultado. */
            int inicio = i + 1;

            if (UsarParalelismo && candidatos[i] is not { Count: > 0 })
                continue;

            for (int j = inicio; j < quantidade; j++)
            {
                Corpo b = Corpos[j];
                if (absorvidos.Contains(b) || !Sobrepostos(a, b, raios[i] + raios[j])) continue;

                Fundir(a, b);
                raios[i] = RaioColisao(a);
                absorvidos.Add(b);
                FusoesUltimaIteracao++;
            }
        }
        Corpos.RemoveAll(absorvidos.Contains);
    }

    private static void Fundir(Corpo a, Corpo b)
    {
        double massa = a.Massa + b.Massa;
        double pesoA = a.Massa / massa;
        double pesoB = b.Massa / massa;

        if (b.Massa > a.Massa) a.Nome = b.Nome;
        a.PosX = a.PosX * pesoA + b.PosX * pesoB;
        a.PosY = a.PosY * pesoA + b.PosY * pesoB;
        a.VelX = a.VelX * pesoA + b.VelX * pesoB;
        a.VelY = a.VelY * pesoA + b.VelY * pesoB;
        a.Densidade = a.Densidade * pesoA + b.Densidade * pesoB;
        a.Massa = massa;
    }

    public void AvancarIteracao(
        double tempoEntreIteracoes,
        Stopwatch? tempoForcas = null,
        Stopwatch? tempoPosicoes = null,
        Stopwatch? tempoColisoes = null)
    {
        tempoPosicoes?.Start();
        AtualizarPosicoes(tempoEntreIteracoes);
        tempoPosicoes?.Stop();

        tempoColisoes?.Start();
        TratarColisoes();
        tempoColisoes?.Stop();

        tempoForcas?.Start();
        CalcularForcas();
        tempoForcas?.Stop();
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

            tempoForcas.Start();
            CalcularForcas();
            tempoForcas.Stop();

            for (int i = 1; i <= iteracoes; i++)
            {
                /* Atualizar o tempo decorrido com base no tempo entre iterações. */
                tempoDecorrido += tempoEntreIteracoes;

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

    public void GerarCorposAleatorios(int quantidade)
    {
        Random random = Random.Shared;
        double[] densidades = { 500, 1000, 2000, 3000, 5000, 7874, 11340 };

        int quantidadeGigantes = Math.Max(2, quantidade / 5);

        // Área menor para aumentar a proximidade inicial dos corpos.
        const double larguraUtil = 500;
        const double alturaUtil = 300;

        // Velocidades iniciais menores para favorecer encontros gravitacionais.
        const double velocidadeMaxima = 30;

        const double escalaVelocidade = MetrosPorUnidade / SegundosPorUnidade;

        for (int i = 0; i < quantidade; i++)
        {
            bool gigante = i < quantidadeGigantes;

            double expoente = gigante
                ? 2.0 + random.NextDouble() * 1.2
                : 0.4 + random.NextDouble() * 1.6;

            double massa = Math.Pow(10, expoente) * MassaPorUnidade;

            double densidade = densidades[random.Next(densidades.Length)]
                * (0.7 + random.NextDouble() * 0.6);

            double velocidade = random.NextDouble() * velocidadeMaxima * escalaVelocidade;
            double angulo = random.NextDouble() * Math.Tau;

            double posX = (random.NextDouble() - 0.5)
                * larguraUtil * MetrosPorUnidade;

            double posY = (random.NextDouble() - 0.5)
                * alturaUtil * MetrosPorUnidade;

            string prefixo = gigante ? "Gigante" : "Corpo";

            AdicionarCorpo(new Corpo($"{prefixo}-{i + 1:D3}", massa,
                Math.Clamp(densidade, 500, 15000), posX, posY,
                Math.Cos(angulo) * velocidade,
                Math.Sin(angulo) * velocidade));
        }
    }
}
