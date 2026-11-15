/* Modelo Físico: */

CREATE TABLE Simulacao (
    id_simulacao INT PRIMARY KEY AUTO_INCREMENT,
    nome VARCHAR(255) NOT NULL,
    data_criacao DATETIME DEFAULT CURRENT_TIMESTAMP,
    qtd_corpos INT NOT NULL,
    qtd_iteracoes INT NOT NULL,
    tempo_entre_iteracoes DOUBLE NOT NULL
);

CREATE TABLE Corpo (
    id_corpo INT PRIMARY KEY AUTO_INCREMENT,
    id_simulacao INT,
    nome VARCHAR(255),
    massa DOUBLE,
    densidade DOUBLE
);

CREATE TABLE EstadoCorpo (
    id_registro BIGINT PRIMARY KEY AUTO_INCREMENT,
    tempo_decorrido DOUBLE,
    pos_x DOUBLE,
    pos_y DOUBLE,
    vel_x DOUBLE,
    vel_y DOUBLE,
    forca_x DOUBLE,
    forca_y DOUBLE,
    aceleracao_x DOUBLE,
    aceleracao_y DOUBLE,
    num_passo INT,
    corpo_id INT
);
 
ALTER TABLE Corpo ADD CONSTRAINT FK_Corpo_3
    FOREIGN KEY (id_simulacao)
    REFERENCES Simulacao (id_simulacao)
    ON DELETE CASCADE;
 
ALTER TABLE EstadoCorpo ADD CONSTRAINT FK_EstadoCorpo_2
    FOREIGN KEY (corpo_id)
    REFERENCES Corpo (id_corpo)
    ON DELETE CASCADE;
    
CREATE UNIQUE INDEX idx_estado_corpo_passo ON EstadoCorpo (corpo_id, num_passo);