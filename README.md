# Simulador Gravitacional 2D — Versão Web (N2)

> Projeto final interativo desenvolvido para as disciplinas de **Programação Avançada de Sistemas (PAS)** e **Banco de Dados II (BD II)** no curso de **Tecnólogo em Análise e Desenvolvimento de Sistemas (TADS)**.

## 📌 Sobre o Projeto

O **Simulador Gravitacional 2D (Versão Web N2)** é a aplicação final e completa do sistema de simulação de mecânica celeste e gravitação universal.

Nesta versão **Web**, o sistema une o motor físico e transacional de alta performance em **C# (.NET)** — com computação paralela (`Parallel.For`), cronometria (`Stopwatch`) e banco de dados **MySQL (3ª Forma Normal)** — com a interface gráfica web interativa em **HTML5 Canvas e JavaScript**, permitindo a visualização em tempo real das trajetórias gravitacionais, replay de simulações salvas e exibição das métricas de desempenho.

## 🚀 Principais Funcionalidades da Versão Web

1. **Interface Gráfica Interativa (HTML5 Canvas 2D & JS)**:
   - Renderização fluida de corpos celestes, vetores de movimento e trajetórias de órbita.
   - Controles visuais de simulação: alternância de velocidade, zoom, pausa e reinício.
   - Interruptor **Processamento paralelo (N2)**: desligado, faz os mesmos cálculos em uma única thread, para comparar o desempenho com a versão N1.

2. **Backend API RESTful em C# (ASP.NET Core)**:
   - `POST /api/simulation/start`: Começa uma simulação no servidor a partir do universo da tela. Se a gravação estiver ligada, cria a simulação no MySQL e grava o passo 0.
   - `POST /api/simulation/{id}/step`: Avança N iterações da simulação guardada no servidor e grava cada uma no MySQL (em segundo plano).
   - `POST /api/simulation/{id}/stop`: Encerra a simulação e espera todos os passos chegarem ao banco.
   - `POST /api/universes/save`: Grava o universo atual no MySQL (passo 0) e devolve o `id`.
   - `GET /api/universes`: Lista as simulações armazenadas no MySQL.
   - `GET /api/replay/{id}`: Dados gerais, último passo gravado e estado inicial de uma simulação.
   - `GET /api/replay/{id}/frames?inicio=&quantidade=`: Bloco de passos gravados, para o *Replay* frame a frame no navegador.

3. **Computação Paralela (`Parallel.For`)**:
   - O cálculo das forças entre todos os pares de corpos é dividido entre as threads. Cada thread soma em uma lista própria, juntada no final com `lock`, então cada par é calculado uma vez e não há condição de corrida.
   - A procura por corpos que colidiram também é feita em paralelo; as fusões são feitas depois, em ordem, e conservam massa e quantidade de movimento.
   - A posição é atualizada pelo método de Euler semi-implícito.
   - A gravação no MySQL roda em uma thread separada, em lotes de até 500 linhas por `INSERT`.

4. **Persistência Relacional em MySQL (3ª Forma Normal)**:
   - Estrutura relacional normalizada: `Simulacao` (Metadados), `Corpo` (Propriedades físicas) e `EstadoCorpo` (Histórico temporal).
   - Gerenciamento com **`MySqlTransaction`** para garantir a atomicidade das gravações via ADO.NET.
   - Integridade referencial com **`ON DELETE CASCADE`** para exclusão segura de simulações.
   - **Índice Composto**: `idx_estado_corpo_passo (corpo_id, num_passo)` (índice **único**, que também impede passos duplicados)

5. **Stored Procedure de Limpeza Automática**:
   - Procedure `sp_limpar_simulacoes_antigas` para expurgo automático de registros com mais de 30 dias de criação.

## 🗄️ Modelagem e Arquitetura do Banco de Dados (MySQL)

O esquema relacional foi projetado em **3ª Forma Normal (3FN)**:

```
+------------------------------------+
|             Simulacao              |
+------------------------------------+
| PK  id_simulacao (INT, AUTO_INC)   |
|     nome (VARCHAR)                 |
|     data_criacao (DATETIME)        |
|     qtd_corpos (INT)               |
|     qtd_iteracoes (INT)            |
|     tempo_entre_iteracoes (DOUBLE) |
+------------------------------------+
                  | 1
                  |
                  | N
+------------------------------------+
|               Corpo                |
+------------------------------------+
| PK  id_corpo (INT, AUTO_INC)       |
| FK  id_simulacao (INT) ------------+--> ON DELETE CASCADE
|     nome (VARCHAR)                 |
|     massa (DOUBLE)                 |
|     densidade (DOUBLE)             |
+------------------------------------+
                  | 1
                  |
                  | N
+------------------------------------+
|            EstadoCorpo             |
+------------------------------------+
| PK  id_registro (BIGINT, AUTO_INC) |
| FK  corpo_id (INT) ----------------+--> ON DELETE CASCADE
|     num_passo (INT)                |
|     tempo_decorrido (DOUBLE)       |
|     nome (VARCHAR)                 |
|     massa, densidade (DOUBLE)      |
|     pos_x, pos_y (DOUBLE)          |
|     vel_x, vel_y (DOUBLE)          |
|     forca_x, forca_y (DOUBLE)      |
|     aceleracao_x, aceleracao_y     |
+------------------------------------+
```

### Script SQL da Stored Procedure de Limpeza
```sql
USE simulador_gravitacional;

DELIMITER //

CREATE PROCEDURE sp_limpar_simulacoes_antigas()
BEGIN
    -- Remove simulações com mais de 1 mês de antiguidade
    -- O ON DELETE CASCADE limpa automaticamente Corpo e EstadoCorpo
    DELETE FROM Simulacao 
    WHERE data_criacao < DATE_SUB(NOW(), INTERVAL 1 MONTH);
END //

DELIMITER ;
```

## 📐 Estudo de Volumetria (BD II)

- **Carga Diária Estimada**: 50 simulações/dia com 400 corpos e 100 iterações (mais o passo 0).
- **Registros em `EstadoCorpo`**: 50 x 400 x 101 = **2.020.000 registros/dia**.
- **Consumo Estimado de Disco**: 195 bytes por registro (medido no MySQL, com índices) ➔ ~394 MB/dia ➔ **~11,8 GB/mês**.
- Detalhes no relatório `05 - DOCUMENTAÇÃO/RELATORIO_COMPARATIVO_N1_N2` (Word e PDF).
- **Justificativa Técnica**: Valida a chave primária `BIGINT` na tabela `EstadoCorpo`, a otimização com o índice composto `idx_estado_corpo_passo` e a necessidade da Stored Procedure para expurgo periódico.

## 💻 Como Executar a Aplicação Web

### Pré-requisitos
1. **.NET SDK 10.0** instalado na máquina.
2. Servidor **MySQL 8.0+** (ou MariaDB, como o do XAMPP) ativo.

### Passo a Passo

1. **Clonar o Repositório Web**:
   - Repositório: `SimuladorGravitacional-N2-Web`

2. **Configurar o Banco de Dados no MySQL**:
   - Abra o MySQL Workbench e execute o script DDL para criar o banco `simulador_gravitacional`, as tabelas e a Stored Procedure.

3. **Configurar a Conexão** (a senha NUNCA vai no código nem no repositório):
   - Defina a variável de ambiente `SIMULADOR_DB_CONNECTION` e reinicie o terminal/Visual Studio. No PowerShell:
     `setx SIMULADOR_DB_CONNECTION "Server=localhost;Port=3306;Database=simulador_gravitacional;User ID=SEU_USUARIO;Password=SUA_SENHA;"`

4. **Iniciar a Aplicação Web**:
   - Dê dois cliques em `04 - CÓDIGO FINAL/SimuladorGravitacional-N2-Web/Iniciar.cmd`. Ele compila o projeto, inicia o servidor em segundo plano e abre o navegador em `http://localhost:5080`.
   - Para encerrar o servidor, use `Parar.cmd`.
   - Também é possível rodar com `dotnet run` dentro da pasta do projeto.

## 🔗 Repositórios do Projeto

* 🖥️ **Versão Console (Benchmarking & Testes)**: `SimuladorGravitacionalN2-Console`
* 🌐 **Versão Web (Aplicação Final Interativa)**: `SimuladorGravitacional-N2-Web`

## 👨‍💻 Autores e Contexto Acadêmico

Trabalho desenvolvido para as avaliações **N2** das disciplinas de:
* **Programação Avançada de Sistemas (PAS)**
* **Banco de Dados II (BD II)**

Curso: **Tecnólogo em Análise e Desenvolvimento de Sistemas (TADS)**.
