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
    id_simulacao INT NOT NULL,
    nome VARCHAR(255) NOT NULL,
    massa DOUBLE NOT NULL,
    densidade DOUBLE NOT NULL
);

CREATE TABLE EstadoCorpo (
    id_registro BIGINT PRIMARY KEY AUTO_INCREMENT,
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
    aceleracao_y DOUBLE NOT NULL,
    num_passo INT NOT NULL,
    corpo_id INT NOT NULL
);
 
ALTER TABLE Corpo ADD CONSTRAINT FK_Corpo_Simulacao
    FOREIGN KEY (id_simulacao)
    REFERENCES Simulacao (id_simulacao)
    ON DELETE CASCADE;
 
ALTER TABLE EstadoCorpo ADD CONSTRAINT FK_EstadoCorpo_Corpo
    FOREIGN KEY (corpo_id)
    REFERENCES Corpo (id_corpo)
    ON DELETE CASCADE;
    
CREATE UNIQUE INDEX idx_estado_corpo_passo ON EstadoCorpo (corpo_id, num_passo);
CREATE INDEX idx_simulacao_data_criacao ON Simulacao(data_criacao);