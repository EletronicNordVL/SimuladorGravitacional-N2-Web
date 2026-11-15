Simulador Gravitacional 2D — Versão Web (N2)

Documentação oficial do repositório da versão Web (SimuladorGravitacional-N2-Web), desenvolvida para as disciplinas de Programação Avançada de Sistemas (PAS) e Banco de Dados II (BD II) no curso de Tecnólogo em Análise e Desenvolvimento de Sistemas (TADS).

📌 Sobre o Projeto

O Simulador Gravitacional 2D (Versão Web N2) é a solução final interativa do projeto de mecânica celeste e gravitação universal. Enquanto a aplicação em console atua como o motor de desenvolvimento, testes e benchmarking de CPU, a interface Web representa a camada de visualização do sistema ("os olhos"), oferecendo uma experiência gráfica fluida diretamente no navegador.

Nesta versão N2, a aplicação integra três componentes fundamentais:

1. Frontend Web Interativo (HTML5 Canvas 2D + JavaScript): Interface gráfica com renderização das trajetórias orbitais, posições dos corpos celestes e controles de animação e reprodução (Replay).
2. Backend API REST em C# (.NET Core): Servidor HTTP que executa o cálculo gravitacional paralelizado via Parallel.For e mede o tempo de processamento com Stopwatch.
3. Persistência Relacional em MySQL (3ª Forma Normal): Armazenamento permanente de simulações, corpos e históricos por meio de transações ADO.NET, com índices otimizados e rotina automatizada de expurgo.

🚀 Principais Funcionalidades da Versão Web

1. Visualização Gráfica Interativa (HTML5 Canvas 2D)

* Animação em Tempo Real: Renderização das partículas celestes no elemento Canvas, ajustando raio proporcionalmente à massa e desenhando os vetores de movimento.
* Painel de Controle de Simulação: Campos para definir quantidade de corpos, iterações e intervalo de tempo por passo.
* Controles de Reprodução: Botões para iniciar, pausar, reiniciar e controlar a velocidade do Replay.

2. Computação Paralela no Backend (Parallel.For)

* Processamento Multinúcleo: Aceleração dos cálculos de força resultante entre todos os pares de corpos celestes via Parallel.For.
* Execução Thread-Safe: Acúmulo de forças isolado por corpo a cada passo temporal para impedir condições de corrida (race conditions).
* Integração Física: Cálculo simultâneo de posições, velocidades e acelerações (MRUV) com tratamento de colisões elásticas.

3. Exibição do Tempo de Execução (Stopwatch)

* Métricas Visuais de Desempenho: O tempo exato gasto pelo motor físico paralelizado é medido pelo Stopwatch no servidor C# e retornado em milissegundos para a interface.
* Feedback Transparente: O usuário visualiza diretamente no painel web o ganho de velocidade proporcionado pela paralelização.

4. Replay de Simulações Gravadas no MySQL

* Listagem Dinâmica: A página web consulta o endpoint da API para preencher a lista de universos salvos no MySQL.
* Reprodução Histórica: Ao selecionar um registro, a aplicação solicita os passos da tabela EstadoCorpo e anima o movimento quadro a quadro.

🌐 Arquitetura da API RESTful (C# ASP.NET Core)

A integração entre o frontend Web e o backend C# ocorre via rotas RESTful formatadas em JSON:

* POST /api/simular: Recebe os parâmetros, executa a simulação paralelizada (Parallel.For), grava no MySQL e retorna o tempo (Stopwatch) e o estado inicial.
* GET /api/simulacoes: Consulta o banco MySQL e retorna a lista JSON de todas as simulações cadastradas.
* GET /api/simulacao/{id}: Busca os dados completos e todo o histórico temporal (EstadoCorpo) para reprodução do Replay no Canvas.

🗄️ Modelagem e Arquitetura do Banco de Dados (MySQL)

O esquema relacional do banco de dados simulador_gravitacional está normalizado em 3ª Forma Normal (3FN):

* Simulacao (Tabela Pai): Armazena metadados como id_simulacao (PK), nome, data_criacao, qtd_corpos, qtd_iteracoes e tempo_entre_iteracoes.
* Corpo (Tabela Filho): Registra atributos fixos dos corpos como id_corpo (PK), id_simulacao (FK com ON DELETE CASCADE), nome, massa e densidade.
* EstadoCorpo (Tabela Neto): Histórico completo com id_registro (PK BIGINT), corpo_id (FK com ON DELETE CASCADE), num_passo, posições, velocidades, forças e acelerações em X e Y.

Recursos de Desempenho e Manutenção (BD II)

* Índice Composto: idx_estado_corpo_passo na tabela EstadoCorpo(corpo_id, num_passo) para agilizar a leitura dos passos de Replay.
* Stored Procedure de Expurgo (sp_limpar_simulacoes_antigas): Remove registros com mais de 30 dias de criação para manter o volume sob controle.

📐 Estudo de Volumetria (BD II)

* Carga Estimada: 50 simulações/dia x 400 corpos x 100 iterações = 2.000.000 registros/dia na tabela EstadoCorpo.
* Armazenamento em Disco: ~100 bytes/registro = 200 MB/dia -> ~6,0 GB/mês.
* Aplicação Prática: Justifica o uso de BIGINT para a chave primária de EstadoCorpo, o uso do índice composto e a limpeza periódica via Stored Procedure.

🏗️ Estrutura de Arquivos do Repositório Web

* Web/wwwroot/index.html: Estrutura HTML5 da interface gráfica
* Web/wwwroot/styles.css: Estilização e layout responsivo
* Web/wwwroot/app.js: Lógica de renderização 2D, animação e consumo da API
* Web/wwwroot/icon.svg: Ícone da aplicação
* Web/Program.cs: Configuração do ASP.NET Core e endpoints REST
* Web/Web.csproj: Arquivo de projeto .NET Web
* Universo.cs: Motor físico paralelizado (Parallel.For) e cronometrado (Stopwatch)
* Corpo.cs: Modelo do corpo celeste
* GravadorUniversoBD.cs: DAO transacional ADO.NET (MySQL)
* ConexaoBD.cs: Gerenciador da string de conexão MySQL
* README.md: Documentação oficial do projeto

💻 Como Executar o Projeto Web

Pré-requisitos

1. .NET SDK 10.0 (ou .NET 8.0+) instalado.
2. Servidor MySQL Server 8.0+ em execução local.
3. Navegador Web moderno.

Passo a Passo

1. Clonar o Repositório: Obtenha o código do repositório oficial da versão Web.
2. Criar a Estrutura no MySQL: Execute o script DDL no MySQL Workbench para criar o banco simulador_gravitacional, tabelas, índice composto e a Stored Procedure.
3. Configurar Conexão: Ajuste os dados do MySQL no arquivo ConexaoBD.cs.
4. Executar o Servidor Web C#: Entre na pasta Web e execute dotnet run.
5. Acessar a Aplicação: Abra a URL local indicada no terminal no seu navegador.

👨‍💻 Autores e Contexto Acadêmico

Projeto desenvolvido para as avaliações N2 das disciplinas de:

* Programação Avançada de Sistemas (PAS)
* Banco de Dados II (BD II)

Curso: Tecnólogo em Análise e Desenvolvimento de Sistemas (TADS).
