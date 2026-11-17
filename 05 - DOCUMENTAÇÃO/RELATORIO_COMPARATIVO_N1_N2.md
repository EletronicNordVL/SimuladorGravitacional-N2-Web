# Relatório comparativo dos projetos N1 e N2

## Objetivo

Avaliamos como as mudanças da N2 afetaram o tempo de processamento das simulações em relação à N1. Para isso, aumentamos a quantidade de corpos e de iterações e observamos em quais cenários a nova versão apresentou vantagem.

O principal resultado foi a redução de **35,18% no tempo de cálculo com 200 corpos e 1.000 iterações**. A vantagem apareceu nos cenários de 100 e 200 corpos; nos testes menores, a N1 teve melhor desempenho.

## Como realizamos os testes

Usamos as mesmas posições, velocidades e massas iniciais nas duas versões. Combinamos 10, 50, 100 e 200 corpos com 100, 500 e 1.000 iterações, formando 12 cenários.

Executamos cada cenário cinco vezes em cada versão, totalizando **120 medições**. Antes de medir, fizemos execuções de aquecimento para reduzir a influência da inicialização. Também alternamos a ordem das versões entre as repetições.

## Resultados obtidos

Os tempos estão em milissegundos (ms). **Quanto menor o tempo, melhor o desempenho.**

| Corpos | Iterações | N1: média em ms | N2: média em ms | Tempo da N2 em relação à N1 |
| --- | --- | --- | --- | --- |
| 10 | 100 | 0,19 | 1,60 | 725,51% mais tempo |
| 10 | 500 | 0,81 | 8,15 | 906,61% mais tempo |
| 10 | 1000 | 2,17 | 16,75 | 670,47% mais tempo |
| 50 | 100 | 4,51 | 4,85 | 7,52% mais tempo |
| 50 | 500 | 22,66 | 24,93 | 10,03% mais tempo |
| 50 | 1000 | 45,00 | 51,25 | 13,90% mais tempo |
| 100 | 100 | 18,64 | 15,33 | 17,76% menos tempo |
| 100 | 500 | 85,87 | 78,09 | 9,06% menos tempo |
| 100 | 1000 | 167,75 | 129,72 | 22,67% menos tempo |
| 200 | 100 | 67,05 | 42,78 | 36,20% menos tempo |
| 200 | 500 | 331,41 | 225,20 | 32,05% menos tempo |
| 200 | 1000 | 663,92 | 430,33 | 35,18% menos tempo |

Os percentuais consideram os tempos originais, antes do arredondamento da tabela.

## O que identificamos nos resultados

**Com 10 corpos, a N1 foi mais eficiente.** Na execução de 1.000 iterações, ela levou 2,17 ms, enquanto a N2 levou 16,75 ms. Nesse tamanho de simulação, as mudanças da N2 acrescentaram mais custo do que benefício.

**Com 50 corpos, os tempos ficaram mais próximos**, mas a N1 ainda apresentou vantagem nas três quantidades de iterações. Esse resultado mostra que aumentar um pouco a carga ainda não foi suficiente para compensar o custo adicional da N2.

**Com 100 corpos, a N2 passou a apresentar vantagem.** Para 1.000 iterações, o tempo caiu de 167,75 ms para 129,72 ms, uma redução de 22,67%.

**Com 200 corpos, a melhora foi mais consistente:** a N2 reduziu o tempo em aproximadamente 32% a 36% nos três testes. Com 1.000 iterações, foram 663,92 ms na N1 e 430,33 ms na N2 — uma economia de 233,59 ms por execução.

## Quais mudanças ajudam a explicar a melhora?

Na N1, as forças são calculadas sequencialmente. Na N2, parte do trabalho é distribuída entre tarefas com `Parallel.For`. Em simulações maiores, há mais cálculos para distribuir; em simulações pequenas, o custo de organizar essas tarefas pode pesar mais.

A verificação de colisões também foi otimizada: a N2 calcula os raios uma vez por chamada e evita calcular a raiz quadrada da distância nos pares que não colidem. Isso reduz operações repetidas, embora a verificação dos pares continue sequencial.

Além disso, a N2 utiliza Velocity Verlet e pode dividir uma iteração em passos menores. Portanto, a melhora observada é o resultado do conjunto de mudanças do motor, e não uma medida isolada do paralelismo.

Também comparamos as forças iniciais e as posições e velocidades finais das duas versões. **Os 12 cenários passaram nas tolerâncias definidas**, nas condições de corpos distantes usadas nesses testes.

## Conclusão

Os testes mostraram que a N2 apresentou melhor desempenho quando aumentamos a quantidade de corpos para 100 e 200. O destaque foi o cenário de 200 corpos e 1.000 iterações, com **35,18% de redução no tempo de processamento**.

A N1 continuou mais eficiente com 10 e 50 corpos. Assim, a evolução da N2 foi mais vantajosa nas cargas maiores avaliadas, enquanto as simulações pequenas favoreceram o processamento sequencial da N1.
