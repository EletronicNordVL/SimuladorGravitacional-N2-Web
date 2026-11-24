using System.Globalization;
using SimuladorGravitacional_N2;
using MySql.Data.MySqlClient;
using System.Collections.Concurrent;
using TrabalhoN2;

/* Simulações que estão rodando agora, cada uma ligada a uma aba do navegador. */
var sessoes = new ConcurrentDictionary<Guid, SessaoSimulacao>();

/* Depois de quanto tempo sem uso uma simulação é encerrada. */
var tempoMaximoOcioso = TimeSpan.FromMinutes(15);

/* Este projeto implementa uma simulação de um universo com corpos celestes, permitindo a criação, manipulação e exportação de estados do universo. 
 * A aplicação é construída utilizando ASP.NET Core e fornece uma API RESTful para interação com o universo simulado. */
var builder = WebApplication.CreateBuilder(args);

/* Configura o caminho de armazenamento dos universos salvos. Se não for especificado, utiliza o diretório pai do diretório de conteúdo da aplicação. */
builder.WebHost.UseUrls("http://localhost:5080");

/* Configura o diretório raiz para servir arquivos estáticos, como HTML, CSS e JavaScript. */
builder.WebHost.UseWebRoot(Path.Combine(builder.Environment.ContentRootPath, "wwwroot"));

/* Configura a aplicação para aceitar e retornar dados JSON com nomes de propriedades insensíveis a maiúsculas e minúsculas. */
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNameCaseInsensitive = true);

/* Configura o diretório de armazenamento dos universos salvos. Se não for especificado, utiliza o diretório pai do diretório de conteúdo da aplicação. */
var app = builder.Build();
var storage = Path.GetFullPath(builder.Configuration["UniverseStoragePath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, ".."));

/* Middleware para tratamento de exceções e retorno de erros em JSON.Captura ArgumentException, FormatException, OverflowException, InvalidDataException e IndexOutOfRangeException como erros 400 (Bad Request). Captura IOException como erro 409 (Conflict) com mensagem genérica. */
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or InvalidDataException or IndexOutOfRangeException)
    {
        /* Retorna um erro 400 (Bad Request) com a mensagem da exceção. */
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (IOException)
    {
        /* Retorna um erro 409 (Conflict) com uma mensagem genérica para evitar expor detalhes do sistema de arquivos. */
        context.Response.StatusCode = 409;
        await context.Response.WriteAsJsonAsync(new { error = "Não foi possível acessar o arquivo. Tente novamente." });
    }
    /* Falha ao acessar o MySQL: servidor desligado, erro de SQL ou variável SIMULADOR_DB_CONNECTION ausente (ConexaoBD lança InvalidOperationException).
     * Retorna 503 (Service Unavailable) com mensagem genérica, sem expor detalhes da conexão. */
    catch (Exception ex) when (ex is MySqlException || (ex is InvalidOperationException && ex.Message.Contains("SIMULADOR_DB_CONNECTION")))
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new { error = "Não foi possível acessar o banco de dados. Verifique se o MySQL está em execução e se a variável SIMULADOR_DB_CONNECTION está configurada." });
    }
});

/*  * Middleware para habilitar CORS (Cross-Origin Resource Sharing) para permitir requisições de qualquer origem. */
app.UseDefaultFiles();
app.UseStaticFiles();

/*  * Rotas da API: */
app.MapGet("/api/health", (HttpResponse response) =>
{
    /* Retorna informações de saúde da aplicação que incluem status, 
     * nome da aplicação, caminho de armazenamento e ID do processo. */
    response.Headers.CacheControl = "no-store";
    return new { status = "ok", application = "SimuladorGravitacionalN2", storagePath = storage, processId = Environment.ProcessId };
});

/*  * Rota para criar um novo universo com corpos aleatórios. */
app.MapPost("/api/universes/create", (CreateRequest request) =>
{
    /* Valida os parâmetros da requisição para criar o universo, garantindo que a quantidade de corpos, 
     * iterações e passo de tempo estejam dentro dos limites aceitáveis. */
    if (request.Count < 1)
        throw new ArgumentException("A quantidade de corpos deve ser maior que zero.");

    /* Valida a quantidade de iterações, garantindo que esteja entre 1 e 1.000.000. */
    if (request.Iterations < 1 || request.Iterations > 1000000)
        throw new ArgumentException("Use de 1 a 1.000.000 iterações.");

    /* Valida o passo de tempo, garantindo que seja finito e esteja entre 0,001 e 86.400 segundos. */
    if (!double.IsFinite(request.Timestep) ||
        request.Timestep < .001 || request.Timestep > 86400)
        throw new ArgumentException("O passo de tempo deve estar entre 0,001 e 86.400 segundos.");

    /* Cria um novo universo com os parâmetros validados e gera corpos aleatórios. */
    var universe = new Universo
    {
        /* Define a quantidade de iterações e o tempo entre iterações do universo. */
        QuantidadeIteracoes = request.Iterations,
        TempoEntreIteracoes = request.Timestep
    };

    /* Gera corpos aleatórios no universo, garantindo que cada corpo tenha massa, densidade, posição e velocidade dentro dos limites aceitáveis. */
    universe.GerarCorposAleatorios(request.Count);
    universe.CalcularForcas();

    /* Retorna o estado inicial do universo criado, com a iteração e tempo decorrido definidos como zero. */
    return Snapshot.From(universe, 0, 0);
});

/* Rota que começa uma simulação no servidor, a partir do universo que está na tela.
 * Se a página pedir gravação (e for a iteração 0), a simulação também é criada no MySQL. */
app.MapPost("/api/simulation/start", (StartRequest request) =>
{
    ArgumentNullException.ThrowIfNull(request.State);

    var universe = request.State.ToUniverse();
    universe.UsarParalelismo = request.Paralelo;
    universe.CalcularForcas();

    var sessao = new SessaoSimulacao(universe, request.State.Iteration, request.State.Elapsed);
    string? aviso = null;
    if (request.Gravar && request.State.Iteration == 0)
        aviso = sessao.ComecarGravacao($"Simulação {DateTime.Now:dd/MM/yyyy HH:mm:ss}");

    sessoes[sessao.Id] = sessao;

    return Results.Ok(new
    {
        sessao = sessao.Id,
        aviso,
        threads = Environment.ProcessorCount,
        state = Snapshot.From(universe, sessao.Iteracao, sessao.TempoDecorrido, sessao.IdSimulacao)
    });
});

/* Rota que avança a simulação guardada no servidor. A página manda só a quantidade de
 * passos e as configurações (que podem ter mudado enquanto estava pausada). */
app.MapPost("/api/simulation/{id:guid}/step", async (Guid id, StepRequest request) =>
{
    if (!sessoes.TryGetValue(id, out var sessao))
        return Results.NotFound(new { error = "A simulação não está mais ativa no servidor." });
    if (request.Steps < 1 || request.Steps > 100) throw new ArgumentException("Quantidade de passos inválida.");

    await sessao.Trava.WaitAsync();
    try
    {
        var universe = sessao.Universo;

        if (request.QuantidadeIteracoes < sessao.Iteracao)
            throw new ArgumentException("O limite de iterações é inválido.");
        if (!double.IsFinite(request.TempoEntreIteracoes) || request.TempoEntreIteracoes < .001 || request.TempoEntreIteracoes > 86400)
            throw new ArgumentException("O passo de tempo deve estar entre 0,001 e 86.400 segundos.");
        universe.QuantidadeIteracoes = request.QuantidadeIteracoes;
        universe.TempoEntreIteracoes = request.TempoEntreIteracoes;
        universe.UsarParalelismo = request.Paralelo;

        var (fusoes, esperandoBanco, aviso) = sessao.Avancar(request.Steps);

        /* Na última iteração, espera tudo ser gravado e fecha a gravação. */
        if (sessao.Terminou)
            aviso = await sessao.TerminarGravacaoAsync() ?? aviso;

        return Results.Ok(new
        {
            state = Snapshot.From(universe, sessao.Iteracao, sessao.TempoDecorrido, sessao.IdSimulacao),
            fusoes,
            esperandoBanco,
            gravando = sessao.Gravador is not null,
            threads = Environment.ProcessorCount,
            aviso
        });
    }
    finally
    {
        sessao.Trava.Release();
    }
});

/* Rota que encerra a simulação do servidor (novo universo, reinício, edição ou aba fechada).
 * Só responde depois que todos os passos foram gravados. */
app.MapPost("/api/simulation/{id:guid}/stop", async (Guid id) =>
{
    if (!sessoes.TryRemove(id, out var sessao)) return Results.NoContent();

    await sessao.Trava.WaitAsync();
    try
    {
        var aviso = await sessao.TerminarGravacaoAsync();
        return aviso is null ? Results.NoContent() : Results.Ok(new { aviso });
    }
    finally
    {
        sessao.Trava.Release();
    }
});

/* A cada minuto, encerra as simulações que ninguém usa há muito tempo
 * (por exemplo, quando a aba foi fechada e o aviso de encerramento não chegou). */
_ = Task.Run(async () =>
{
    using var relogio = new PeriodicTimer(TimeSpan.FromMinutes(1));
    while (await relogio.WaitForNextTickAsync())
    {
        foreach (var (id, sessao) in sessoes)
        {
            if (DateTime.UtcNow - sessao.UltimoUso < tempoMaximoOcioso || !sessoes.TryRemove(id, out _)) continue;
            await sessao.Trava.WaitAsync();
            try { await sessao.TerminarGravacaoAsync(); }
            finally { sessao.Trava.Release(); }
        }
    }
});

/* Quando o servidor for desligado, termina as gravações abertas para não perder passos. */
app.Lifetime.ApplicationStopping.Register(() =>
{
    var gravacoes = sessoes.Values.Select(sessao => sessao.TerminarGravacaoAsync()).ToArray();
    try { Task.WaitAll(gravacoes, TimeSpan.FromSeconds(30)); } catch { /* o servidor já está fechando */ }
});

/* Rota para listar todas as simulações salvas no banco de dados. */
app.MapGet("/api/universes", () =>
{
    /* Cria uma instância do GravadorUniversoBD para acessar o banco de dados e listar as simulações salvas. */
    GravadorUniversoBD gravadorBD =
        new GravadorUniversoBD();

    /* Retorna uma lista de simulações salvas, contendo o ID, nome e data de criação, 
     * ordenadas pela data de modificação em ordem decrescente. */
    return gravadorBD.ListarSimulacoes()
        .Select(simulacao => new
        {
            /* Propriedades do objeto anônimo retornado para cada simulação salva. */
            id = simulacao.Id,
            name = simulacao.Nome,
            modified = simulacao.DataCriacao
        })

        /* Ordena as simulações salvas pela data de modificação em ordem decrescente, 
         * para que as mais recentes apareçam primeiro. */
        .OrderByDescending(simulacao => simulacao.modified)
        .ToList();
});

/*  Rota para salvar o estado atual do universo no MySQL. O estado atual vira o passo 0 de uma nova simulação
 *  (equivale ao antigo universo_N.txt: guarda posições e velocidades atuais, e os contadores recomeçam ao abrir). */
app.MapPost("/api/universes/save", (Snapshot state) =>
{
    /* Converte o estado do universo recebido na requisição para um objeto Universo (a conversão também valida os dados). */
    var universe = state.ToUniverse();

    /* Nome legível para diferenciar as simulações na lista de universos salvos. */
    var name = $"Universo {DateTime.Now:dd/MM/yyyy HH:mm:ss}";

    /* O using garante que a conexão de gravação seja fechada, mesmo se ocorrer algum erro. */
    using var gravador = new GravadorUniversoBD();
    var id = gravador.IniciarSimulacao(name, universe.Corpos, universe.QuantidadeIteracoes, universe.TempoEntreIteracoes);
    try
    {
        /* Grava o passo 0 da simulação no banco de dados, que representa as condições iniciais do universo. */
        gravador.GravarPasso(0, 0, universe.Corpos);
        gravador.FinalizarSimulacao();
    }
    catch
    {
        /* Se a gravação do passo 0 falhar, remove a simulação criada para não deixar um registro vazio no banco. */
        try { gravador.ExcluirSimulacao(id); } catch { /* melhor esforço: o erro original é o que importa */ }
        throw;
    }

    /* Retorna o ID e o nome da simulação salva, para que a tela possa continuar gravando na mesma simulação se desejar. */
    return Results.Ok(new {
        id,
        name,
        corpoIds = universe.Corpos
        .Select(corpo => corpo.IdCorpoBanco)
        .ToArray()
    });
});

/*  Rota para carregar um universo salvo no MySQL. Carrega o passo 0 (condições iniciais) da simulação com o ID informado. */
app.MapGet("/api/universes/{id:int}", (int id) =>
{
    /* Cria uma instância do GravadorUniversoBD para acessar o banco de dados e carregar a simulação com o ID informado. */
    using var gravador = new GravadorUniversoBD();

    /* Busca os dados gerais da simulação (quantidade de iterações e tempo entre iterações). */
    var resumo = gravador.ListarSimulacoes().FirstOrDefault(simulacao => simulacao.Id == id);
    if (resumo is null) return Results.NotFound(new { error = "Simulação não encontrada." });

    /* Monta o universo com os corpos do passo 0 gravados no banco. */
    var universe = new Universo { QuantidadeIteracoes = resumo.QuantidadeIteracoes, TempoEntreIteracoes = resumo.TempoEntreIteracoes };
    foreach (var corpo in gravador.CarregarPasso(id, 0)) universe.AdicionarCorpo(corpo);

    /* Valida os corpos do universo carregado para garantir que todos os parâmetros estejam dentro dos limites aceitáveis. */
    ValidateBodies(universe.Corpos);
    universe.CalcularForcas();
    var state = Snapshot.From(universe, 0, 0);
    state.ToUniverse();
    return Results.Ok(state);
});

/* Rota para excluir todas as simulações salvas no banco de dados. */
app.MapDelete("/api/universes", async () =>
{
    var avisos = new List<string>();

    /* Encerra todas as sessões ativas antes de excluir os registros. */
    foreach (var id in sessoes.Keys.ToArray())
    {
        if (!sessoes.TryRemove(id, out var sessao))
            continue;

        await sessao.Trava.WaitAsync();

        try
        {
            var aviso = await sessao.TerminarGravacaoAsync();

            if (aviso is not null)
                avisos.Add(aviso);
        }
        finally
        {
            sessao.Trava.Release();
        }
    }

    /* Exclui todas as simulações. As tabelas relacionadas são limpas
     * pelas restrições ON DELETE CASCADE do banco de dados. */
    using var gravador = new GravadorUniversoBD();

    int excluidas = gravador.ExcluirTodasSimulacoes();

    return Results.Ok(new
    {
        excluidas,
        aviso = avisos.Count > 0
            ? string.Join(" ", avisos)
            : null
    });
});

/*  Rota que retorna os dados gerais de uma simulação gravada para o replay como o nome, o último passo gravado e o estado
 *  inicial (passo 0), que traz os corpos com nome, massa e densidade. Os passos seguintes vêm da rota /frames. */
app.MapGet("/api/replay/{id:int}", (int id) =>
{
    using var gravador = new GravadorUniversoBD();

    /* Busca os dados gerais da simulação (quantidade de iterações e tempo entre iterações) para o replay. */
    var resumo = gravador.ListarSimulacoes().FirstOrDefault(simulacao => simulacao.Id == id);
    if (resumo is null) return Results.NotFound(new { error = "Simulação não encontrada." });

    /* Monta o universo com os corpos do passo 0 gravados no banco, que será usado para o replay. */
    var universe = new Universo { QuantidadeIteracoes = resumo.QuantidadeIteracoes, TempoEntreIteracoes = resumo.TempoEntreIteracoes };
    foreach (var corpo in gravador.CarregarPasso(id, 0)) universe.AdicionarCorpo(corpo);

    /* Valida os corpos do universo carregado para garantir que todos os parâmetros estejam dentro dos limites aceitáveis. */
    return Results.Ok(new
    {
        nome = resumo.Nome,
        maxPasso = gravador.ObterUltimoPasso(id),
        state = Snapshot.From(universe, 0, 0)
    });
});

/*  Rota que retorna um bloco de passos consecutivos de uma simulação gravada (de "inicio" até "inicio + quantidade - 1").
 *  Cada quadro traz só o que a tela precisa para desenhar: posição e velocidade de cada corpo. */
app.MapGet("/api/replay/{id:int}/frames", (int id, int inicio, int quantidade) =>
{
    if (inicio < 0 || quantidade < 1 || quantidade > 500) throw new ArgumentException("Intervalo de replay inválido.");

    /* Cria uma instância do GravadorUniversoBD para acessar o banco de dados e carregar os quadros da simulação com o ID informado. */
    using var gravador = new GravadorUniversoBD();
    return Results.Ok(gravador.CarregarQuadros(id, inicio, quantidade));
});

/* Inicia a aplicação web. */
app.Run();

/* Funções auxiliares. */
/* Função para validar os corpos do universo, garantindo que todos os parâmetros estejam dentro dos limites aceitáveis. */
static void ValidateBodies(List<Corpo> bodies) => Snapshot.ValidateBodies(bodies);

/* Classes de requisição para a API. */
record CreateRequest(int Count, int Iterations, double Timestep);

/* Classe de requisição para iniciar uma simulação no servidor. */
record StartRequest(Snapshot State, bool Gravar, bool Paralelo = true);

/* Classe de requisição para avançar a simulação em um número de passos. */
record StepRequest(int Steps, int QuantidadeIteracoes, double TempoEntreIteracoes, bool Paralelo = true);

/* Classe Snapshot representa o estado de um universo em um determinado ponto no tempo, incluindo os corpos presentes, 
 * a quantidade de iterações, o tempo entre iterações, a iteração atual e o tempo decorrido. */
record Snapshot(
    List<Corpo> Corpos,
    int QuantidadeIteracoes,
    double TempoEntreIteracoes,
    int Iteration,
    double Elapsed,
    int IdSimulacao = 0)
{
    /* Construtor da classe Snapshot, que inicializa os campos com os valores fornecidos. */
    public static Snapshot From(
        Universo u,
        int iteration,
        double elapsed,
        int idSimulacao = 0)
    {
        /* Valida os corpos do universo antes de criar o Snapshot. */
        ValidateBodies(u.Corpos);
        if (u.Corpos.Any(c => new[] { c.ForcaX, c.ForcaY, c.AceleracaoX, c.AceleracaoY }.Any(v => !double.IsFinite(v))))
            throw new ArgumentException("O cálculo excedeu a precisão numérica. Reduza o passo de tempo ou aumente a distância entre os corpos.");
        return new(
            u.Corpos,
            u.QuantidadeIteracoes,
            u.TempoEntreIteracoes,
            iteration,
            elapsed,
            idSimulacao);
    }

    /* Converte o Snapshot de volta para um Universo, validando os dados. */
    public Universo ToUniverse()
    {
        /* Valida os parâmetros do Snapshot antes de criar o Universo. */
        if (QuantidadeIteracoes < 1 || QuantidadeIteracoes > int.MaxValue)
            throw new ArgumentException("Quantidade de iterações inválida.");

        /* Valida o tempo entre iterações, garantindo que seja finito e esteja dentro do 
         * intervalo aceitável (0,001 a 86.400 segundos). */
        if (!double.IsFinite(TempoEntreIteracoes) || TempoEntreIteracoes < .001 || TempoEntreIteracoes > 86400)
            throw new ArgumentException("O passo de tempo deve estar entre 0,001 e 86.400 segundos.");

        /* Valida o estado da simulação (iteração e tempo decorrido). */
        if (Iteration < 0 || Iteration > QuantidadeIteracoes || !double.IsFinite(Elapsed) || Elapsed < 0)
            throw new ArgumentException("Estado da simulação inválido.");
        ValidateBodies(Corpos);

        /*  Cria um novo Universo com os corpos e parâmetros validados. */
        var u = new Universo { QuantidadeIteracoes = QuantidadeIteracoes, TempoEntreIteracoes = TempoEntreIteracoes };
        foreach (var c in Corpos) u.AdicionarCorpo(c);
        return u;
    }

    /* Valida os corpos do universo, garantindo que todos os parâmetros estejam dentro dos limites aceitáveis. */
    public static void ValidateBodies(List<Corpo>? bodies)
    {
        /* Aqui é pra lançar um aviso que deve ter pelo menos um corpo */
        if (bodies is null || bodies.Count < 1) throw new ArgumentException("O universo deve ter pelo menos um corpo.");
        foreach (var c in bodies)
        {
            /* Valida o nome do corpo, garantindo que não seja nulo, vazio, maior que 60 caracteres ou contenha ponto e vírgula. */
            if (c is null || string.IsNullOrWhiteSpace(c.Nome) || c.Nome.Length > 60 || c.Nome.IndexOfAny([';', '\r', '\n']) >= 0)
                throw new ArgumentException("Nome de corpo inválido (até 60 caracteres, sem ponto e vírgula).");

            /* Valida a massa e densidade do corpo, garantindo que estejam dentro dos limites aceitáveis. */
            if (!double.IsFinite(c.Massa) || c.Massa <= 0 || c.Massa > 1e35 || !double.IsFinite(c.Densidade) || c.Densidade <= 0 || c.Densidade > 1e18)
                throw new ArgumentException("Massa ou densidade fora do intervalo permitido.");

            /* Valida o raio do corpo, garantindo que seja finito e positivo. */
            if (!double.IsFinite(c.Raio) || c.Raio <= 0)
                throw new ArgumentException("A massa e a densidade produzem um raio fora do intervalo numérico.");

            /* Valida a posição e velocidade do corpo, garantindo que estejam dentro dos limites aceitáveis. */
            if (new[] { c.PosX, c.PosY, c.VelX, c.VelY }.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1e20))
                throw new ArgumentException("A simulação ultrapassou o intervalo numérico. Reduza o passo de tempo ou ajuste os corpos.");
        }
    }
}