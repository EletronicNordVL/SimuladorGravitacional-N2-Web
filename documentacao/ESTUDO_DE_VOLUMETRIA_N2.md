# Estudo de volumetria: da N1 em TXT à N2 com banco de dados

Na N1, os dados da simulação eram salvos em arquivos `.txt`. Cada arquivo guardava as configurações do universo e uma linha por corpo, com nome, massa, densidade, posição e velocidade.

Na N2, esses dados passaram a ser organizados no banco, nas tabelas `Simulacao`, `Corpo` e `EstadoCorpo`. O estudo de volumetria mostra como a quantidade de dados cresce conforme aumentamos os corpos, os passos e os salvamentos.

## O que muda na quantidade de dados?

Ao salvar pela interface, as duas versões guardam o estado atual do universo. Por exemplo, uma simulação com 400 corpos produz:

| N1: arquivo TXT | N2: banco de dados |
| --- | --- |
| Um cabeçalho e 400 linhas de corpos | Um registro de simulação, 400 registros de corpos e 400 estados |

Um estado representa os dados de um corpo naquele momento. Salvar pela interface não guarda automaticamente todos os movimentos anteriores.

A N2 também possui um método para gravar o histórico completo. Nesse caso, cada corpo tem um estado inicial e outro após cada passo da simulação, chamado de iteração.

## Exemplo com histórico completo

Para contar os estados, usamos:

```text
Quantidade de estados = corpos × (iterações + 1)
```

O `+ 1` representa o estado inicial. Com **400 corpos e 100 iterações**, temos:

```text
400 × 101 = 40.400 estados
```

Ou seja, salvar apenas o momento atual gera 400 estados; guardar todo esse histórico gera 40.400. Quanto mais corpos e passos forem registrados, maior será a quantidade de dados.

## Quanto espaço pode ocupar?

Para uma projeção, consideramos 180 bytes por estado, 256 por corpo e 512 por simulação. São valores adotados para planejamento, não tamanhos medidos. Eles incluem uma margem para a organização dos dados no banco.

Supondo **50 salvamentos por dia, com 400 corpos, durante 30 dias**, sem apagar dados:

| Forma de salvar na N2 | Estados acumulados | Espaço total estimado |
| --- | --- | --- |
| Apenas o estado atual | 600.000 | 262 MB |
| Histórico completo de 100 iterações | 60.600.000 | 11,06 GB |

Os valores usam MB e GB decimais e incluem a estimativa dos registros de corpos e simulações. O espaço real depende da configuração e dos índices do banco. Se houver limpeza de dados antigos, o crescimento também dependerá do que for excluído.

Ainda não medimos o tamanho de arquivos TXT equivalentes nem o espaço real ocupado no banco. Por isso, **não podemos afirmar que a N2 ocupa menos espaço que a N1**. Para verificar isso, precisamos salvar a mesma simulação nas duas versões e medir os dois formatos.

A principal diferença é a forma de organizar e consultar os dados. O histórico completo da N2 permite manter mais informações, mas exige mais armazenamento do que salvar apenas um momento da simulação.
