# Operação — watcher de pasta, pacote, erros e e-mail (Etapa 7)

> **Instalação no servidor (Etapa 9):** o serviço é instalado como `Friozem-ImportacaoNotas` sob
> NSSM, com raiz `C:\Friozem\importacao_notas` e a **pasta de entrada em um share de rede** (fora da
> raiz do app). Os scripts de deploy ficam em `deploy\`; o passo a passo completo (RDP e WinRM,
> publicar/instalar/segredos/TLS/validar/rollback/remover) está em
> [`docs\INSTALACAO-SERVIDOR.md`](INSTALACAO-SERVIDOR.md).
>
> **Manuais (Etapa 10):** guias por público (usuário, técnico nível 1 e agente de IA) estão em
> [`docs\manuais\`](manuais/README.md).

O serviço (hospedado por NSSM) observa a pasta de entrada continuamente, integra os XMLs de NF-e
ao WMS por transporte, move cada arquivo e, ao terminar o pacote, gera o relatório (HTML + PDF) e
envia o resumo por e-mail. A I60 permanece **desligada** (`I60:Habilitada=false`).

## 1. Pastas

Configuradas na seção `Pastas` do `appsettings.json` (override por env, ex.: `Pastas__Entrada`).
`Pastas:Entrada` vazia herda o legado `Entrada:PastaEntrada` (padrão `C:\Envia xml`).
`Processados`, `Erro` e `Estado` relativos resolvem contra a Entrada.

Cada pacote tem a sua **própria subpasta** em `Processados` e em `Erro`, nomeada
`pacote_<yyyyMMdd_HHmmss>` (carimbo no horário **local** do servidor). O identificador da subpasta é o
**mesmo** do nome do relatório daquele pacote (seção 2), o que liga o conteúdo em disco ao relatório.

| Pasta | Conteúdo |
|---|---|
| `<Entrada>\Puxada` | ZIP (ou XML solto) de puxada normal: sobe como `PUXADA`. Ver seção 8. |
| `<Entrada>\PuxadaTSP` | ZIP (ou XML solto) de puxada TSP: sobe como `TSP`, uma onda por ZIP; XMLs soltos formam uma onda sem título. Ver seção 8. |
| `<Entrada>` (raiz) | XML/ZIP solto, aceito como `PUXADA` com Origem `raiz` (se `Pastas:AceitarRaiz=true`). |
| `<Entrada>\Estado\tsp\ondas.json` | Sequencial do dia e onda de cada ZIP TSP (reprocesso). Não apagar com o serviço rodando. |
| `<Entrada>\Estado\tmp` | Extração temporária dos ZIPs; apagada ao fim de cada ZIP. |
| `<Entrada>\Processados\pacote_<yyyyMMdd_HHmmss>\<arquivo>.xml` | Notas integradas, pendentes na interface, integradas com produto a revisar e reprocessos "já existe" — uma subpasta por pacote. |
| `<Entrada>\Erro\pacote_<yyyyMMdd_HHmmss>\<Categoria>\<arquivo>.xml` | Notas não integradas e XMLs inválidos, mais `arquivo.xml.motivo.txt` com o detalhe — a categoria fica **aninhada** sob a subpasta do pacote. |
| `<Entrada>\Estado` | Diário do pacote em andamento (`pacote-atual.jsonl`). Não apagar com o serviço rodando. |
| `Relatorio:PastaSaida` (padrão `relatorios`) | `pacote_<yyyyMMdd_HHmmss>.html` e `.pdf` (mesmo identificador da subpasta do pacote; carimbo no horário local do servidor). |

Categorias de `Erro` (uma pasta por tipo de erro):

- `XmlInvalido`: o XML não pôde ser lido (inclui arquivo truncado).
- `ZipInvalido`: o ZIP não pôde ser aberto (corrompido, com senha, sem XML, acima de `Zip:MaxArquivos`
  ou `Zip:MaxTamanhoMb`); o ZIP vai para essa pasta com `.motivo.txt`.
- `SemTransporte`: sem "Transporte: N" nas informações complementares.
- `ProdutoNaoCadastrado`: produto continua não cadastrado após as tentativas.
- `EmpresaNaoLocalizada`: empresa não localizada pelo WMS.
- `TentativasEsgotadas`: 3 tentativas sem sucesso e sem causa específica na mensagem.
- `Outro`: demais erros (inclui exceção inesperada ao processar o arquivo).

A mensagem tem prioridade sobre o número de tentativas na classificação.

Para mudar a organização, use `Pastas:OrganizacaoErro` (as três modalidades ficam **aninhadas** sob a
subpasta do pacote):

- `PorCategoria` (padrão): `Erro\pacote_<yyyyMMdd_HHmmss>\<Categoria>\arquivo.xml`.
- `PorArquivo`: `Erro\pacote_<yyyyMMdd_HHmmss>\<nome-do-arquivo>\arquivo.xml` (uma pasta por nota com erro).
- `Plana`: `Erro\pacote_<yyyyMMdd_HHmmss>\arquivo.xml`.

Exemplo real de árvore gerada por um pacote (dois arquivos; `a` integrada e `b` com produto não
cadastrado, `OrganizacaoErro=PorCategoria`):

```text
Processados\
  pacote_20250110_090000\
    a.xml
Erro\
  pacote_20250110_090000\
    Outro\
      b.xml
      b.xml.motivo.txt
relatorios\
  pacote_20250110_090000.html
  pacote_20250110_090000.pdf
```

## 2. Ciclo do pacote

O serviço consulta a pasta a cada `Pacote:IntervaloVarreduraSegundos` (polling; o
`FileSystemWatcher` perde eventos em pasta de rede).

O pacote é uma **foto** da pasta de entrada:

1. **Ocioso**: sem pacote aberto. Ao detectar arquivo(s) na entrada (prontos ou ainda estabilizando),
   abre o pacote (**Coletando**). Nesse instante calcula-se **uma única vez** o identificador do pacote
   (`pacote_<yyyyMMdd_HHmmss>` no horário local), usado tanto para a subpasta em `Processados`/`Erro`
   (cada arquivo é movido para lá durante o processamento) quanto para o nome do relatório — garantindo
   que ambos tenham exatamente o mesmo carimbo.
2. **Coletando** (janela de silêncio, padrão desde a release 2026.11.13-1): o pacote fecha a coleta quando
   passam `Pacote:SilencioSegundos` (padrão 15 s) **sem chegar arquivo novo nem mudar o tamanho/data de um
   existente**, ou quando a coleta atinge o teto `Pacote:JanelaMaximaSegundos` (padrão 180 s) contado da
   abertura (o pacote nunca passa disso). Cada arquivo novo ou alterado reinicia a contagem do silêncio,
   então lote pequeno fecha em ~15 s e lote grande, copiado aos poucos, não é dividido em vários pacotes e
   relatórios. O silêncio efetivo nunca é menor que `EstabilizacaoSegundos` (aviso no log da partida se a
   configuração pedir menos). Vale para a raiz, `Puxada` e `PuxadaTSP`. O log registra "Pacote aberto" com
   a janela e "Fim da coleta por ..." com o motivo (silêncio, teto ou janela fixa) e a duração.
   **Voltar ao comportamento antigo sem release**: `Pacote:ModoJanela` = `Fixa` em
   `config\appsettings.Production.json` (reiniciar o serviço): a coleta fecha após `JanelaColetaSegundos`
   (10 s) fixos da primeira detecção e a chegada de novos arquivos não adia a janela.
   No relatório a etapa aparece como "Coleta (janela de silêncio)" (telemetria `EsperaJanelaMs`).
3. **Foto e Processando**: ao fim da coleta, tira-se a foto: os XMLs **prontos** naquele instante formam
   o pacote (lista congelada). Um arquivo só é "pronto" quando tamanho e data ficam iguais por
   `EstabilizacaoSegundos` e o arquivo abre com acesso exclusivo. Registra-se a **primeira e a última
   gravação** (`LastWriteTime`) dos arquivos do pacote (log, corpo do e-mail e cabeçalho do relatório).
   As notas são enviadas **por transporte, em sequência** (todo o transporte A, depois o B). Arquivos que
   chegarem durante o processamento **não entram** no pacote em andamento: formam o **próximo pacote**.
4. **Fechando**: ao terminar a foto, no mesmo ciclo, grava HTML + PDF (sempre) **do pacote**, envia **um
   e-mail só dele** conforme a política, apaga o diário e volta a **Ocioso**. Se houver arquivos na
   entrada, o ciclo seguinte abre o próximo pacote (nova foto).

A quietude (`QuietudeSegundos`) foi **removida**: o fechamento deixou de depender de tempo e acontece
quando a foto termina de ser processada. A chave antiga, se existir no `appsettings`, é ignorada.

Envio por transporte: as notas do mesmo transporte são enviadas juntas e em sequência. Transportes
distintos rodam em paralelo até `Pacote:MaxTransportesConcorrentes` (padrão **1**; **2** é seguro).
A ordem de chegada é criação do arquivo e depois nome.

**Arquivo que nunca estabiliza** (cópia travada, gravação sem fim): fica na entrada, não entra na foto
e não trava o pacote. Depois de `TimeoutArquivoSegundos` (padrão 300 s) vira "instável": um aviso único
no log, e ele **sozinho nunca abre pacote**. Se a foto não tiver nenhum arquivo pronto (só arquivos ainda
em gravação), nenhum relatório vazio é gerado; o serviço volta a Ocioso e aplica **backoff exponencial**
entre fotos (silêncio efetivo × 2, ×4 no modo Silencio, ou `JanelaColetaSegundos` × 2, ×4 no modo Fixa, até `Pacote:BackoffMaximoSegundos`, padrão 120 s), sem
consumir CPU nem entrar em loop de foto. Quando o arquivo estabiliza, entra na foto seguinte. O backoff
vale só para arquivos ainda instáveis: um arquivo **pronto** novo abre pacote sem esperar o backoff.

Falhas: exceção em um arquivo ou transporte não derruba os demais (o arquivo vai para `Erro\Outro`).
Se o relatório não puder ser gravado, o pacote continua em **Fechando** e tenta de novo no ciclo
seguinte (o diário só é apagado após o relatório gravado). Falha de e-mail apenas é logada.

**Reinício do serviço e deduplicação**: o diário é a fonte de verdade. O identificador do pacote é
gravado em cada linha do diário, então a retomada reusa o **mesmo** identificador para reconciliar os
moves na subpasta correta (diário antigo sem o campo recalcula o identificador a partir do início do
pacote como fallback). Na retomada, um arquivo que ainda está na entrada, já tem registro no diário e
não foi regravado depois dele é apenas **movido** (reconciliação, sem reenviar ao WMS); se foi regravado
depois do registro, é tratado como novo. Movimentos
que falharam depois do envio (`MovePendentes`) são repetidos a cada ciclo Ocioso, nunca reenviados.
Como rede de segurança, o relatório mantém **uma linha por arquivo**: se houver dois registros do mesmo
arquivo (ex.: um "integrada" e um "já existia"), vale o **primeiro integrado**; se nenhum foi integrado,
vale o primeiro registrado. Uma nota em voo na queda ainda pode ser reenviada uma vez; o WMS responde "já
existe", ela segue para `Processados` e a mensagem começa com "Já existia no WMS (reprocesso)".
## 3. Limpeza

`Limpeza:Habilitada` (padrão `true`) e `Limpeza:DiasRetencao` (padrão **30**; o Plano B precisa achar o XML da venda original por até 30 dias). Roda na partida e a cada 24 h.
A limpeza opera por **subpasta de pacote**: cada subpasta imediata de `Processados`/`Erro` é removida
**por inteiro** quando toda ela está vencida, usando a **maior** (`LastWriteTimeUtc` mais recente) data
do seu conteúdo — assim um pacote que ainda tenha qualquer arquivo dentro do prazo é preservado inteiro.
Arquivos **soltos** direto na raiz (layout antigo, pré-subpasta) continuam sendo apagados por arquivo.
Só apaga dentro de `Processados` e `Erro`; nunca remove a **raiz** `Processados`/`Erro` nem toca a
Entrada, o `Estado` ou os `relatorios`. Recusa configuração perigosa (Processados/Erro iguais, dentro um
do outro, iguais à Entrada ou raiz de drive) e o serviço não inicia com `Pastas` inválida (erro no log).

## 4. E-mail

Corpo com a identidade visual Friozem: cabeçalho da marca, cartões coloridos por status (Total, Integradas,
Integradas — revisar produto, Pendentes, Não integradas, XMLs inválidos, Transportes), tabela de
erros/pendências (no máximo 20 linhas; quando excede, nota "ver todas no PDF anexo") e, quando tudo integra sem
ressalvas, um bloco verde "sem pendências" no lugar da tabela. No rodapé vai a **assinatura do sistema**
(KitWms Integrador — integração automática de notas fiscais com o WMS; contato `sac@friozem.com.br`;
`www.friozem.com.br`), sem telefones nem pessoas. A logo é embutida por **CID** (imagem inline, não `data:` URI,
para o Outlook não bloquear). Por padrão é a **logo Friozem embutida no programa** (versão negativa, sobre a faixa
azul; na assinatura, sobre um selo azul); `Relatorio:LogoPath` só a substitui por outro PNG. O texto "Friozem" só
aparece se nenhuma logo estiver disponível. Logo após os cartões vem o cartão **"Tempo de integração"** (resumo do
pacote, etapas e, só quando há TSP, a tabela por onda), com **os mesmos números** do relatório HTML/PDF; a geração
do relatório não entra nele (ver `RELATORIO-LOGS.md`, seção "Tempo de integração"). Pacote sem medição (antigo ou
retomado) mostra só o que foi medido, ou omite o cartão. Layout só em tabelas com CSS inline (Outlook). PDF
detalhado segue anexo. **Um e-mail por pacote.** `Email:EnviarQuando`: `Sempre` (padrão: todo pacote fechado envia), `ComProblema` (só se há não integrada, pendente, produto a revisar ou XML
inválido) ou `Nunca`. O envio vem **desligado** (`Habilitado=false`) até o SMTP real ser preenchido em `email.settings.json`. Sem SMTP configurado nada falha: o serviço só registra no log e o relatório fica salvo em disco (`Relatorio:PastaSaida`).

Configuração na seção `Email` (placeholders em `appsettings.json`, **sem senha**):

```json
{
  "Email": {
    "Habilitado": true,
    "Host": "smtp.suaempresa.com.br",
    "Porta": 587,
    "UsarTls": true,
    "Usuario": "integrador@suaempresa.com.br",
    "Remetente": "integrador@suaempresa.com.br",
    "RemetenteNome": "KitWms Integrador",
    "Destinatarios": "operacao@suaempresa.com.br;gestor@suaempresa.com.br",
    "AssuntoPrefixo": "[KitWms]",
    "EnviarQuando": "Sempre",
    "TimeoutSegundos": 60
  }
}
```

Onde colocar o que é local e secreto:

- **Arquivo de configuração dedicado**: copie `email.settings.example.json` para `email.settings.json`
  ao lado do executável. O arquivo é opcional, recarregado ao salvar e está no `.gitignore`.
- **Senha**: variável de ambiente `Email__Senha` (preferível) ou `email.settings.json`. Nunca no
  `appsettings.json` versionado, nem no log.

### 4.1. Destinatários do fechamento (arquivo txt editável a quente)

O sistema usa **dois** arquivos txt de destinatários, independentes e editáveis a quente (um e-mail
por linha, `#` comenta, linhas em branco ignoradas, **relidos a cada envio** — editar e salvar tem
efeito no próximo envio, sem reiniciar o serviço):

- **Fechamento do pacote** (novo): `Email:CaminhoDestinatarios`, padrão
  `<telemetria>\destinatarios-fechamento.txt`. Modelo em
  `deploy/config-exemplo/destinatarios-fechamento.exemplo.txt`.
- **Resumo semanal** (já existente): `Monitoramento:CaminhoDestinatarios` (ver seção 6.5).

Para o **fechamento**, a **precedência** e o **fallback** são:

1. se o txt tiver ao menos um e-mail, ele é usado (tem **precedência** sobre `Email:Destinatarios`);
2. se o txt estiver ausente/vazio, usa-se a lista `Email:Destinatarios` (separada por `;`), o
   comportamento atual;
3. se **ambos** estiverem vazios, o e-mail do pacote **não é enviado** e um aviso é registrado no log
   — o fechamento **conclui normalmente** (relatório HTML + PDF é gerado assim mesmo).

Esse fallback é **diferente** do resumo semanal, que cai no padrão mínimo `ti@friozem.com.br` (seção
6.5) em vez de não enviar. A lista REAL de fechamento não é versionada (fica em `config\`, coberta
pelo `.gitignore`).

Limitações do `SmtpClient`: suporta STARTTLS (587) e SMTP interno, mas **não** TLS implícito (465)
nem OAuth2 (Microsoft 365/Gmail modernos). Se o provedor exigir, o envio pode ser trocado por MailKit
implementando `IEnviadorEmail`, sem mexer no resto. Em Windows Server 2012 R2 o TLS 1.2 depende do
SChannel do sistema (validar no servidor).

## 5. Exemplo de configuração do fluxo (sem segredos)

```json
{
  "Pastas": { "Entrada": "C:\\Envia xml", "Processados": "Processados", "Erro": "Erro", "Estado": "Estado", "OrganizacaoErro": "PorCategoria" },
  "Pacote": {
    "IntervaloVarreduraSegundos": 5, "EstabilizacaoSegundos": 3,
    "ModoJanela": "Silencio", "SilencioSegundos": 15, "JanelaMaximaSegundos": 180, "JanelaColetaSegundos": 10,
    "TimeoutArquivoSegundos": 300, "BackoffMaximoSegundos": 120, "MaxTransportesConcorrentes": 1
  },
  "Limpeza": { "Habilitada": true, "DiasRetencao": 30 }
}
```

## 6. Monitoramento / Resumo semanal

O agente coleta telemetria de uso e saúde e envia, por e-mail, um **resumo semanal** do uso do
sistema (frequência, performance, chamadas ao WMS, erros e disponibilidade). Tudo roda **dentro do
Worker** (sem Task Scheduler), em horário **local** do servidor. A seção de configuração é
`Monitoramento` no `appsettings.json`.

### 6.1. O que é coletado e onde fica

- **`telemetria.jsonl`** — uma linha por **pacote fechado** (totais por status, média de notas por
  pacote, tempos médios, tentativas, chamadas ao WMS e erros por categoria). Desde 12/11/2026 também
  traz os tempos medidos: `EsperaJanelaMs`, `LeituraMs`, `IntegracaoWmsMs` (fase de integração, tempo
  de parede), `ChamadasWmsMs` (soma das chamadas I50/I10/I30/I60), `TempoRelatorioMs` (geração e
  gravação do HTML+PDF, que não cabe no próprio relatório) e `Ondas` (por onda TSP: notas,
  transportes, duração). Os campos são **opcionais**: linhas antigas continuam sendo lidas, com 0/vazio.
  O resumo semanal ganha a linha **"Integração no WMS por nota"** (soma de `ChamadasWmsMs` ÷ notas,
  só dos pacotes com medição); ela não aparece enquanto nenhum pacote tiver medição.
- **`servico.jsonl`** — eventos de **saúde** do serviço (ex.: `servico-iniciado` na partida, com a
  versão do assembly).

Os arquivos ficam **ao lado do `Estado`**, na pasta `telemetria` (`<Estado>\telemetria`), **fora** de
`Processados`/`Erro`. A pasta é criada sob demanda e nunca é tocada pela limpeza. A escrita é append +
flush (padrão do diário do pacote) e a leitura é tolerante a uma linha truncada no fim.

> As **chamadas ao WMS** são contadas de forma **aproximada** (estimadas pelo histórico textual de cada
> nota, por interface I50/I10/I30); o resumo sinaliza quando a contagem é aproximada.

### 6.2. Agendamento, idempotência e slot perdido

- Envio **fixo**: toda **segunda-feira às 12:00** (horário local). Configurável em
  `Monitoramento:DiaDaSemana` e `Monitoramento:Hora`.
- A cada ciclo do Worker o agendador calcula o **slot** da semana corrente (o instante dia+hora
  configurados). Envia quando `agora >= slot` e aquele slot ainda não foi enviado.
- **Idempotência**: o último slot enviado é gravado em `<telemetria>\ultimo-resumo.txt`; o mesmo slot
  **não** é reenviado. Na semana seguinte um novo slot reabre o envio.
- **Slot perdido**: se o serviço estava parado no horário do slot e voltou **depois** (ainda na mesma
  semana), o resumo é enviado com atraso no primeiro ciclo após a volta — nenhuma semana é pulada por
  uma parada curta.

### 6.3. Faixas do dia

A frequência de uso é distribuída por dia da semana e por **faixa do dia** (horário local, bordas
fixas):

- **Manhã**: 06:00–11:59
- **Tarde**: 12:00–17:59
- **Noite**: 18:00–05:59 (atravessa a meia-noite)

### 6.4. Configuração (seção `Monitoramento`)

A seção guarda **apenas** parâmetros e **o caminho** do arquivo de destinatários — **nunca** a lista de
e-mails. O SMTP é **reaproveitado** do bloco `Email` (mesmo servidor/credenciais do e-mail de
fechamento). Campos:

- `Habilitado` (padrão `true`): liga a coleta e o resumo.
- `DiaDaSemana` (padrão `Monday`) e `Hora` (`"HH:mm"`, padrão `"12:00"`): quando enviar.
- `PeriodoDias` (padrão `7`): janela do resumo.
- `AssuntoPrefixo` (padrão `"[KitWms]"`): prefixo do assunto.
- `CaminhoTelemetria` (vazio = `<Estado>\telemetria`): onde a telemetria é gravada.
- `CaminhoDestinatarios` (vazio = `<telemetria>\destinatarios.txt`): o arquivo da lista de
  destinatários (fora do git).

### 6.5. Destinatários (arquivo txt editável a quente)

A lista de destinatários **não** fica no `appsettings.json`: vive num arquivo txt dedicado
(`<telemetria>\destinatarios.txt` por padrão, ou o caminho em `CaminhoDestinatarios`), que **não** é
versionado. Use `docs/exemplos/destinatarios-resumo.exemplo.txt` como modelo. O arquivo é **relido a
cada envio**, então editá-lo tem efeito **no próximo envio**, sem recompilar nem reiniciar o serviço.

Formato: **um e-mail por linha**; linhas em branco são ignoradas; linhas iniciadas por `#` são
comentários. Exemplo:

```text
# Destinatarios do resumo semanal — um e-mail por linha. '#' comenta.
ti@friozem.com.br
gestor@friozem.com.br
```

**Incluir** um destinatário: acrescente uma linha com o e-mail e salve. **Excluir**: apague a linha (ou
comente-a com `#`) e salve. Se o arquivo estiver **ausente, vazio ou ilegível**, o resumo é enviado ao
padrão mínimo `ti@friozem.com.br` e um aviso é registrado no log. Não há subcomando de gerência: o txt é
o único mecanismo.

### 6.6. Fallback em disco (SMTP desligado)

Se o SMTP não estiver configurado (bloco `Email` desabilitado ou sem `Host`/`Remetente`) ou o envio
falhar, o resumo **não falha**: o HTML e o PDF são gravados em `<telemetria>\resumos\` como
`resumo_<yyyyMMdd_HHmm>.html` e `.pdf`, e o fato é registrado no log.

## 7. Suposições e pontos em aberto

- O texto da resposta da I50 para duplicata contém "já existe" (não verificado no WMS). Se for outro,
  ajuste só `ClassificadorRetorno.EhJaExiste`.
- Um reprocesso "já existe" vai para `Processados`, mas aparece no relatório como não integrado, com
  a mensagem "Já existia no WMS (reprocesso)".
- Nota tardia de um transporte já enviado no mesmo pacote vai em lote posterior com o mesmo
  `nroagrupseparacao`; o WMS pode bloquear se a onda já saiu de "editando".
- `Pastas:Estado` e `Relatorio:PastaSaida` não podem ficar iguais nem dentro de `Processados`/`Erro` (a limpeza apagaria o diário e os relatórios); o serviço não inicia nesse caso (erro no log).
- Valores padrão de janela de coleta, backoff e timeout são estimativas a validar em operação.
- Não validado contra WMS ou SMTP reais (somente testes automatizados com fakes).

## 8. Puxada e Puxada TSP (ZIP por pasta)

Desde 12/11/2026 a entrada tem duas pastas de depósito. O colaborador salva **somente o ZIP**
(sem e-mail, sem Outlook). Escopo e decisões: [`ESCOPO-PUXADA-TSP.md`](ESCOPO-PUXADA-TSP.md).
Desenho: [`ARQUITETURA.md`](ARQUITETURA.md), §2.2.

| Pasta | Aceita | Tipo | Onda no WMS (`nroagrupseparacao`) |
|---|---|---|---|
| `Puxada` | ZIP e XML solto | `PUXADA` | Número do transporte de cada nota (como sempre). |
| `PuxadaTSP` | ZIP e XML solto | `TSP` | Uma por ZIP: `TSP-AAAAMMDD-NN-NOME`. Os XMLs soltos do pacote formam uma onda `TSP-AAAAMMDD-NN` (sem título). |
| raiz da entrada | XML/ZIP solto | `PUXADA` (Origem `raiz`) | Número do transporte. |

### 8.1. Configuração

Na seção `Pastas` e na nova seção `Zip` do `appsettings` (override por env com `__`, ex.:
`Pastas__AceitarRaiz`):

| Chave | Padrão | Efeito |
|---|---|---|
| `Pastas:Puxada` | `Puxada` | Nome da subpasta da puxada normal. |
| `Pastas:PuxadaTsp` | `PuxadaTSP` | Nome da subpasta da puxada TSP. |
| `Pastas:AceitarRaiz` | `true` | `true`: XML/ZIP solto na raiz sobe como `PUXADA` com Origem `raiz`. `false`: fica parado na entrada, sem erro e sem processamento. |
| `Zip:MaxArquivos` | `1000` | Máximo de arquivos dentro de um ZIP. |
| `Zip:MaxTamanhoMb` | `500` | Máximo, em MB, do conteúdo descompactado de um ZIP. |

O serviço cria `Puxada` e `PuxadaTSP` na entrada se faltarem. Os nomes das duas pastas não podem
repetir nem coincidir com `Processados`, `Erro` ou `Estado`; configuração inválida impede o início
(erro no log). Os dois `appsettings` versionados já trazem as chaves.

### 8.2. Regras do ZIP

- O ZIP só é aberto quando está pronto (mesmo detector de arquivo pronto dos XMLs).
- Entram só arquivos `.xml`; outro tipo vira `ExtensaoInvalida`. ZIP dentro de ZIP não é aberto.
  Nomes com `..` ou caminho absoluto são ignorados.
- ZIP corrompido, com senha, sem XML ou acima dos limites: `Erro\pacote_*\ZipInvalido` + `.motivo.txt`.
- O mesmo XML repetido em dois ZIPs é processado uma vez; o repetido aparece como "Arquivo ignorado
  (XML repetido)".
- Os XMLs e o ZIP original ficam em `Processados\pacote_*\`.
- XML solto na `PuxadaTSP` é aceito e sobe como `TSP`, com Origem `PuxadaTSP` e sem ZIP. Todos os XMLs soltos
  do mesmo pacote formam **uma onda** `TSP-AAAAMMDD-NN` (sem título, sequencial do dia compartilhado com
  as ondas de ZIP). Não há reaproveitamento por hash: se o colaborador errou, apaga a onda no WMS e refaz
  o processo, e um novo sequencial é gerado. Arquivo que não seja ZIP nem XML vai para `ExtensaoInvalida`.

### 8.3. Nome da onda da TSP

Formato `TSP-AAAAMMDD-NN-NOME`, por exemplo `TSP-20261111-01-SOROCABA` (`XML'S SOROCABA.zip`) ou
`TSP-20261111-02-SJC` (`XML'S SJC.zip`).

- Data do relógio do servidor; `NN` é o sequencial do dia (reinicia a cada dia).
- `NOME`: nome do ZIP sem extensão, sem as palavras `XML`/`XMLS` e apóstrofos, maiúsculas, sem acento,
  só letras, números e hífen, **cortado em 14 caracteres** (a parte fixa tem 16; o WMS aceita até 30).
- Sem nome aproveitável (e também nos XMLs soltos): `TSP-AAAAMMDD-NN`.
- **Reprocesso:** o mesmo ZIP (mesmo conteúdo, por hash) reaproveita a mesma onda, então reenviar não
  cria uma segunda onda. Um ZIP diferente com o mesmo nome ganha novo `NN`.
- O DocCarga continua sendo um por nota. Nota de TSP sem transporte sobe normalmente.

### 8.3.1. Placa do veículo (Puxada com placa, TSP sem placa)

A **Puxada TSP sempre vai ao WMS sem placa** (`placaVeiculo` em branco na I50), seja ZIP ou XML solto em
`PuxadaTSP` (decisão de 13/11/2026). A chave é `I50:EnviarPlacaVeiculoTsp` (padrão `false`; override por env
`I50__EnviarPlacaVeiculoTsp`). A Puxada normal (pasta `Puxada` e raiz) continua com `I50:EnviarPlacaVeiculo`
(configurada como `true`: a Puxada normal **vai com a placa** lida do XML; `false` envia em branco). O `false` de 13/11/2026 foi só no teste local.

### 8.4. O que o relatório e o e-mail mostram

- Colunas **Tipo** (`PUXADA` ou `TSP`) e **Origem** (`Puxada`, `PuxadaTSP` ou `raiz`) em cada nota.
- PUXADA agrupada por transporte; TSP agrupada por **onda**, com a quantidade de notas e de
  transportes distintos; a coluna Transporte mostra o transporte de cada nota ("(sem)" se não houver).
- Resumo do relatório e e-mail de fechamento: notas por tipo (PUXADA e TSP) e "Notas da raiz". O resumo
  semanal soma as notas por tipo.
- Cabeçalho do relatório: `Pacote <id> · PUXADA`, `· TSP` ou `· PUXADA + TSP` (pacote misto).
- Bloco "Tempo de integração": na TSP, tabela por onda (onda, notas, transportes, tempo da onda), que
  substitui os "chips" de ondas; na PUXADA, só o resumo e as etapas (sem listar transportes).
- **Pasta errada:** o sistema não detecta nem bloqueia. Conferir Tipo e Origem no relatório; para
  corrigir, ajustar a onda no WMS (a nota já subiu) e orientar o colaborador sobre a pasta certa.
  Notas com Origem `raiz` entram no relatório justamente para análise.

### 8.5. Pontos a validar no homolog

- Na TSP o `numeroPlanejamento` da I50 repete o nome da onda (como na puxada repete o transporte).
  Confirmar que o WMS aceita. Roteiro: [`ROTEIRO-TESTE-PUXADA-TSP.md`](ROTEIRO-TESTE-PUXADA-TSP.md).
- Ainda sem teste com ZIP real no homolog (os testes automatizados usam ZIPs sintéticos, sem WMS).

## 9. Devolução pela I40: tela de chaves (Plano B)

Recurso **desligado** (`Devolucao:Habilitada=false`). Nada foi ligado em produção. Quando ligado, o
Worker abre uma página web simples na porta `Devolucao:Porta` (padrão 8085), com um login simples (só identifica quem envia), onde o
colaborador digita ou bipa a chave de NF-e, ou pesquisa pelo número da nota + série (até 30 notas por envio). O sistema localiza o XML da venda em
`Processados` (retenção de 30 dias), monta a I40 (`tipoNota=DE`) e envia ao WMS, mostrando o
resultado por chave.

- **Sem placa:** a placa e o motorista foram removidos da tela e do código (10/11/2026). O envio vai
  sempre como `IMP` (`Devolucao:TipoOperacaoNota`), com `placaVeiculoPrevistoNota` e `nomeMotorista` em branco
  (validado no homolog: "Registrado com sucesso" e recebimento concluído).
- **Busca por número + série:** `GET /api/devolucao/buscar`. Lê só os nomes dos arquivos
  `NFE<chave>.XML` em `Processados` e `pacote_*` (nunca `Erro`), filtra pelo CNPJ de
  `Operacional:CnpjDepositante` e, havendo mais de uma chave para o mesmo número e série, a tela pede
  a escolha (nada é enviado sozinho). Sem resultado: "Nota não encontrada na retenção de 30 dias."
- **Auditoria:** `<Estado>\devolucao\devolucao_yyyyMMdd.log` (ou `Devolucao:PastaAuditoria`), uma linha por
  chave com data, IP de origem, **usuário do WMS informado na tela**, chave, nota, status, transação e mensagem
  (linhas antigas têm 7 campos, sem o usuário). Sem senha. Cada chave também aparece em `logs\worker.out.log`
  com `usuario=`.
- **Login simples (13/11/2026):** o usuário digita o login do WMS e uma senha antes de enviar. O login serve
  só para registrar quem enviou; a senha não é verificada nem guardada (a página nem a envia). A busca
  funciona sem login, mas "Enviar devolução" só libera com o usuário confirmado. O WMS continua sendo
  chamado com a conta de serviço.
- **Sem estado:** a tela não guarda histórico além da auditoria. Se a mesma nota for enviada duas
  vezes, quem decide é o WMS.
- **Isolamento:** falha da tela (porta ocupada, erro) é registrada e não derruba o ciclo de pacotes.
- **Segurança:** decisão do usuário (13/11/2026): link aberto na rede interna, sem restrição de firewall por
  origem. Risco aceito: quem tiver o link pode enviar, e a identificação é autodeclarada. Não exponha a
  porta fora da rede interna (`docs/INSTALACAO-SERVIDOR.md`, seção 3.12).
- **Ligar/desligar:** `Devolucao__Habilitada=true|false` e reiniciar o serviço (a devolução não consta nos manuais oficiais).
- **Verificar:** `GET http://<servidor>:<porta>/api/saude` deve responder `{"status":"ok"}`.

## 10. Retorno de separação (I51) e relatório diário da I60

- Chaves (todas `false` por padrão): `I51__Habilitada`, `I60__Habilitada`. Demais chaves em `appsettings.json`, seções `I51`, `I60` (padrões e `TpAcaoReenvioImp`) e `RelatorioI60`.
- Estado em `Estado\i51\` (`notas`, `xml`, `orfaos`, `brutos\pendentes|processados|falhos`, `comandos`, `consulta.json`). Retenção: `I51__DiasRetencaoEstado` (30).
- Comandos: `--consultar-i51 --tipo UNICO --cnpj <cnpj> --numero <n>`; `--reenfileirar-i60 --chave <c>|--motivo <MotivoI60>` (o Worker em execução aplica no ciclo seguinte); `--relatorio-i60 [--enviar]`.
- Relatório diário às 12:00 em `Relatorio:PastaSaida\i60`; destinatários em `destinatarios-i60.txt` (telemetria), com fallback na lista do resumo semanal.
- Logs não trazem token, senha nem corpo de nota. Roteiro de teste: `ROTEIRO-TESTE-I51-I60.md`.
