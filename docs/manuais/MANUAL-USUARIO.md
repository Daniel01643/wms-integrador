# Manual do Usuário — KitWms Integrador

<!--CAPA:usuario-->

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
10. [Devolução: tela de chaves](#10-devolução-tela-de-chaves)

---

## 1. O que o sistema faz

O KitWms Integrador lê os arquivos XML de nota fiscal que você deposita numa pasta de rede,
integra cada nota ao WMS automaticamente e organiza o resultado em pastas e relatórios. Você só
precisa depositar os arquivos; o resto é automático.

<!--DIAGRAMA:fluxo-usuario-->

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
| **Arquivo ignorado (não é XML)** | O arquivo depositado **não** é um `.xml`, então não é uma nota. O sistema não integra; só registra o aviso. | Depositar apenas arquivos `.xml` (ver a FAQ no fim deste manual). O arquivo foi para `Erro\...\ExtensaoInvalida`. |

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

**E se eu colocar um arquivo que não é XML?**
O sistema **não** integra arquivos que não sejam `.xml`. Ele move o arquivo para
`Erro\pacote_<data_hora>\ExtensaoInvalida` e registra no relatório como
**"Arquivo ignorado (não é XML)"** (status cinza, aviso — não é falha de nota). Esse arquivo é
removido automaticamente depois de **60 dias**, junto com o restante do pacote. Para evitar isso,
deposite **apenas arquivos `.xml`** de nota fiscal na pasta de entrada.
Arquivos que ainda estão sendo copiados (com extensão temporária como `.tmp`, `.part`,
`.crdownload` etc.) são **deixados em paz**: o sistema não os processa nem os move, espera a cópia
terminar. Quando a cópia acaba e o arquivo vira `.xml`, ele entra no fluxo normal.

---

## 10. Devolução: tela de chaves

> **Atenção:** esta tela só funciona depois que a TI ligar o recurso. Enquanto isso não for feito,
> o endereço não abre. A TI informa o endereço quando estiver pronto.

Quando um cliente devolve mercadoria, você usa uma tela simples para avisar o WMS. Você só
informa a nota de venda original: pela **chave de acesso** (os 44 números) ou pelo **número da nota
e a série**. O sistema acha o XML da nota sozinho e envia a devolução ao WMS.

### Antes de começar

- A nota de venda precisa ter sido integrada pelo sistema **nos últimos 60 dias**. Depois disso o
  XML é apagado e a tela avisa que não encontrou.
- Você não precisa de login nem senha.
- Use o computador ligado à rede da Friozem.

### Passo a passo

1. Abra o navegador (Edge ou Chrome) e digite o endereço da tela que a TI informou. Aparece a
   página **"Devolução: envio de chaves"**, com o logo da Friozem.
2. Escolha **uma** das duas formas de informar a nota (pode misturar na mesma lista):
   - **Pela chave:** clique no campo **"Chave de acesso (44 dígitos)"** (ele já vem selecionado ao
     abrir a página). **Digite** a chave ou **bipe** o código de barras da nota com o leitor, e tecle
     **Enter** (o leitor já faz isso sozinho). A chave entra na lista. Você também pode **colar várias
     chaves de uma vez**, uma por linha.
   - **Pelo número e pela série:** no quadro **"Ou procure pelo número e pela série"**, digite o
     **Número da nota** (até 9 números) e a **Série** (até 3 números) e clique em **Buscar e
     adicionar** (ou tecle **Enter**). Zeros à esquerda não atrapalham: `006` é o mesmo que `6`.
3. Veja o que a tela respondeu à busca por número e série:
   - **A nota foi encontrada:** ela entra na lista, e a tela avisa "Nota ... adicionada à lista".
   - **Apareceu uma tabela "Chaves encontradas":** existem **duas ou mais** notas com esse mesmo
     número e série (por exemplo, uma nota reemitida). A tabela mostra a chave, a data do arquivo e
     o pacote. Escolha a correta e clique em **Usar esta chave**. **Nada entra na lista antes da sua
     escolha.** Na dúvida, chame a TI.
   - **"Nota não encontrada na retenção de 60 dias":** confira o número e a série. Se a venda for
     antiga (mais de 60 dias) ou a nota nunca tiver sido integrada, a devolução precisa ser feita
     direto no WMS.
4. Confira a lista. Cada linha mostra a chave e, embaixo, o **número e a série** da nota.
   - O limite é **30 notas por envio**. O contador mostra quantas você já colocou.
   - Se a chave tiver número errado ou faltando, a tela avisa na hora e **não** coloca na lista.
   - Se você colocar a mesma nota duas vezes, a tela guarda só uma.
   - Para tirar uma nota da lista, clique em **Remover** ao lado dela.
5. Clique em **Enviar**. O botão fica desativado até ter pelo menos uma nota na lista e, enquanto o
   envio acontece, não clique de novo. Não existe campo de placa: a devolução é enviada sem placa
   e sem motorista.
6. Veja o quadro **"3. Resultado"**. Cada nota aparece numa linha, com a chave, o número e a série,
   a situação e a mensagem do WMS.
### O que cada situação significa

| Situação | O que quer dizer | O que fazer |
|---|---|---|
| **Enviada com sucesso** (verde) | O WMS registrou a devolução. | Nada. Siga o recebimento no WMS. |
| **Erro do WMS** (vermelho) | O WMS recusou. A mensagem explica o motivo. | Leia a mensagem. Se não entender, chame a TI e informe a nota e a mensagem. |
| **XML não encontrado (retenção de 60 dias)** (amarelo) | O sistema não achou o XML dessa nota. A venda pode ser mais antiga que 60 dias, ou a nota nunca foi integrada. | Confira a chave. Se a venda for antiga, a devolução precisa ser feita direto no WMS. |
| **Chave inválida** (vermelho) | A chave não tem 44 números ou algum número está errado. | Digite ou bipe de novo. |
| **Erro ao ler o XML** (vermelho) | O arquivo existe, mas não foi possível ler. | Chame a TI. |

Uma chave com problema **não impede** as outras: as demais do mesmo envio seguem normalmente.

### Dicas

- Para reenviar só as chaves com problema, use **Limpar lista**, digite novamente apenas as que
  precisam e envie.
- Não precisa de mouse: use **Tab** para passar de um campo para o outro e **Enter** para
  adicionar a chave ou para buscar pelo número e série.
- Se a página não abrir ou der erro de conexão, avise a TI. Isso não afeta o envio normal das notas.