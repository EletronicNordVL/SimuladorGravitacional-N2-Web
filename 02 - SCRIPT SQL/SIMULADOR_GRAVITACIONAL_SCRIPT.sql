/* 1) Criação e Seleção do Banco de Dados */
/* DROP DATABASE IF EXISTS simulador_gravitacional; */
CREATE DATABASE IF NOT EXISTS simulador_gravitacional; 
USE simulador_gravitacional;

/* 2) Tabela de Simulacao */
CREATE TABLE Simulacao (
  id_simulacao INT PRIMARY KEY AUTO_INCREMENT,
  nome VARCHAR(255) NOT NULL,
  data_criacao DATETIME DEFAULT CURRENT_TIMESTAMP,
  qtd_corpos INT NOT NULL, 
  qtd_iteracoes INT NOT NULL, 
  tempo_entre_iteracoes DOUBLE NOT NULL
);

/* 3) Tabela de Corpos */
CREATE TABLE Corpo (
  id_corpo INT PRIMARY KEY AUTO_INCREMENT,
  id_simulacao INT NOT NULL,
  nome VARCHAR(255) NOT NULL,
  massa DOUBLE NOT NULL, 
  densidade DOUBLE NOT NULL
);

/* 4) Tabela de EstadoCorpo */
CREATE TABLE EstadoCorpo (
  id_registro BIGINT PRIMARY KEY AUTO_INCREMENT,
  corpo_id INT NOT NULL, 
  num_passo INT NOT NULL,
  tempo_decorrido DOUBLE NOT NULL,
  nome VARCHAR(255) NOT NULL,
  massa DOUBLE NOT NULL,
  densidade DOUBLE NOT NULL,
  pos_x DOUBLE NOT NULL,
  pos_y DOUBLE NOT NULL, 
  vel_x DOUBLE NOT NULL,
  vel_y DOUBLE NOT NULL, 
  forca_x DOUBLE NOT NULL, 
  forca_y DOUBLE NOT NULL,
  aceleracao_x DOUBLE NOT NULL,
  aceleracao_y DOUBLE NOT NULL
);

/* 5) Definição das Chaves Estrangeiras (FKs) */
ALTER TABLE Corpo 
   ADD CONSTRAINT FK_Corpo_Simulacao 
   FOREIGN KEY (id_simulacao) 
   REFERENCES Simulacao (id_simulacao) 
   ON DELETE CASCADE; 

ALTER TABLE EstadoCorpo 
   ADD CONSTRAINT FK_EstadoCorpo_Corpo 
   FOREIGN KEY (corpo_id) 
   REFERENCES Corpo (id_corpo) 
   ON DELETE CASCADE;
   
/* SEÇÃO DOS ÍNDICES */
/* 6) Índice único para otimizar o replay por corpo e número do passo. */
CREATE UNIQUE INDEX idx_estado_corpo_passo ON EstadoCorpo(corpo_id, num_passo);

/* 6.1) Índice para otimizar a busca de simulações antigas. */
CREATE INDEX idx_simulacao_data_criacao ON Simulacao(data_criacao);

/* SEÇÃO DOS SELECTS */
/* 7) Ver a simulação criada */
SELECT * FROM Simulacao;

/* 7.1) Ver a última iteração da Simulção */
SELECT *
FROM Simulacao
where id_simulacao = '56';

/* 7.2) Ver os corpos da simulação */
SELECT * FROM Corpo;

/* 7.3) Ver o Estado dos Corpos presentes da simulação */
SELECT * FROM EstadoCorpo;

/* 7.4) Ver os primeiros passos do histórico gravados */
SELECT * FROM EstadoCorpo LIMIT 15;

/* 7.5) Uma consulta da contagem do total de estados dos corpos */
SELECT COUNT(*) AS TotalEstados FROM EstadoCorpo;
SELECT COUNT(*) FROM EstadoCorpo;

/* 7.6) Ver o total de corpos e o total de seus estados */
SELECT ec.corpo_id, c.nome, COUNT(*) AS total_estados FROM EstadoCorpo ec INNER JOIN Corpo c ON c.id_corpo = ec.corpo_id
GROUP BY ec.corpo_id, c.nome ORDER BY ec.corpo_id;

/* 7.7) Consultar Iterações
/* Informe o ID da simulação desejada e consultar a última iteração gravada de uma simulação */
SELECT * FROM Simulacao;
SET @id_simulacao = 54;
SELECT ec.*
FROM EstadoCorpo ec
INNER JOIN Corpo c ON c.id_corpo = ec.corpo_id
WHERE c.id_simulacao = @id_simulacao
AND ec.num_passo = (
    SELECT MAX(ec2.num_passo)
    FROM EstadoCorpo ec2
    INNER JOIN Corpo c2 ON c2.id_corpo = ec2.corpo_id
    WHERE c2.id_simulacao = @id_simulacao
);

/* 7.8) Select Alternativo de encontrar última iteração */
/* Verifica as últimas simulações criadas. */
SELECT
    s.id_simulacao,
    s.nome,
    s.qtd_corpos,
    COUNT(DISTINCT ec.num_passo) AS total_iteracoes_gravadas,
    MAX(ec.num_passo) AS ultima_iteracao
FROM Simulacao s
LEFT JOIN Corpo c
    ON c.id_simulacao = s.id_simulacao
LEFT JOIN EstadoCorpo ec
    ON ec.corpo_id = c.id_corpo
GROUP BY s.id_simulacao, s.nome, s.qtd_corpos
ORDER BY s.id_simulacao DESC
LIMIT 10;

/* 7.9) Conferir se existe Simulações Antigas e sua data de criação 
dentro do Banco de Dados */
SELECT
    id_simulacao,
    nome,
    data_criacao
FROM Simulacao
WHERE data_criacao < DATE_SUB(NOW(), INTERVAL 1 MONTH)
ORDER BY data_criacao;

/* 7.9.1) Select para conferir o registro mais antigo de uma simulação */
SELECT
    COUNT(*) AS simulacoes_antigas,
    MIN(data_criacao) AS registro_mais_antigo
FROM Simulacao
WHERE data_criacao < DATE_SUB(NOW(), INTERVAL 1 MONTH);

/* 7.10) Select para testar o Replay */
SELECT COUNT(*) 
FROM EstadoCorpo WHERE corpo_id 
IN (SELECT id_corpo FROM Corpo WHERE id_simulacao = 19);

/* SEÇÃO DOS DELETES */
/* 8) Comando para apagar a simulação e/ou simulações por meio do ID */
/* DELETE FROM Simulacao WHERE id_simulacao = 9;
DELETE FROM Simulacao WHERE id_simulacao = 10;
DELETE FROM Simulacao WHERE id_simulacao = 11;
DELETE FROM Simulacao WHERE id_simulacao = 12;
DELETE FROM Simulacao; */

/* 8.1) Comando para apagar todas as simulações */
/* DELETE FROM Simulacao; */

/* SEÇÃO DE CONSULTAS COM EXPLAIN SELECT E DESCRIÇÕES*/
/* 9) Comando para uma consulta de um EstadoCorpo utilizando o Explain Select */
EXPLAIN SELECT * FROM EstadoCorpo WHERE corpo_id = 1 AND num_passo = 50;

/* 9.1) Ter a descrição completo dos Corpos e seus estados */
DESCRIBE EstadoCorpo;
DESCRIBE Corpo;

/* ADIÇÃO DO STORED PROCEDURE */
/* 10) Adicionar o 'Stored Procedure' para remover registros com mais de 1 mês de antiguidade */
DROP PROCEDURE IF EXISTS sp_limpar_simulacoes_antigas;
DELIMITER $$
CREATE PROCEDURE sp_limpar_simulacoes_antigas()
BEGIN
    DELETE FROM Simulacao
    WHERE data_criacao < DATE_SUB(NOW(), INTERVAL 1 MONTH);
END $$
DELIMITER ;

/* 10.1) Conferir a Stored Procedure */
SHOW CREATE PROCEDURE sp_limpar_simulacoes_antigas;

/* SEÇÃO DE LIMPEZA, AGENDADOR E EXISTÊNCIA DE EVENTOS */
/* 11) Criar um evento para executar a limpeza automaticamente uma vez por dia */
CREATE EVENT IF NOT EXISTS ev_limpar_simulacoes_antigas
ON SCHEDULE EVERY 1 DAY
STARTS CURRENT_TIMESTAMP + INTERVAL 1 DAY
DO CALL sp_limpar_simulacoes_antigas();

/* 11.2) Verificar se o Agendador está ligado */
SHOW VARIABLES LIKE 'event_scheduler';

/* 11.3) Conferir se o evento existe */
SHOW EVENTS FROM simulador_gravitacional;

/* 11.4) Se o agendador estiver desligado (OFF), há opção de ativar o agendador */
SET GLOBAL event_scheduler = ON;