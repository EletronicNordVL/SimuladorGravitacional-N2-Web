using System;
using System.Threading;
using System.Threading.Tasks;
using TrabalhoN2;

namespace SimuladorGravitacional_N2;

/* Guarda no servidor uma simulação que está rodando no navegador.
 * Antes, a página mandava todos os corpos a cada requisição e o servidor recriava o
 * universo e o gravador toda vez. Agora o universo e o gravador ficam aqui, na memória,
 * do começo ao fim, e a página só pede para avançar alguns passos. */
public sealed class SessaoSimulacao
{
    /* Identificador que a página usa para falar com esta simulação. */
    public Guid Id { get; } = Guid.NewGuid();

    /* O universo que está sendo simulado. */
    public Universo Universo { get; }

    /* Iteração atual (0 = estado inicial). */
    public int Iteracao { get; private set; }

    /* Tempo físico já simulado, em segundos. */
    public double TempoDecorrido { get; private set; }

    /* Gravador do MySQL. Fica null quando a gravação está desligada ou deu erro. */
    public GravadorUniversoBD? Gravador { get; private set; }

    /* ID da simulação no banco (0 quando não está gravando). */
    public int IdSimulacao { get; private set; }

    /* Momento da última requisição, para encerrar simulações de abas que foram fechadas. */
    public DateTime UltimoUso { get; private set; } = DateTime.UtcNow;

    /* Só deixa uma requisição por vez mexer nesta simulação. */
    public SemaphoreSlim Trava { get; } = new SemaphoreSlim(1, 1);

    public SessaoSimulacao(Universo universo, int iteracao, double tempoDecorrido)
    {
        Universo = universo;
        Iteracao = iteracao;
        TempoDecorrido = tempoDecorrido;
    }

    /* Cria o registro da simulação no MySQL e grava o passo 0.
     * Se não conseguir, a simulação roda sem gravar e a mensagem é devolvida para a página. */
    public string? ComecarGravacao(string nome)
    {
        var gravador = new GravadorUniversoBD();
        try
        {
            IdSimulacao = gravador.IniciarSimulacao(nome, Universo.Corpos,
                Universo.QuantidadeIteracoes, Universo.TempoEntreIteracoes);
            gravador.GravarPasso(Iteracao, TempoDecorrido, Universo.Corpos);
            Gravador = gravador;
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO BD] Gravação não iniciada: {ex.Message}");
            gravador.Dispose();
            IdSimulacao = 0;
            return "Não foi possível gravar no banco de dados. A simulação vai continuar sem gravação.";
        }
    }

    /* Executa até "passos" iterações e grava cada uma.
     * Para mais cedo se chegar ao limite de iterações ou se o banco estiver atrasado
     * (nesse caso a página tenta de novo na próxima chamada, sem perder nenhum passo). */
    public (int fusoes, bool esperandoBanco, string? aviso) Avancar(int passos)
    {
        UltimoUso = DateTime.UtcNow;
        string? aviso = PararGravacaoSeDeuErro();
        int fusoes = 0;
        bool esperandoBanco = false;

        int quantos = Math.Min(passos, Universo.QuantidadeIteracoes - Iteracao);
        for (int i = 0; i < quantos; i++)
        {
            if (Gravador is not null && Gravador.BancoAtrasado)
            {
                esperandoBanco = true;
                break;
            }

            Universo.AvancarIteracao(Universo.TempoEntreIteracoes);
            Snapshot.ValidateBodies(Universo.Corpos);
            Iteracao++;
            TempoDecorrido += Universo.TempoEntreIteracoes;
            fusoes += Universo.FusoesUltimaIteracao;

            Gravador?.GravarPasso(Iteracao, TempoDecorrido, Universo.Corpos);
        }

        return (fusoes, esperandoBanco, aviso ?? PararGravacaoSeDeuErro());
    }

    /* true quando todas as iterações já foram executadas. */
    public bool Terminou => Iteracao >= Universo.QuantidadeIteracoes;

    /* Se a gravação deu erro, desliga a gravação (a simulação continua) e devolve o aviso. */
    private string? PararGravacaoSeDeuErro()
    {
        if (Gravador is null || !Gravador.HouveErroGravacao) return null;

        var gravador = Gravador;
        Gravador = null;
        _ = Task.Run(() =>
        {
            try { gravador.FinalizarSimulacao(); } catch { /* o erro já foi avisado */ }
            finally { gravador.Dispose(); }
        });
        return "A gravação no banco de dados falhou. A simulação continua, mas sem gravação.";
    }

    /* Termina a gravação: espera todos os passos chegarem no banco e fecha a conexão.
     * Devolve um aviso se algo deu errado. */
    public async Task<string?> TerminarGravacaoAsync()
    {
        var gravador = Gravador;
        if (gravador is null) return null;
        Gravador = null;

        try
        {
            await Task.Run(gravador.FinalizarSimulacao);
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO BD] Falha ao terminar a gravação: {ex.Message}");
            return "Erro ao terminar a gravação. As últimas iterações podem não ter sido gravadas.";
        }
        finally
        {
            gravador.Dispose();
        }
    }
}
