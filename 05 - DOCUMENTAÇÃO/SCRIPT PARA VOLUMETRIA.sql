/* CONSULTAS SQL PARA O ESTUDO DE VOLUMETRIA */

/* 1) Uma consulta para ter informações sobre tamanho dos dois. No entanto, são estimativas, pois podem variar */
SELECT
    t.TABLE_NAME,
    t.ENGINE,
    t.ROW_FORMAT,
    (SELECT COUNT(*)
     FROM EstadoCorpo) AS REGISTROS_REAIS,
    t.TABLE_ROWS AS REGISTROS_ESTIMADOS,
    t.DATA_LENGTH,
    t.INDEX_LENGTH,
    t.DATA_FREE,
    ROUND(
        (t.DATA_LENGTH + t.INDEX_LENGTH) /
        (SELECT COUNT(*) FROM EstadoCorpo),
        2
    ) AS BYTES_POR_REGISTRO
FROM information_schema.TABLES t
WHERE t.TABLE_SCHEMA = 'simulador_gravitacional'
  AND t.TABLE_NAME = 'EstadoCorpo';

/* 2) Esse comando atualiza as estatísticas utilizadas pelo MySQL. Não apaga nem insere registros. */
ANALYZE TABLE EstadoCorpo;

/* 3) Conferir a estrutura dos índices que estão contribuindo para esse tamanho. */
SHOW INDEX FROM EstadoCorpo;

/* 4) Conferir a definição real da tabela */
SHOW CREATE TABLE EstadoCorpo;

/* 5) Verificar quantas simulações diferentes contribuíram para esses 8.551 registros. */
SELECT
    COUNT(*) AS TOTAL_REGISTROS,
    COUNT(DISTINCT c.id_simulacao) AS TOTAL_SIMULACOES,
    COUNT(DISTINCT ec.corpo_id) AS TOTAL_CORPOS,
    MIN(ec.num_passo) AS PRIMEIRO_PASSO,
    MAX(ec.num_passo) AS ULTIMO_PASSO
FROM EstadoCorpo ec
INNER JOIN Corpo c
    ON c.id_corpo = ec.corpo_id;

/* 6) Verificar a distribuição por simulação */
SELECT
    c.id_simulacao,
    COUNT(*) AS TOTAL_REGISTROS,
    COUNT(DISTINCT c.id_corpo) AS TOTAL_CORPOS,
    MIN(ec.num_passo) AS PRIMEIRO_PASSO,
    MAX(ec.num_passo) AS ULTIMO_PASSO
FROM EstadoCorpo ec
INNER JOIN Corpo c
    ON c.id_corpo = ec.corpo_id
GROUP BY c.id_simulacao
ORDER BY c.id_simulacao;

/* 7) Verificar a ocupação por corpo */
SELECT
    c.id_simulacao,
    COUNT(*) AS QUANTIDADE_CORPOS,
    MIN(contagem.total_estados) AS MENOR_HISTORICO,
    ROUND(AVG(contagem.total_estados), 2) AS MEDIA_HISTORICO,
    MAX(contagem.total_estados) AS MAIOR_HISTORICO
FROM (
    SELECT
        corpo_id,
        COUNT(*) AS total_estados
    FROM EstadoCorpo
    GROUP BY corpo_id
) contagem
INNER JOIN Corpo c
    ON c.id_corpo = contagem.corpo_id
GROUP BY c.id_simulacao
ORDER BY c.id_simulacao;

/* 8) Verificar o Total de Registros */
SELECT COUNT(*) AS TOTAL_REGISTROS
FROM EstadoCorpo;

/* 9) Consultar os registros necessários */
SELECT
    NAME,
    ROW_FORMAT,
    ROW_FORMAT AS FORMATO,
    SPACE,
    FLAG
FROM information_schema.INNODB_TABLES
WHERE NAME = 'simulador_gravitacional/estadocorpo';

/* 10) Consultar registros para ter acesso aos dados reais */
SELECT
    FILE_NAME,
    TABLESPACE_NAME,
    TOTAL_EXTENTS,
    EXTENT_SIZE,
    DATA_FREE
FROM information_schema.FILES
WHERE TABLESPACE_NAME = 'simulador_gravitacional/estadocorpo';