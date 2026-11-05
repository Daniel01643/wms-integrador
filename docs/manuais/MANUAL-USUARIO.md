# Manual do Usuário — KitWms Integrador

Guia prático para o operador do depósito depositar as notas fiscais (XML), acompanhar o
resultado e entender os relatórios. Linguagem simples, passo a passo. Você **não** precisa de
nada técnico: só o Windows Explorer e a rede da Friozem.

## Índice

1. [O que o sistema faz](#1-o-que-o-sistema-faz)
2. [Como depositar os XMLs](#2-como-depositar-os-xmls)
3. [O que acontece depois que você solta os arquivos](#3-o-que-acontece-depois-que-você-solta-os-arquivos)
4. [Onde ver o resultado](#4-onde-ver-o-resultado)
5. [Como ler o relatório (os status)](#5-como-ler-o-relatório-os-status)
6. [O e-mail de fechamento](#6-o-e-mail-de-fechamento)
7. [Boas práticas](#7-boas-práticas)
8. [Como reenviar uma nota corrigida](#8-como-reenviar-uma-nota-corrigida)
9. [Perguntas frequentes](#9-perguntas-frequentes)

---

## 1. O que o sistema faz

O KitWms Integrador lê os arquivos XML de nota fiscal que você deposita numa pasta de rede,
integra cada nota ao WMS automaticamente e organiza o resultado em pastas e relatórios. Você só
precisa depositar os arquivos; o resto é automático.

## 2. Como depositar os XMLs

1. Abra o **Explorador de Arquivos** (Windows Explorer).
2. Na barra de endereço, digite o caminho do compartilhamento de rede e pressione **Enter**:

   ```text
   \\SRVAPPALC02\kitbrf
   ```

   Se o nome do servidor não funcionar, use o endereço IP:

   ```text
   \\200.200.200.213\kitbrf
   ```

3. **Copie** (ou arraste) os arquivos **`.xml`** das notas para dentro dessa pasta.
4. Pronto. A partir daí o sistema cuida do resto.

> Deposite os arquivos **na raiz** do compartilhamento (soltos na pasta `kitbrf`). Não crie
> subpastas para eles; o sistema só lê os `.xml` que estão no topo.

## 3. O que acontece depois que você solta os arquivos

Em poucos segundos o sistema:

1. **Lê** os XMLs que você soltou.
2. **Agrupa** as notas por transporte e **integra** cada uma no WMS.
3. **Move** cada arquivo para a pasta de resultado (Processados ou Erro), organizado por
   **pacote** (um lote de notas processadas juntas).
4. **Gera o relatório** (HTML e PDF) daquele pacote.
5. Pode **enviar um e-mail** de fechamento com o resumo e o PDF em anexo.

Cada lote processado recebe um nome de pacote no formato `pacote_<data_hora>`
(exemplo: `pacote_20250110_090000`). Esse mesmo nome aparece na pasta de resultado **e** no
relatório, para você ligar um ao outro.

## 4. Onde ver o resultado

Tudo fica **dentro do mesmo compartilhamento** `\\SRVAPPALC02\kitbrf` (ou `\\200.200.200.213\kitbrf`):

| Pasta | O que tem dentro |
|---|---|
| `Processados\pacote_<data_hora>\` | Notas que entraram bem (integradas, pendentes na interface ou integradas com produto a revisar), organizadas por pacote. |
| `Erro\pacote_<data_hora>\` | Notas que **não** foram integradas e XMLs inválidos. Ao lado de cada arquivo há um `.motivo.txt` com o detalhe do problema. |
| `relatorios\` | O relatório de cada pacote em **HTML** (`pacote_<data_hora>.html`) e **PDF** (`pacote_<data_hora>.pdf`). |

Para ver o relatório, abra a pasta `relatorios` e dê dois cliques no arquivo `.html` (abre no
navegador) ou no `.pdf`.

## 5. Como ler o relatório (os status)

O relatório mostra uma linha por nota, com um **status**. Estes são os status reais exibidos:

| Status no relatório | O que significa | O que você deve fazer |
|---|---|---|
| **Integrada** | A nota foi aceita pelo WMS, tudo certo. | Nada. |
| **Integrada — revisar produto** | A nota entrou no WMS, mas havia um produto provisório que precisa ser conferido depois. | Avisar quem cuida de cadastro de produto para revisar o item no WMS. A nota já entrou. |
| **Pendente** | A nota foi importada, mas está **aguardando processamento na tela do WMS**. | Aguardar e/ou conferir a nota na interface do WMS. |
| **Não integrada** | A nota **não** entrou (erro que não deu para resolver, ou esgotaram as tentativas). | Corrigir a nota e reenviar (ver seção 8). O motivo está na coluna Mensagem e no arquivo `.motivo.txt` na pasta Erro. |

Além dos status, o relatório mostra um contador de **XMLs inválidos**: são arquivos que nem
puderam ser lidos (por exemplo, arquivo corrompido ou cortado pela metade). Eles **não** viram
linha de nota — aparecem como um número no resumo e o arquivo vai para `Erro\XmlInvalido`. Nesse
caso, gere o XML de novo e deposite outra vez.

Outras colunas úteis do relatório: Nota, Transporte, Horário, Mensagem (último erro) e Tentativas.

## 6. O e-mail de fechamento

Ao fechar um pacote, o sistema pode enviar um **e-mail de fechamento** com o resumo (quantas
integraram, quantas ficaram pendentes, quantas não integraram etc.) e o **PDF do pacote em anexo**.

Se você está na lista de destinatários, recebe esse e-mail automaticamente. Para entrar ou sair
da lista, fale com o responsável técnico/TI (a lista é configurada no servidor).

## 7. Boas práticas

- **Não tente apagar** arquivos do compartilhamento. Você tem permissão para **gravar e ler**, mas
  **não para excluir** — o próprio sistema organiza e move os arquivos.
- Depois de soltar um lote, **espere alguns segundos** antes de soltar o próximo. O sistema junta
  em um pacote os arquivos que chegam dentro de uma pequena janela de tempo; dar um intervalo entre
  os lotes deixa os pacotes mais organizados.
- Deposite somente arquivos **`.xml`** de nota fiscal, soltos na raiz do compartilhamento.
- Confira o relatório e a pasta `Erro` depois de depositar, para saber se algo precisa de correção.

## 8. Como reenviar uma nota corrigida

1. Corrija a nota que apareceu como **Não integrada** (gere o XML novamente, já corrigido).
2. Abra o compartilhamento `\\SRVAPPALC02\kitbrf` (ou `\\200.200.200.213\kitbrf`).
3. **Copie o XML corrigido** para a raiz da pasta, como faz com qualquer nota nova.
4. O sistema processa de novo e gera um novo resultado/relatório.

Não é preciso apagar nada da pasta `Erro`: basta depositar o arquivo corrigido novamente.

## 9. Perguntas frequentes

**Depositei o arquivo e não aconteceu nada na hora. É normal?**
Sim. O sistema verifica a pasta a cada poucos segundos e ainda espera uma pequena janela para
juntar o lote. Aguarde alguns segundos e confira as pastas `Processados`/`Erro` e `relatorios`.

**Posso apagar os arquivos antigos do compartilhamento?**
Não, e você não tem permissão para isso. O próprio sistema faz a limpeza das pastas de resultado
automaticamente depois de alguns dias.

**Soltei vários XMLs de uma vez. Eles vão para o mesmo pacote?**
Os que chegarem dentro da mesma janela de coleta entram no mesmo pacote. Os que chegam depois
formam o próximo pacote.

**Uma nota apareceu como "Pendente". Deu erro?**
Não necessariamente. "Pendente" quer dizer que a nota foi importada e está aguardando
processamento na tela do WMS. Acompanhe pelo WMS.

**O que é "Integrada — revisar produto"?**
A nota entrou no WMS normalmente, mas há um produto provisório que alguém precisa revisar depois
no WMS. A integração em si deu certo.

**Não recebi o e-mail de fechamento.**
O envio depende de você estar na lista de destinatários e do e-mail estar configurado. Fale com a
TI para incluir seu endereço.
