using System.Globalization;
using SimuladorGravitacional_N2;
using MySql.Data.MySqlClient;
using System.Collections.Concurrent;
using TrabalhoN2;

/* O namespace SimuladorGravitacional_N2 contém classes relacionadas à simulação gravitacional, 
 * incluindo a classe ConexaoBD para gerenciar a conexão com o banco de dados MySQL. */
var gravadoresAtivos = new ConcurrentDictionary<int, GravadorUniversoBD>();

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
    response.Headers.CacheControl = "no-store";
    return new { status = "ok", application = "TrabalhoN1", storagePath = storage, processId = Environment.ProcessId };
});

/*  * Rota para criar um novo universo com corpos aleatórios. */
app.MapPost("/api/universes/create", (CreateRequest request) =>
{
    /* Valida se a requisição contém um número válido de corpos para criar. */
    if (request.Count < 1) throw new ArgumentException("A quantidade de corpos deve ser maior que zero.");
    var universe = new Universo { QuantidadeIteracoes = 10000, TempoEntreIteracoes = 3600 };

    /* Gera corpos aleatórios no universo e calcula as forças entre eles. */
    universe.GerarCorposAleatorios(request.Count);
    universe.CalcularForcas();
    return Snapshot.From(universe, 0, 0);
});

/* Rota para avançar a simulação em um número de passos. */
app.MapPost("/api/simulation/step", (StepRequest request) =>
{
    /* Valida se o estado do universo na requisição não é nulo. */
    ArgumentNullException.ThrowIfNull(request.State);

    /* Converte o estado do universo recebido na requisição para um objeto Universo. */
    var universe = request.State.ToUniverse();
    if (request.Steps < 1 || request.Steps > 100) throw new ArgumentException("Quantidade de passos inválida.");

    /* Calcula o número de passos a serem executados, garantindo que não ultrapasse o número total de iterações do universo. */
    var steps = Math.Min(request.Steps, universe.QuantidadeIteracoes - request.State.Iteration);

    /* Se o universo está sendo gravado (IdSimulacao > 0), o gravador retoma a simulação que a tela criou antes
     * da primeira iteração. Os corpos são recriados a cada requisição, então o gravador refaz o mapa de IDs. */
    using var gravador = request.State.IdSimulacao > 0 ? new GravadorUniversoBD() : null;
    gravador?.RetomarSimulacao(request.State.IdSimulacao, universe.Corpos, request.State.Iteration);

    /* O estado pode ter sido editado na tela (massa, posição...): recalcula forças e acelerações antes do primeiro passo. */
    universe.CalcularForcas();
    for (var i = 0; i < steps; i++)
    {
        /* Avança uma iteração (posições Velocity Verlet, colisões e forças), com subpassos se houver corpos próximos. */
        universe.AvancarIteracao(universe.TempoEntreIteracoes);
        ValidateBodies(universe.Corpos);
    }

    /* O estado pode ter sido editado na tela (massa, posição...): recalcula forças e acelerações antes do primeiro passo. */
    universe.CalcularForcas();
    for (var i = 0; i < steps; i++)
    {
        /* Avança uma iteração (posições Velocity Verlet, colisões e forças), com subpassos se houver corpos próximos. */
        universe.AvancarIteracao(universe.TempoEntreIteracoes);
        ValidateBodies(universe.Corpos);

        /* Grava no banco o estado de TODOS os corpos nesta iteração (posição, velocidade, força e aceleração). */
        gravador?.GravarPasso(
            request.State.Iteration + i + 1,
            request.State.Elapsed + (i + 1) * universe.TempoEntreIteracoes,
            universe.Corpos);
    }

    /* Grava o que sobrou no buffer e fecha a conexão de gravação. */
    gravador?.FinalizarSimulacao();

    /* Retorna o estado atualizado do universo após os passos executados (as forças já estão atualizadas).
     * O IdSimulacao volta na resposta para que a tela continue gravando na mesma simulação. */
    return Snapshot.From(universe, request.State.Iteration + steps,
        request.State.Elapsed + steps * universe.TempoEntreIteracoes,
        request.State.IdSimulacao);
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
        gravador.GravarPasso(0, 0, universe.Corpos);
        gravador.FinalizarSimulacao();
    }
    catch
    {
        /* Se a gravação do passo 0 falhar, remove a simulação criada para não deixar um registro vazio no banco. */
        try { gravador.ExcluirSimulacao(id); } catch { /* melhor esforço: o erro original é o que importa */ }
        throw;
    }
    return Results.Ok(new { id, name });
});

/*  Rota para carregar um universo salvo no MySQL. Carrega o passo 0 (condições iniciais) da simulação com o ID informado. */
app.MapGet("/api/universes/{id:int}", (int id) =>
{
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

/*  Rota para excluir uma simulação do MySQL. As linhas de Corpo e EstadoCorpo são removidas em cascata (ON DELETE CASCADE). */
app.MapDelete("/api/universes/{id:int}", (int id) =>
{
    using var gravador = new GravadorUniversoBD();

    /* Retorna 204 (No Content) se a simulação foi excluída ou 404 (Not Found) se ela não existir. */
    return gravador.ExcluirSimulacao(id)
        ? Results.NoContent()
        : Results.NotFound(new { error = "Simulação não encontrada." });
});

/*  Rota que retorna os dados gerais de uma simulação gravada para o replay como o nome, o último passo gravado e o estado
 *  inicial (passo 0), que traz os corpos com nome, massa e densidade. Os passos seguintes vêm da rota /frames. */
app.MapGet("/api/replay/{id:int}", (int id) =>
{
    using var gravador = new GravadorUniversoBD();

    var resumo = gravador.ListarSimulacoes().FirstOrDefault(simulacao => simulacao.Id == id);
    if (resumo is null) return Results.NotFound(new { error = "Simulação não encontrada." });

    var universe = new Universo { QuantidadeIteracoes = resumo.QuantidadeIteracoes, TempoEntreIteracoes = resumo.TempoEntreIteracoes };
    foreach (var corpo in gravador.CarregarPasso(id, 0)) universe.AdicionarCorpo(corpo);

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

    using var gravador = new GravadorUniversoBD();
    return Results.Ok(gravador.CarregarQuadros(id, inicio, quantidade));
});

/*  Rota para exportar o estado atual do universo em um arquivo de texto. */
app.MapPost("/api/universes/export", (Snapshot state) =>
{
    /* Converte o estado do universo recebido na requisição para um objeto Universo. */
    var universe = state.ToUniverse();

    /* Cria uma lista de linhas para o arquivo de exportação, começando com o cabeçalho contendo a quantidade de corpos, quantidade de iterações e tempo entre iterações. */
    var lines = new List<string> { $"{universe.Corpos.Count};{universe.QuantidadeIteracoes};{universe.TempoEntreIteracoes.ToString("R", CultureInfo.InvariantCulture)}" };

    /* Adiciona uma linha para cada corpo no universo, contendo os dados do corpo separados por ponto e vírgula. */
    lines.AddRange(universe.Corpos.Select(c => string.Join(';', c.Nome,
        c.Massa.ToString("R", CultureInfo.InvariantCulture), c.Densidade.ToString("R", CultureInfo.InvariantCulture),
        c.PosX.ToString("R", CultureInfo.InvariantCulture), c.PosY.ToString("R", CultureInfo.InvariantCulture),
        c.VelX.ToString("R", CultureInfo.InvariantCulture), c.VelY.ToString("R", CultureInfo.InvariantCulture))));

    /* Retorna o arquivo de exportação como um resultado de arquivo, com o conteúdo codificado em UTF-8 e o tipo MIME definido como "text/plain". */
    return Results.File(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines)), "text/plain; charset=utf-8", "universo_exportado.txt");
});

/*  Rota para importar um universo a partir de um arquivo de texto enviado no corpo da requisição. */
app.MapPost("/api/universes/import", async (HttpRequest request) =>
{
    /* Lê o conteúdo do corpo da requisição como uma string. */
    using var reader = new StreamReader(request.Body);
    var content = await reader.ReadToEndAsync();

    /* Valida se o tamanho do arquivo é maior que 100 KB. */
    if (content.Length > 100000) throw new ArgumentException("Arquivo muito grande (máximo 100 KB).");
    var lines = content.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /* Valida se o arquivo possui pelo menos duas linhas (uma para o cabeçalho e pelo menos uma para um corpo). */
    if (lines.Length < 2) throw new ArgumentException("Arquivo de universo inválido.");
    var header = lines[0].Trim().Split(';');

    /* Valida se o cabeçalho possui exatamente três campos (quantidade de corpos, quantidade de iterações e tempo entre iterações). */
    if (header.Length != 3) throw new ArgumentException("Cabeçalho inválido.");
    var count = int.Parse(header[0]);

    /* Valida se a quantidade de corpos é válida (entre 1 e 100) e se o número de linhas no arquivo corresponde à quantidade de corpos especificada. */
    if (count < 1 || lines.Length != count + 1) throw new ArgumentException("Quantidade de corpos inválida no arquivo.");
    var state = new Snapshot([], int.Parse(header[1]), Parse(header[2]), 0, 0);

    /* Itera sobre cada linha do arquivo (exceto o cabeçalho) para criar os corpos e adicioná-los ao estado do universo. */
    foreach (var line in lines.Skip(1))
    {
        /* Divide a linha em campos separados por ponto e vírgula e valida se há exatamente sete campos 
         * (nome, massa, densidade, posição X, posição Y, velocidade X, velocidade Y). */
        var fields = line.Trim().Split(';');
        if (fields.Length != 7) throw new ArgumentException("Cada corpo deve ter sete campos.");
        state.Corpos.Add(new Corpo(fields[0], Parse(fields[1]), Parse(fields[2]), Parse(fields[3]), Parse(fields[4]), Parse(fields[5]), Parse(fields[6])));
    }
    /* Converte o estado do universo para um objeto Universo, calcula as forças e retorna o estado atualizado. */
    var universe = state.ToUniverse();
    universe.CalcularForcas();
    return Snapshot.From(universe, 0, 0);
});

/* Inicia a aplicação web. */
app.Run();

/* Funções auxiliares. */
double Parse(string value) => double.Parse(value.Trim().Replace(',', '.'), CultureInfo.InvariantCulture);

/* Função para validar os corpos do universo, garantindo que todos os parâmetros estejam dentro dos limites aceitáveis. */
static void ValidateBodies(List<Corpo> bodies) => Snapshot.ValidateBodies(bodies);

/* Classes de requisição para a API. */
record CreateRequest(int Count);

/* Classe de requisição para avançar a simulação em um número de passos. */
record StepRequest(Snapshot State, int Steps);

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
        if (QuantidadeIteracoes < 1 || QuantidadeIteracoes > 1000000) throw new ArgumentException("Use de 1 a 1.000.000 iterações.");

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