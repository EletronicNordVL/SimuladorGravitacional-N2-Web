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
   
/* 6) Criação de IndiceS de Performance para otimizar a leitura do replay e limpeza automática */
CREATE UNIQUE INDEX idx_estado_corpo_passo ON EstadoCorpo(corpo_id, num_passo);
CREATE INDEX idx_simulacao_data_criacao ON Simulacao(data_criacao);

/* 6.1) Criação do índice composto para otimizar o SELECT do Replay por corpo e número do passo */
CREATE INDEX idx_estado_corpo_passo ON EstadoCorpo (corpo_id, num_passo);

/* 7) Lista de Selects */
/* A) Ver a simulação criada */
SELECT * FROM Simulacao;

/* A.1) Ver a última iteração da Simulção */
SELECT *
FROM Simulacao
where id_simulacao = '56';

/* B) Ver os corpos da simulação */
SELECT * FROM Corpo;

/* B.1) Ver o Estado dos Corpos presentes da simulação */
SELECT * FROM EstadoCorpo;

/* C) Ver os primeiros passos do histórico gravados */
SELECT * FROM EstadoCorpo LIMIT 15;

/* C.1) Uma consulta da contagem do total de estados dos corpos */
SELECT COUNT(*) AS TotalEstados FROM EstadoCorpo;
SELECT COUNT(*) FROM EstadoCorpo;

/* C.2) Ver o total de corpos e o total de seus estados */
SELECT ec.corpo_id, c.nome, COUNT(*) AS total_estados FROM EstadoCorpo ec INNER JOIN Corpo c ON c.id_corpo = ec.corpo_id
GROUP BY ec.corpo_id, c.nome ORDER BY ec.corpo_id;

/* C.3) Consultar Iterações
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

/* C.4) Select Alternativo de encontrar última iteração */
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

/* D) Comando para apagar a simulação e/ou simulações por meio do ID */
/* DELETE FROM Simulacao WHERE id_simulacao = 9;
DELETE FROM Simulacao WHERE id_simulacao = 10;
DELETE FROM Simulacao WHERE id_simulacao = 11;
DELETE FROM Simulacao WHERE id_simulacao = 12;
DELETE FROM Simulacao; */

/* E) Comando para apagar todas as simulações */
/* DELETE FROM Simulacao; */

/* F) Comando para uma consulta de um EstadoCorpo utilizando o Explain Select */
EXPLAIN SELECT * FROM EstadoCorpo WHERE corpo_id = 1 AND num_passo = 50;

/* F.1) Ter a descrição completo dos Corpos e seus estados */
DESCRIBE EstadoCorpo;
DESCRIBE Corpo;

/* F.2) Um Select para testar o Replay */
SELECT COUNT(*) FROM EstadoCorpo WHERE corpo_id IN (SELECT id_corpo FROM Corpo WHERE id_simulacao = 19);

/* G) Adicionar o 'Stored Procedure' para remover registros com mais de 1 mês de antiguidade */
DROP PROCEDURE IF EXISTS sp_limpar_simulacoes_antigas;
DELIMITER $$
CREATE PROCEDURE sp_limpar_simulacoes_antigas()
BEGIN
    DELETE FROM Simulacao
    WHERE data_criacao < DATE_SUB(NOW(), INTERVAL 1 MONTH);
END $$
DELIMITER ;

/* G.1) Executar a limpeza automaticamente uma vez por dia */
CREATE EVENT IF NOT EXISTS ev_limpar_simulacoes_antigas
ON SCHEDULE EVERY 1 DAY
STARTS CURRENT_TIMESTAMP + INTERVAL 1 DAY
DO CALL sp_limpar_simulacoes_antigas();

/* 6.2) Verificar se o Agendador está ligado */
SHOW VARIABLES LIKE 'event_scheduler';
SHOW EVENTS FROM simulador_gravitacional;

/* 6.3) Se o agendador estiver desligado (OFF), há opção de ativar o agendador */
SET GLOBAL event_scheduler = ON;