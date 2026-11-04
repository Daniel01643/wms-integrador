# Operação — watcher de pasta, pacote, erros e e-mail (Etapa 7)

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
| `<Entrada>` | XMLs a processar (somente `*.xml`, topo da pasta). |
| `<Entrada>\Processados\pacote_<yyyyMMdd_HHmmss>\<arquivo>.xml` | Notas integradas, pendentes na interface, integradas com produto a revisar e reprocessos "já existe" — uma subpasta por pacote. |
| `<Entrada>\Erro\pacote_<yyyyMMdd_HHmmss>\<Categoria>\<arquivo>.xml` | Notas não integradas e XMLs inválidos, mais `arquivo.xml.motivo.txt` com o detalhe — a categoria fica **aninhada** sob a subpasta do pacote. |
| `<Entrada>\Estado` | Diário do pacote em andamento (`pacote-atual.jsonl`). Não apagar com o serviço rodando. |
| `Relatorio:PastaSaida` (padrão `relatorios`) | `pacote_<yyyyMMdd_HHmmss>.html` e `.pdf` (mesmo identificador da subpasta do pacote; carimbo no horário local do servidor). |

Categorias de `Erro` (uma pasta por tipo de erro):

- `XmlInvalido`: o XML não pôde ser lido (inclui arquivo truncado).
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
2. **Coletando**: espera a janela de coleta **fixa** `JanelaColetaSegundos` (padrão 10 s), contada da
   primeira detecção. Chegada de novos arquivos NÃO reinicia nem adia a janela.
3. **Foto e Processando**: ao fim da janela, tira-se a foto: os XMLs **prontos** naquele instante formam
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
entre fotos (`JanelaColetaSegundos` × 2, ×4, ... até `Pacote:BackoffMaximoSegundos`, padrão 120 s), sem
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

`Limpeza:Habilitada` (padrão `true`) e `Limpeza:DiasRetencao` (padrão **10**). Roda na partida e a cada 24 h.
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
para o Outlook não bloquear); vem de `Relatorio:LogoPath` — sem logo válida, o cabeçalho e a assinatura caem
para o texto "Friozem", sem erro. PDF detalhado segue anexo. **Um e-mail por pacote.** `Email:EnviarQuando`: `Sempre` (padrão: todo pacote fechado envia), `ComProblema` (só se há não integrada, pendente, produto a revisar ou XML
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

Limitações do `SmtpClient`: suporta STARTTLS (587) e SMTP interno, mas **não** TLS implícito (465)
nem OAuth2 (Microsoft 365/Gmail modernos). Se o provedor exigir, o envio pode ser trocado por MailKit
implementando `IEnviadorEmail`, sem mexer no resto. Em Windows Server 2012 R2 o TLS 1.2 depende do
SChannel do sistema (validar no servidor).

## 5. Exemplo de configuração do fluxo (sem segredos)

```json
{
  "Pastas": { "Entrada": "C:\\Envia xml", "Processados": "Processados", "Erro": "Erro", "Estado": "Estado", "OrganizacaoErro": "PorCategoria" },
  "Pacote": {
    "IntervaloVarreduraSegundos": 5, "EstabilizacaoSegundos": 3, "JanelaColetaSegundos": 10,
    "TimeoutArquivoSegundos": 300, "BackoffMaximoSegundos": 120, "MaxTransportesConcorrentes": 1
  },
  "Limpeza": { "Habilitada": true, "DiasRetencao": 10 }
}
```

## 6. Suposições e pontos em aberto

- O texto da resposta da I50 para duplicata contém "já existe" (não verificado no WMS). Se for outro,
  ajuste só `ClassificadorRetorno.EhJaExiste`.
- Um reprocesso "já existe" vai para `Processados`, mas aparece no relatório como não integrado, com
  a mensagem "Já existia no WMS (reprocesso)".
- Nota tardia de um transporte já enviado no mesmo pacote vai em lote posterior com o mesmo
  `nroagrupseparacao`; o WMS pode bloquear se a onda já saiu de "editando".
- `Pastas:Estado` e `Relatorio:PastaSaida` não podem ficar iguais nem dentro de `Processados`/`Erro` (a limpeza apagaria o diário e os relatórios); o serviço não inicia nesse caso (erro no log).
- Valores padrão de janela de coleta, backoff e timeout são estimativas a validar em operação.
- Não validado contra WMS ou SMTP reais (somente testes automatizados com fakes).
