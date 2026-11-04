# Operação — watcher de pasta, pacote, erros e e-mail (Etapa 7)

O serviço (hospedado por NSSM) observa a pasta de entrada continuamente, integra os XMLs de NF-e
ao WMS por transporte, move cada arquivo e, ao terminar o pacote, gera o relatório (HTML + PDF) e
envia o resumo por e-mail. A I60 permanece **desligada** (`I60:Habilitada=false`).

## 1. Pastas

Configuradas na seção `Pastas` do `appsettings.json` (override por env, ex.: `Pastas__Entrada`).
`Pastas:Entrada` vazia herda o legado `Entrada:PastaEntrada` (padrão `C:\Envia xml`).
`Processados`, `Erro` e `Estado` relativos resolvem contra a Entrada.

| Pasta | Conteúdo |
|---|---|
| `<Entrada>` | XMLs a processar (somente `*.xml`, topo da pasta). |
| `<Entrada>\Processados` | Notas integradas, pendentes na interface, integradas com produto a revisar e reprocessos "já existe". |
| `<Entrada>\Erro\<Categoria>` | Notas não integradas e XMLs inválidos, mais `arquivo.xml.motivo.txt` com o detalhe. |
| `<Entrada>\Estado` | Diário do pacote em andamento (`pacote-atual.jsonl`). Não apagar com o serviço rodando. |
| `Relatorio:PastaSaida` (padrão `relatorios`) | `pacote_<yyyyMMdd_HHmmss>.html` e `.pdf`. |

Categorias de `Erro` (uma pasta por tipo de erro):

- `XmlInvalido`: o XML não pôde ser lido (inclui arquivo truncado).
- `SemTransporte`: sem "Transporte: N" nas informações complementares.
- `ProdutoNaoCadastrado`: produto continua não cadastrado após as tentativas.
- `EmpresaNaoLocalizada`: empresa não localizada pelo WMS.
- `TentativasEsgotadas`: 3 tentativas sem sucesso e sem causa específica na mensagem.
- `Outro`: demais erros (inclui exceção inesperada ao processar o arquivo).

A mensagem tem prioridade sobre o número de tentativas na classificação.

Para mudar a organização, use `Pastas:OrganizacaoErro`:

- `PorCategoria` (padrão): `Erro\<Categoria>\arquivo.xml`.
- `PorArquivo`: `Erro\<nome-do-arquivo>\arquivo.xml` (uma pasta por nota com erro).
- `Plana`: `Erro\arquivo.xml`.

## 2. Ciclo do pacote

O serviço consulta a pasta a cada `Pacote:IntervaloVarreduraSegundos` (polling; o
`FileSystemWatcher` perde eventos em pasta de rede).

1. **Ocioso**: sem XML. Ao surgir o primeiro, abre o pacote (**Coletando**).
2. **Coletando**: espera `JanelaColetaSegundos` sem arquivo novo, para que uma cópia em massa chegue inteira.
3. **Processando**: a cada ciclo envia os arquivos prontos. Um arquivo só é "pronto" quando tamanho e
   data ficam iguais por `EstabilizacaoSegundos` e o arquivo abre com acesso exclusivo.
   Arquivos novos entram no mesmo pacote.
4. **Fechando**: quando a entrada não tem pendentes e passaram `QuietudeSegundos` sem arquivo novo ou
   processado, o pacote fecha: grava HTML + PDF (sempre), envia o e-mail conforme a política, apaga o
   diário e volta a **Ocioso**. Chegada de arquivo durante a quietude mantém o pacote aberto.

Envio por transporte: as notas do mesmo transporte são enviadas juntas e em sequência. Transportes
distintos rodam em paralelo até `Pacote:MaxTransportesConcorrentes` (padrão **1**; **2** é seguro).
A ordem de chegada é criação do arquivo e depois nome.

Arquivos que não ficam prontos em `TimeoutArquivoSegundos` geram aviso no log, permanecem na entrada
e **não bloqueiam** o fechamento; entram no próximo pacote quando liberados.

Falhas: exceção em um arquivo ou transporte não derruba os demais (o arquivo vai para `Erro\Outro`).
Se o relatório não puder ser gravado, o pacote continua em **Fechando** e tenta de novo no ciclo
seguinte (o diário só é apagado após o relatório gravado). Falha de e-mail apenas é logada.

Reinício do serviço: o pacote é reaberto a partir do diário. Uma nota em voo na queda pode ser
reenviada; o WMS responde "já existe" e ela segue para `Processados`, com a mensagem iniciada por
"Já existia no WMS (reprocesso)".

## 3. Limpeza

`Limpeza:Habilitada` (padrão `true`) e `Limpeza:DiasRetencao` (padrão **10**). Roda na partida e a cada 24 h.
A idade é a do `LastWriteTimeUtc`, que a movimentação carimba com a data da movimentação.
Só apaga dentro de `Processados` e `Erro`; nunca toca a Entrada nem `relatorios`. Recusa configuração
perigosa (Processados/Erro iguais, dentro um do outro, iguais à Entrada ou raiz de drive) e o serviço
não inicia com `Pastas` inválida (erro no log).

## 4. E-mail

Resumo no corpo (contagens, período e tabela de erros/pendências, no máximo 20 linhas) e PDF detalhado
anexo. `Email:EnviarQuando`: `ComProblema` (padrão: há não integrada, pendente, produto a revisar ou XML
inválido), `Sempre` ou `Nunca`. Sem configuração, o serviço só registra no log onde o relatório foi salvo.

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
    "EnviarQuando": "ComProblema",
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
    "QuietudeSegundos": 30, "TimeoutArquivoSegundos": 300, "MaxTransportesConcorrentes": 1
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
- Valores padrão de quietude, janela de coleta e timeout são estimativas a validar em operação.
- Não validado contra WMS ou SMTP reais (somente testes automatizados com fakes).
