# Design técnico: retorno de separação (I51) como gatilho da I60

Status: design para revisão. Nada implementado. Fontes: `.agents/tasks/brief-i51-i60.md` (vence em conflito) e
`docs/ESCOPO-I51-I60.md`. Código lido: `ClienteWms\*`, `ConstrutorI60`, `OpcoesI60`, `Orquestrador`,
`ProcessadorLote`, `GerenciadorPacote`, `OrganizadorArquivos`, `RegistroOndasTsp`, `DiarioPacote`,
`Monitoramento\*` (agendador, marca, enviador do resumo semanal), `Relatorio\*`, `Email\*`, `Devolucao\*`,
`Worker\*`, `appsettings.json`, testes e fakes existentes.

## 1. Visão geral

Hoje a I60 sai dentro do `Orquestrador`, logo depois da I50 aceita (só quando `I60:Habilitada=true`). Como o
doccarga ainda não foi separado, o WMS não associa. O design cria um fluxo assíncrono em três peças:

1. **Registro**: quando a I50 é aceita, o `ProcessadorLote` registra a nota num estado persistente
   (`Estado\i51\notas\<chave>.json`), guarda uma cópia do XML da NF-e e o `Orquestrador` deixa de enviar a I60
   (somente com `I51:Habilitada=true`).
2. **Consulta periódica**: um `BackgroundService` no Worker (a cada `I51:IntervaloMinutos`, padrão 5) chama a
   I51 com `TIPO=TODOS`, grava a resposta bruta em disco **antes** de processar, casa os documentos com as
   notas aguardando e marca as notas como `Separada`. `TIPO=UNICO` (CNPJ + número) recupera notas atrasadas.
3. **I60 depois da separação**: para cada nota `Separada`, monta a I60 com o **mesmo XML** da I50, com
   quantidades ajustadas às separadas, envia como `INE` e trata o resultado. Doccarga encerrado (ENC) reenvia
   como `IMP` e registra o erro.

Em paralelo: relatório diário às 12:00 (HTML + PDF + e-mail) e a tela de devolução I40 mantida pronta atrás de
`Devolucao:Habilitada=false`, protegida por testes de regressão.

Com `I51:Habilitada=false` (padrão) nenhum código novo executa no caminho da I50/I60: o comportamento permanece
idêntico (garantido por testes, seção 16).

## 2. Stack (travada na aprovação)

.NET 8, C# com nullable, `System.Text.Json`, `Microsoft.Extensions.*` (Options, Logging, Http, Hosting), xUnit
(já usado em `tests\KitWmsIntegrador.Tests`), PdfSharpCore 1.3.67 (já usado), SMTP via `IEnviadorEmail`
existente. **Sem pacote NuGet novo, sem banco de dados.** Persistência em arquivos JSON no `Estado\`, no mesmo
padrão de `RegistroOndasTsp` (escrita em `.tmp` + `File.Move(overwrite)`, nunca lança por falha de disco).

## 3. Decisões principais e alternativas descartadas

**D1. Estado em um arquivo JSON por nota, com índice em memória.** Alternativas: (a) um único `estado.json`
(reescrever tudo a cada transição, risco de arquivo grande e corrupção total); (b) JSONL append-only como o
diário (a consulta por situação exige varrer e compactar). Escolhido: um arquivo por nota
(`Estado\i51\notas\<chave>.json`), escrita atômica, e um dicionário em memória carregado na partida. A
varredura de diretório só acontece na partida e na limpeza. Corrompido: o arquivo é preservado como
`.corrompido-<carimbo>` e logado (padrão de `RegistroOndasTsp.Preservar`).

**D2. Cópia do XML em `Estado\i51\xml\<chave>.xml`.** O brief diz "mesma NF-e da I50, já guardada pelo
integrador". Hoje o XML sai da entrada para `Processados\<pacote>\` logo após a nota (nome pode ganhar sufixo
`_n`, e a limpeza apaga em 60 dias). Uma nota pode ficar aguardando separação por dias. Escolhido: copiar no
registro (antes do `Finalizar` mover o arquivo) para uma pasta que a `LimpadorPastas` não toca. Fallback na
leitura: se a cópia sumir, `LocalizadorXmlDevolucao.Localizar(chave)` em `Processados`. Sem nenhum dos dois:
`Rejeitada` com categoria `XmlIndisponivel`. O XML não vai dentro do JSON de estado (só o caminho relativo).

**D3. Cliente bruto separado: `IClienteWmsBruto`.** A I51 precisa de (1) identificador configurável (não cabe
no enum `InterfaceWms`) e (2) o corpo bruto da resposta para gravar em disco antes de desserializar. Alternativa
descartada: novo membro em `IClienteWms` (quebraria os 4 fakes dos testes: `FakeClienteWms`, `ClienteComLatencia`,
`ClienteQueFalha`, `FakeClienteWmsCarga`) e membro com implementação padrão (esconde erro de configuração).
Escolhido: interface nova `IClienteWmsBruto { Task<string> PostBrutoAsync(string identificadorEdi, string jsonBruto, CancellationToken) }`
implementada por `ClienteWmsHttp`. `EnviarAsync` passa a chamar um núcleo privado compartilhado
(`PostCoreAsync`), preservando token, retry, backoff e a query string de 5 parâmetros.

**D4. Uma única instância de `ClienteWmsHttp` no Worker.** O código atual comenta que 401 transitório no
`GetToken` vem de concorrência de sessão no WMS. `AddHttpClient<IClienteWms, ClienteWmsHttp>` registra o cliente
como transient: o `Orquestrador` já tem uma instância e o novo serviço teria outra, com token próprio. Escolhido:
registrar `AddHttpClient<ClienteWmsHttp>(mesma configuração)` e expor `IClienteWms` e `IClienteWmsBruto` por
`AddSingleton(sp => sp.GetRequiredService<ClienteWmsHttp>())`. Mesmo handler (TLS 1.2/1.3), mesmo timeout;
o comportamento do envio não muda.

**D5. Duas chaves independentes.** `I51:Habilitada` liga registro, consulta e relatório. `I60:Habilitada` decide se
a I60 é enviada (por qualquer caminho). Combinações: I51=false: igual ao de hoje. I51=true e I60=false:
acompanha a separação (estados até `Separada`) sem enviar I60 (rollout gradual e útil para o homolog). I51=true e
I60=true: fluxo completo. Com I51=true o `Orquestrador` nunca envia a I60 junto da I50.

**D6. `Atrasada` é uma situação, não um filtro.** Segue o brief. Transição `AguardandoSeparacao → Atrasada`
quando `agora − RegistradaEm > HorasParaAtraso`; a nota atrasada continua casando com o TODOS e entra no
UNICO de recuperação.

**D7. Visão por pacote: seção dentro do relatório diário.** Avaliado e decidido, ver seção 11.4.

**D8. Listas de padrões como texto separado por `;`**, não `IList<string>`. O binder do .NET acrescenta itens à
lista padrão em vez de substituir (por isso `OpcoesPasta.ExtensoesTemporarias` repete os defaults no
appsettings). Texto com `;` (padrão de `Email:Destinatarios`) permite override por variável de ambiente sem
surpresa.

## 4. Configuração

Todas as chaves aceitam override por variável de ambiente (`I51__Habilitada` etc.). Nenhuma contém segredo.

### 4.1 Seção `I51` (`Configuracao\OpcoesI51.cs`, nova)

| Chave | Padrão | Regra |
|---|---|---|
| `Habilitada` | `false` | Liga registro, consulta e relatório. |
| `Identificador` | `WEBAPI-I51` | Obrigatório; 1 a 64 caracteres de `[A-Za-z0-9_-]`. Vai escapado na query. |
| `IntervaloMinutos` | `5` | Inteiro de 1 a 1440. |
| `AtrasoInicialSegundos` | `20` | 5 a 3600 (achado 6 da 2ª revisão: mínimo de 5 s). Espera antes do 1º ciclo (deixa o Worker de pacote subir). |
| `CampoUnico` | `Numero` | `Numero` ou `NroDocumento` (achado 1 da 2ª revisão). Campo da CONSULTA que recebe o número da nota no `UNICO`; o outro usa `ValoresNeutros`. Padrão `Numero` porque o usuário disse "só com o CNPJ e o número". Pergunta Q13. |
| `PadroesSemRetorno` | vazio | Textos (separados por `;`, `TextoNormalizado`) aceitos na `mensagem` de um `sucesso=N` sem documentos. Vazio = todo `N` sem documentos é "nada a retornar" (como hoje). Preenchido: `N` cuja mensagem não casar conta como falha de consulta (5.4; achado 7). |
| `HorasParaAtraso` | `24` | Inteiro de 1 a 720. |
| `CnpjConsulta` | vazio | Vazio usa `Operacional:CnpjDepositante`. Com 14 dígitos (máscara livre). |
| `IntervaloUnicoMinutos` | `60` | 5 a 1440. Mínimo entre dois UNICO da mesma nota atrasada (evita martelar o WMS a cada ciclo de 5 min). |
| `MaxUnicoPorCiclo` | `10` | 0 a 200. Limita a carga por ciclo; 0 desliga a recuperação automática. |
| `DiasRetencaoEstado` | `60` | 7 a 3650. Aplica a notas terminais (inclui `I60Enviada`), XML copiado e brutos processados. |
| `ItemSemRetorno` | `Zerar` | `Zerar` ou `Omitir`: item da nota que o WMS não devolveu. |
| `MaxTentativasProcessarBruto` | `3` | 1 a 20. |
| `ValoresNeutros:Numero/Serie/ChaveNFe/NroDocumento` | `0` / `0` / vazio / `0` | Campos da CONSULTA que não usamos. Mantidos configuráveis porque o manual do fornecedor não deixa claro o valor "neutro" aceito (pergunta Q4). |

Removidos em relação à primeira versão (achado 10 da revisão): `GravarRespostasVazias` (a resposta `sucesso=N` sem
documentos não gera arquivo; só log) e `QuantidadeSeparadaUsa` (a I60 atual usa a mesma quantidade 1:1 nos três
campos; todos passam a usar `qtdeProdutoSep`, ver 9.1). `IntervaloUnicoMinutos`, `MaxUnicoPorCiclo` e
`ValoresNeutros` ficam porque limitam carga no WMS ou cobrem a pergunta aberta Q4.

`Validar()` devolve a lista de problemas. Com `Habilitada=true` e qualquer problema, o `Worker` registra o erro
e chama `StopApplication()` (mesmo padrão da validação de `Pastas`/`Zip`): ligar a I51 com configuração
inválida suprimiria a I60 sem acompanhar a nota, então falhar alto na partida é a opção segura.

### 4.2 Seção `I60` (`OpcoesI60`, ampliada; defaults preservam o comportamento atual)

| Chave | Padrão | Uso |
|---|---|---|
| `Habilitada` | `false` | Inalterada. |
| `TipoOperacaoNota` | `INE` | Inalterada. Operação normal. **Nunca `IMB`.** |
| `TipoOperacaoEncerrado` | `IMP` | `tipoOperacaoNota` e `Tpsit` do reenvio quando o doccarga está ENC. |
| `ReenviarEncerradoComoImp` | `true` | Liga a regra ENC → IMP. |
| `PadroesDocCargaEncerrado` | `encerrad;status ENC` | Casamento por texto normalizado (ver abaixo), em qualquer mensagem ou ressalva do retorno. |
| `PadroesQuantidadeDiferente` | `não confere com a quantidade;quantidade diferente;quantidade caixa` | Classifica `QuantidadeDiferente`. |
| `PadroesVolumeDiferente` | `não confere com a qtdvolume;Total de volume` | Classifica `VolumeDiferente` (erro real do Plano A, `HISTORICO-PLANO-A.md` 2.3/2.4). |
| `PadroesExigeUpd` | `TpAcao precisa ser UPD` | Resposta "Nota [...] já existe, TpAcao precisa ser UPD!". |
| `PadroesRessalvaBloqueante` | `encerrad;status ENC;não confere;quantidade;Total de volume` | Ressalva (bloco `Ressalvas : {...}`) que torna a I60 recusada. Ressalva que não casa é não bloqueante. |
| `PadroesAssociadaSucesso` | vazio | Textos extras que, além de `ClassificadorRetorno.EhSucesso`, indicam I60 associada (pergunta Q11). |
| `TpAcaoReenvioImp` | `UPD` | `I` ou `UPD`: `tpacao` do reenvio IMP. Padrão `UPD` porque a INE recusada por ENC já importou a nota na interface do WMS (`respostas-homolog.txt`), então `I` bateria em "já existe". |
| `ReenviarComUpdSeJaExiste` | `true` | Se a resposta casar `PadroesExigeUpd` e o `tpacao` usado não era `UPD`, reenvia no máximo uma vez com `tpacao=UPD`. |
| `QtdeVolumeOrigem` | `SomaCaixas` | `SomaCaixas` (comportamento atual: soma das quantidades) ou `VolumesI51` (contagem de `volumes[]` da I51). Pergunta Q12. |
| `MaxFalhasComunicacaoAlerta` | `10` | Só gera alerta na saúde do relatório; nunca torna a nota terminal (ver 9.3). |
| `BackoffMaxMinutos` | `60` | Teto do backoff exponencial entre reenvios após falha de comunicação. |
| `Max4xxConsecutivos` | `3` | **[OPCIONAL, cortável]** HTTP 4xx definitivo (exceto 401/403/408/429) seguidos na mesma nota antes de `Rejeitada`/`ErroDefinitivoHttp`. Se cortado: 4xx conta como falha de comunicação comum (nunca terminal). |
| `MaxTentativasOutro` | `3` | Resposta da I60 que não casa nenhum padrão (`Outro`) é reenviada até N vezes, com o mesmo backoff, antes de `Rejeitada`/`Outro` (achado 3 da 2ª revisão). |

**Validação (`OpcoesI60.Validar()`, achado 4 da 2ª revisão).** Chamada na partida só quando `I51:Habilitada=true`
(com a chave desligada o comportamento atual da I60 não muda e nada é validado além do que já é hoje). Regras:
`TpAcaoReenvioImp` ∈ {`I`,`UPD`}; `QtdeVolumeOrigem` ∈ {`SomaCaixas`,`VolumesI51`}; `TipoOperacaoNota` e
`TipoOperacaoEncerrado` não vazios e com 3 letras; `MaxFalhasComunicacaoAlerta` 1..1000; `BackoffMaxMinutos` 1..1440;
`Max4xxConsecutivos` 1..20 (se mantido); `MaxTentativasOutro` 1..20; listas `Padroes*` sem item vazio (item vazio é
ignorado em runtime, o problema é apenas reportado) e `PadroesDocCargaEncerrado` com ao menos um item quando
`ReenviarEncerradoComoImp=true`. Cada regra tem teste em `OpcoesI60Tests` (arquivo de teste novo `OpcoesI60ValidarTests`)
e `OpcoesI51Tests`.

**Normalização de texto (`TextoNormalizado.Normalizar`)**, usada em todos os `Padroes*`: `s.Normalize(NormalizationForm.FormD)`,
remove caracteres `UnicodeCategory.NonSpacingMark`, `ToLowerInvariant()`, colapsa espaços repetidos. Aplicada à
mensagem e a cada padrão, depois `Contains(Ordinal)`. Testes: "encerrado" × "ENCERRADO" × "Encerrádo" × "não confere"
× "nao confere". Padrões vazios são ignorados (nunca casam tudo).

**Parser de ressalvas (`ParserRessalvas`)**: para cada mensagem do WMS, extrai o texto entre `Ressalvas :` e o fim, e
divide os itens por blocos `{...}`. Ex.: `Foi importado e esta pendente na interface. Ressalvas : {Nota [...] já existe, TpAcao precisa ser UPD!}`
vira ressalvas = [`Nota [...] já existe, TpAcao precisa ser UPD!`]. Sem o marcador, a lista é vazia.

### 4.3 Seção `RelatorioI60` (`Configuracao\OpcoesRelatorioI60.cs`, nova)

`Habilitado=true` (só atua com `I51:Habilitada=true`), `Hora="12:00"` (HH:mm local; inválida cai para 12:00 como
`OpcoesMonitoramento.HoraDoDia`), `AssuntoPrefixo="[KitWms]"`, `CaminhoDestinatarios` (vazio =
`<telemetria>\destinatarios-i60.txt`), `ListarCandidatasI40=true`, `EnviarQuandoVazio=false`,
`MaxLinhasPorSecao=300`.

`OpcoesRelatorioI60.Validar()` (achado 4): `Hora` em formato `HH:mm` válido (inválida é problema reportado, e em
runtime cai para 12:00); `MaxLinhasPorSecao` 10..5000; `AssuntoPrefixo` até 40 caracteres sem quebra de linha.
Só é validada na partida quando `I51:Habilitada=true` e `RelatorioI60:Habilitado=true`. Testes em
`OpcoesRelatorioI60Tests`.

### 4.4 `Devolucao`

Sem mudança de chave. `Devolucao:Habilitada` continua `false` no `appsettings.json`.

## 5. Cliente WMS da I51

### 5.1 Arquivos

- `ClienteWms\IClienteWmsBruto.cs` (novo).
- `ClienteWms\ClienteWmsHttp.cs` (alterado): implementa `IClienteWmsBruto`; `EnviarAsync(InterfaceWms, ...)` vira
  `DesserializarResposta(await PostCoreAsync(id.ParaIdentificador(), json, ct))`; `MontarQueryString(string)`
  substitui a versão com enum. A query continua com os 5 parâmetros (`IDENTIFICADOR`, `REQUISITANTE`,
  `CNPJREQUISITANTE`, `DESTINO`, `CNPJDESTINO`) e o log de sucesso passa a registrar o identificador em texto
  (sem corpo). Os testes de `ClienteWmsHttpTests` seguem como rede de proteção.
- `Separacao\ClienteI51.cs` (novo): `ConsultarTodosAsync` e `ConsultarUnicoAsync(cnpj, numero)`, ambos devolvem
  o **corpo bruto** (`string`). Monta o JSON com `JsonWms.Opcoes`.
- `Separacao\Dtos\RequisicaoI51.cs` e `RespostaI51.cs` (novos) e `Separacao\ParserI51.cs`.

### 5.2 Requisição

```json
{"CONSULTA":{"TIPO":"TODOS","CNPJ":"01.838.723/0098-50","numero":"0","serie":"0","chaveNFe":"","nroDocumento":"0"}}
```

`TIPO` é `TODOS` ou `UNICO` (constantes; nenhum outro valor existe no código). `CNPJ` sai com máscara
(`FormatoWms.Documento`). No UNICO, `numero` = número da nota com zeros à esquerda até 9 dígitos
(`FormatoWms.ZerosEsquerda(numero, 9)`) no campo indicado por `I51:CampoUnico` (padrão `numero`; com
`NroDocumento` o número vai em `nroDocumento`, sem zeros à esquerda, até 20 dígitos); o outro campo e os demais usam
`ValoresNeutros`. O brief mostra `nroDocumento` preenchido no exemplo do manual e o usuário disse "só CNPJ e número":
a dúvida é real, por isso é configuração (Q13) e o roteiro do homolog testa as duas variantes. Validação do UNICO:
número somente dígitos, 1 a 9 caracteres (`Numero`) ou 1 a 20 (`NroDocumento`); fora disso a nota não é consultada,
recebe `Observacao` e é logada.
O `IDENTIFICADOR` da query vem de `I51:Identificador`.

### 5.3 Resposta

`RespostaI51 { transacao, sucesso, mensagem, documentos[] }`; `DocumentoI51 { CnpjProprietario, CnpjDestinatario,
CodDestinatario, Numero, Tipo, SubTipo, Placa, Doca, itens[] }`; `ItemI51 { numeroSequencialItem, codProduto,
codProdCliente, CodEmbalagem, qtdeProduto, qtdeProdutoSep, qtdeEmbalagem, qtdeEmbalagemSep }`. De `volumes[]`
lê-se **somente a contagem** (`QtdeVolumesI51 = volumes.Count`, gravada no estado e usada por `QtdeVolumeOrigem=VolumesI51`,
achado 3). Etiquetas, lotes, validades e `detalhes[]` não são lidos nem gravados. Desserialização com opções próprias (`PropertyNameCaseInsensitive`,
`NumberHandling = AllowReadingFromString`, propriedades desconhecidas ignoradas). Quantidades ficam `string`
no DTO e viram `decimal` com `NumberStyles.Number` + `CultureInfo.InvariantCulture` ("24.0000").

### 5.4 Tratamento do corpo

1. **Espiada** com `JsonDocument`: se for JSON válido, `sucesso` for `N` e não houver documentos, o resultado é
   `NadaARetornar` (**não é erro**): sem arquivo bruto, log
   `Debug`; a `mensagem` é logada em `Information` apenas quando muda em relação ao ciclo anterior (corpo não
   é logado, mensagem truncada em 200 caracteres). Exceção (achado 7): se `I51:PadroesSemRetorno` estiver
   preenchida e a mensagem do `N` não casar nenhum padrão (`TextoNormalizado`), o resultado é `FalhaConsulta`:
   `FalhasConsecutivas++` e `LogWarning` com a mensagem truncada (provável EDI mal configurado ou CNPJ errado). Com a
   lista vazia (padrão) o comportamento é o descrito acima. O roteiro registra a mensagem real do homolog para
   preencher a lista.
2. Qualquer outro corpo (documentos presentes, `sucesso=S`, JSON inválido) é gravado em
   `Estado\i51\brutos\pendentes\` **antes** de qualquer desserialização completa (seção 7.2).
3. A desserialização completa só acontece sobre o arquivo gravado.

## 6. Estado persistente

### 6.1 Modelo (`Separacao\NotaSeparacao.cs`, record imutável)

Identidade: `Chave` (44 dígitos; se faltar, chave sintética `nf-<numero>-<serie>-<cnpjDest>`), `Numero`, `Serie`,
`CnpjDestinatario`, `CnpjProprietario`. Origem: `Tipo` (`PUXADA`/`TSP`), `NumeroTransporte`, `Onda`
(nome da onda TSP; vazio na Puxada), `IdentificadorPacote`, `CaminhoXml` (relativo a `Estado\i51\`),
`XmlCopiado`. Fluxo: `Situacao`, `RegistradaEm`, `AtrasadaEm`, `SeparadaEm`, `I60EnviadaEm`, `FinalizadaEm`,
`AtualizadoEm` (UTC). Controle: `TentativasI60` (envios feitos), `FalhasComunicacao` (falhas seguidas, zera ao obter resposta),
`TentativasOutro` (respostas `Outro` seguidas), `UltimaTentativaI60Em`, `ProximaTentativaI60Em` (backoff), `UltimoUnicoEm`, `TentativasUnico`,
`BrutoOrigem` (nome do arquivo bruto que a separou), `QtdeVolumesI51`, `Observacao`. Resultado da I60:
`OperacaoI60` (`INE`/`IMP`), `TpAcaoI60`, `TransacaoI60`, `IdDocumentoI60`, `Ressalvas` (lista, até 5 itens de
200 caracteres), `Motivo` (enum `MotivoI60`) e `Mensagem`
(truncada em 500 caracteres; **nunca** o corpo). Itens: lista de `ItemSeparacao { Sequencia, CodProduto,
QtdeOriginal, QtdeSeparada?, Diferenca }`. `Versao = 1`.

### 6.2 Situações e transições

A cadeia segue o brief (req. 2): `AguardandoSeparacao → Separada → I60Enviada → Associada | Rejeitada`, com
`Atrasada` à parte. Não existe a situação `ImportadaImp` da primeira versão: o ENC → IMP termina em
`I60Enviada` com `OperacaoI60=IMP` e `Motivo=DocCargaEncerrado` (achado 2).

```
AguardandoSeparacao ──(idade > HorasParaAtraso)──> Atrasada
AguardandoSeparacao / Atrasada ──(I51 retornou)──> Separada
Separada ──(I60 aceita pelo WMS)──────────────────> I60Enviada
Separada ──(recusa classificada / sem quantidade / XML)──> Rejeitada     (terminal, com motivo)
I60Enviada ──(I60 INE com sucesso confirmado)─────> Associada            (terminal)
Rejeitada ──(comando manual --reenfileirar-i60)───> Separada             (única saída de Rejeitada)
```

**Reenfileirar (achado 3 da 2ª revisão).** `Rejeitada → Separada` só ocorre por comando manual, nunca
automaticamente. A CLI `ReenfileirarI60` (`--reenfileirar-i60 --chave <chave>` ou `--motivo <MotivoI60>`)
zera `TentativasI60`, `TentativasOutro`, `FalhasComunicacao`, `ProximaTentativaI60Em`, limpa `FinalizadaEm`, `Motivo`
e `Mensagem` e acrescenta em `Observacao` "reenfileirada em <data>". Sai com código 0 (reenfileirou N), 1 (erro de disco),
2 (parâmetro faltando ou nenhum registro). O uso típico: o texto do WMS era novo, ajustou-se `I60__Padroes*` e
reenfileirou-se por `--motivo Outro`. A tabela de transições do repositório inclui esta única aresta, marcada como
"manual" e só aceita pelo método `Reenfileirar(chave)` (o `Atualizar` genérico a recusa).

- `I60Enviada` = o WMS aceitou a chamada (sucesso, ou "importado e pendente na interface" sem ressalva bloqueante).
  O WMS devolve `sucesso="N"` + "pendente na interface" em todas as respostas reais já vistas, então este é o estado
  esperado da maioria das notas, **não** um alerta.
- `Associada` só nasce quando a operação foi `INE` e a resposta é sucesso (`ClassificadorRetorno.EhSucesso` ou
  `PadroesAssociadaSucesso`); a transição é `Separada → I60Enviada → Associada` gravada em dois `Atualizar`
  consecutivos (write-ahead: o estado `I60Enviada` fica em disco antes de promover). Um `IMP` bem-sucedido nunca
  vira `Associada` (o `IMP` não associa).
- `I60Enviada` e `Associada` são terminais para a limpeza: `FinalizadaEm = I60EnviadaEm` e as notas saem após
  `DiasRetencaoEstado`. O relatório mostra a **idade em `I60Enviada`** e agrupa por `Motivo`/`OperacaoI60`.
  Como não há consulta do WMS que confirme a associação depois, a promoção `I60Enviada → Associada` só ocorre no
  envio; confirmar o sinal real de associação é a pergunta Q11.

Enum `MotivoI60`: `Nenhum`, `DocCargaEncerrado`, `QuantidadeDiferente`, `VolumeDiferente`, `SemQuantidadeSeparada`,
`JaExiste`, `PendenteNaInterface`, `PendenteComRessalva`, `XmlIndisponivel`, `ErroDefinitivoHttp`, `Outro`.
(`FalhaComunicacao` deixou de ser motivo terminal; comunicação não rejeita nota, ver 9.3.)

O repositório valida a tabela de transições; transição inválida lança `InvalidOperationException` dentro do
repositório (erro de programação) e o serviço captura, loga e segue para a próxima nota.

### 6.3 Arquivos em `Estado\i51\`

| Caminho | Conteúdo |
|---|---|
| `notas\<chave>.json` | Um estado por nota (nome sanitizado para `[0-9A-Za-z_-]`). |
| `xml\<chave>.xml` | Cópia do XML da NF-e (runtime, fora do repositório git). |
| `orfaos\<numero>_<cnpjDest>.json` | Documento do TODOS/UNICO sem nota correspondente (achado 2): só `Numero`, CNPJs, `QtdeVolumesI51`, quantidades por item (`numeroSequencialItem`, `codProduto`, `qtdeProdutoSep`) e carimbo. Sem corpo. Expira pela retenção. |
| `brutos\pendentes\i51_<TIPO>_<yyyyMMdd_HHmmssfff>[_<numero>].json` | Resposta bruta ainda não processada. |
| `brutos\processados\<yyyyMMdd>\...` | Já processada (retenção `DiasRetencaoEstado`). |
| `brutos\falhos\...` | Excedeu `MaxTentativasProcessarBruto`; aparece no relatório. |
| `consulta.json` | Saúde: `UltimaTentativaEm`, `UltimaConsultaOkEm`, `FalhasConsecutivas`, `UltimaMensagemVazia`. |

`Estado` já é protegido contra limpeza por `OpcoesPasta.Validar` (não pode ficar dentro de Processados/Erro).
Os brutos têm CNPJs e quantidades: ficam no `Estado` com a mesma ACL local das demais pastas; nunca vão para log.

### 6.4 Repositório (`Separacao\IRepositorioSeparacao.cs` e `RepositorioSeparacaoArquivo.cs`)

- **Carga preguiçosa (achado 5):** o construtor não toca o disco. O índice é carregado (e `Estado\i51\` criado) no
  primeiro `Registrar`/`Listar`/`Atualizar`. Com `I51:Habilitada=false` ninguém chama o repositório, então nenhuma
  pasta é criada e nenhum arquivo é lido.
- `Registrar(NotaSeparacao)`: idempotente por `Chave` (já existe: retorna `false`, loga `Information`).
- `Atualizar(chave, Func<NotaSeparacao, NotaSeparacao>)`: lê do índice, aplica, valida transição, grava arquivo
  `.tmp` + `File.Move(overwrite)`, só então troca o índice. Retorna `bool`. Falha de disco: índice permanece como
  estava (memória = disco), `LogError`, retorna `false`; **nunca lança** por I/O.
- `Listar()` / `ListarPorSituacao(...)`: devolve cópia (snapshot). Um `lock` protege o índice, porque
  `ProcessadorLote` registra a partir de várias ondas em paralelo e o serviço de ciclo lê/atualiza.
- `Limpar(DateTimeOffset agora)`: apaga notas terminais com `FinalizadaEm` mais antiga que `DiasRetencaoEstado`
  (e o XML copiado) e brutos processados vencidos. Notas não terminais nunca são apagadas automaticamente.

## 7. Registro na I50 e desligamento da I60 antiga

### 7.1 `Orquestrador` (alteração mínima)

Novo parâmetro opcional no final do construtor: `IOptions<OpcoesI51>? opcoesI51 = null` (null = padrão
`Habilitada=false`; os cinco construtores de teste que montam o `Orquestrador` continuam compilando).
No ponto "sucesso da I50": `if (_opcoesI60.Habilitada && !_i51.Habilitada) { ...I60 como hoje... }` e, quando
`I60.Habilitada && I51.Habilitada`, `historico.Add("I60 adiada: será enviada após o retorno da separação (I51).")`.

### 7.2 `ProcessadorLote` (registro)

Novo parâmetro opcional no final do construtor: `IRegistradorSeparacao? separacao = null`. Em
`ProcessarOndaAsync`, depois de obter `resultado` e **antes** de `Concluir(...)` (que move o arquivo), chamar
`RegistrarSeparacao(nota, resultado, arquivo, ctx, transporte, identificadorPacote)` dentro de `try/catch`
que só loga. Condição de chamada: `separacao is not null && DestinoDe(resultado) == Processados && (resultado.Integrada
|| resultado.Estado == Pendente || ClassificadorRetorno.EhJaExiste(resultado.UltimaMensagem))` (achado 6).
Cobre: (1) I50 aceita (`Integrada`, inclusive `Pendente` com produto provisório); (2) I50 que voltou "importado e
pendente na interface" (ramo (e) do `Orquestrador`, `Integrada=false`, `Estado=Pendente`), que depois de resolvido no
WMS vira doccarga e separa; (3) reprocesso após queda entre a I50 aceita e o `Registrar` (a janela existe porque o
registro acontece depois de `IntegrarAsync`): no reinício a I50 volta "já existe" e a nota entra no acompanhamento.
`Registrar` é idempotente por `Chave`, então o reprocesso de nota já acompanhada não duplica nem retrocede o estado.
Notas rejeitadas (destino Erro) não registram.

**Desligamento (achado 5):** `RegistradorSeparacao.Registrar` é a **primeira** instrução a testar
`OpcoesI51.Habilitada` e retorna imediatamente quando `false`, sem tocar o repositório, o disco ou o XML. O
`IRegistradorSeparacao` é registrado sempre na DI, mas com a chave desligada é um no-op. `ServicoSeparacaoI51` e
`ConsultaI51Worker` também testam a chave antes de qualquer acesso ao repositório; o relatório diário não é
agendado. Teste: `ProcessadorLote` real com `RegistradorSeparacao` real e `I51:Habilitada=false` não cria
`Estado\i51`.

`RegistradorSeparacao` (em `Separacao\`):
1. `Chave` válida? Senão chave sintética.
2. Copia o XML (`File.Copy` para `.tmp` + `File.Move(overwrite)`). Falha: loga `Error`, segue com
   `XmlCopiado=false`.
3. Monta `ItemSeparacao` com `Sequencia = índice + 1` (a mesma sequência que `ConstrutorI50` e `ConstrutorI60`
   usam), `CodProduto`, `QtdeOriginal = qCom`.
4. `repositorio.Registrar(...)`. Falha: `Error` (a nota fica sem acompanhamento; ver riscos).

TSP: `Tipo = "TSP"`, `Onda = ctx.Onda`, `NumeroTransporte = nota.NumeroTransporte ?? transporte`. Puxada:
`Tipo = "PUXADA"`, `NumeroTransporte = transporte`. Os arquivos continuam indo para `Processados\<pacote>\`
exatamente como hoje: a tela I40 depende disso (`LocalizadorXmlDevolucao`).

## 8. Serviço de ciclo (`Separacao\ServicoSeparacaoI51.cs`, Core, sem dependência de Hosting)

`CicloAsync(ct)`, executado pelo `ConsultaI51Worker` (seção 9). Sequência por ciclo:

1. `Habilitada=false`: retorna.
2. **Atrasos**: `AguardandoSeparacao` com idade > `HorasParaAtraso` vira `Atrasada`.
3. **Brutos pendentes** (recuperação após queda ou falha): para cada arquivo em `brutos\pendentes\` (mais antigo
   primeiro) executa `ProcessarBruto`. Isso não chama o WMS.
4. **TODOS**: só se existir ao menos uma nota `AguardandoSeparacao`/`Atrasada` (evita consumir documentos que
   não temos como casar). Chama o cliente; falha de rede/HTTP → `FalhasConsecutivas++`, `LogWarning` sem corpo,
   segue o ciclo. Sucesso → segue seção 5.4 (espiada, gravação do bruto, `ProcessarBruto`).
5. **UNICO de recuperação**: até `MaxUnicoPorCiclo` notas `Atrasada` com `UltimoUnicoEm` mais antigo que
   `IntervaloUnicoMinutos` (a mais antiga primeiro). Cada resposta segue o mesmo caminho (bruto → processar).
6. **I60**: para cada nota `Separada` (por `SeparadaEm`), `ProcessadorI60Separacao.EnviarAsync` (seção 9).
7. **Limpeza** uma vez a cada 24 h (`repositorio.Limpar`).
8. Log de resumo do ciclo (`Information`): contagens por situação, documentos recebidos/casados, I60 enviadas.
   Sem nomes de clientes, sem corpos.

### 8.1 `ProcessarBruto(arquivo)`

Lê o arquivo, desserializa e, para cada documento: normaliza `Numero` (`doc.Numero.Trim().TrimStart('0')`; se o
resultado for vazio, por exemplo `"000000000"`, o documento é **ignorado como inválido**, contado em
"documentos inválidos" e logado em `Warning` sem corpo; a nota é comparada com `nota.Numero.TrimStart('0')`, achado
11), filtra por
`CnpjProprietario` (dígitos iguais ao CNPJ de consulta; ausente = aceita), procura notas em
`AguardandoSeparacao`/`Atrasada` com mesmo número. **Desempate determinístico (achado 2):** vários candidatos →
filtra por `CnpjDestinatario` (dígitos); persistindo o empate, casa com a candidata de menor `RegistradaEm` e grava
`Observacao = "ambígua: casada com a mais antiga"` + `LogWarning` com o número (as demais continuam aguardando e
casam com um próximo documento ou o UNICO). Nunca fica nota presa por ambiguidade.
**Órfãos (achado 2):** documento sem candidato (nota ainda não registrada porque a I50 está em andamento ou pendente
na interface, ou nota desconhecida) **não é descartado**: é gravado em `orfaos\` (6.3), contado como "documento sem nota
correspondente" e o arquivo bruto pode ir a `processados`. A cada ciclo (antes do TODOS) e também no `Registrar` de
uma nota nova (via `IRegistradorSeparacao`, que chama `CasadorDocumentoNota.TentarOrfaos(nota)`), os órfãos são
confrontados com as notas aguardando; casou → mesmo caminho abaixo e o órfão é apagado. Órfão que já corresponde a nota
`Separada` é descartado. Expiram pela retenção (`DiasRetencaoEstado`). Casou: `CalculadorAjuste` gera as quantidades separadas
(seção 9.2) e `Atualizar` grava `Separada`, `SeparadaEm`, `BrutoOrigem` e `QtdeSeparada` por item. Mais de um
documento para a mesma nota na mesma resposta: soma por item (log `Information`).

Fim do arquivo: todos os documentos tratados ou ignorados e nenhuma gravação falhou → move para
`processados\<data>\`. Documento inválido (quantidade não numérica, JSON truncado) ou `Atualizar` retornando
`false` → arquivo fica em `pendentes` e é reprocessado no ciclo seguinte (idempotente: nota já `Separada` é
pulada). Após `MaxTentativasProcessarBruto` tentativas (contador em memória; reinício recomeça a contagem) vai
para `falhos\` e a saúde do relatório mostra o alerta. Documentos válidos do mesmo arquivo já foram aplicados.

### 8.2 Retomada após reinício

Tudo o que importa está em disco antes da ação seguinte: o estado das notas, o XML copiado e o bruto. Cenários:
queda depois de gravar o bruto e antes de processar → o passo 3 processa sem chamar o WMS. Queda no meio do
processamento → reprocesso idempotente. Queda depois de gravar `Separada` e antes da I60 → o passo 6 envia.
Queda entre a chamada da I60 e a gravação do resultado → ver 9.4 (at-least-once).

## 9. I60 depois da separação

### 9.1 Construtor (`Construtores\ConstrutorI60.cs`)

`Construir(NotaFiscal)` e `ConstruirJson(NotaFiscal)` permanecem **inalterados** (testes e `EnvioManualI60`).
Novo overload em `IConstrutorI60`/`ConstrutorI60`:

```csharp
PayloadI60 Construir(NotaFiscal nota, ParametrosI60 parametros);
// ParametrosI60: IReadOnlyList<QuantidadeItemI60>? Quantidades (Sequencia, Quantidade),
//                string? TipoOperacaoNota, string? Tpacao, int? QtdeVolume
```

O overload reutiliza o mesmo corpo do método atual com três diferenças: quantidade do item
(`QtdeProduto`, `QtdeProdutoConfirmada`, `QtdeEmbalagem`, os três 1:1 como hoje) vem de `Quantidades` por
`Sequencia`, todas preenchidas com `qtdeProdutoSep` (achado 9: sem opção de embalagem);
`QtdeTotalNota` = soma das quantidades ajustadas; `QtdeVolume` depende de `I60:QtdeVolumeOrigem` (achado 3):
`SomaCaixas` = soma ajustada arredondada como em `CalcularQtdeVolume` (comportamento atual, e
`I60:QtdeVolumeZero` continua valendo); `VolumesI51` = `QtdeVolumesI51` gravada no estado (se for 0 ou ausente,
cai em `SomaCaixas` e grava `Observacao`). O parâmetro novo `ParametrosI60.QtdeVolume` (`int?`) carrega o valor
escolhido. `Tpsit`/`TipoOperacaoNota` = parâmetro ou
`I60:TipoOperacaoNota`; `Tpacao` = parâmetro ou `"I"`. Valor unitário, `ValorTotal` (vNF), pesos, lote,
validade e `pedidos[] = [nota.Numero]` ficam como no XML (premissa A2). Os itens saem sempre na mesma
numeração `i + 1` da I50. `ItemSemRetorno=Omitir` remove o item da lista (a numeração dos demais não muda).

### 9.2 `CalculadorAjuste`

Para cada item da nota, procura o item do WMS por `numeroSequencialItem`; se o `codProduto` divergir (sem
diferenciar maiúsculas) cai para o primeiro item não usado com o mesmo `codProduto`; sem correspondência, o
item é "sem retorno" (`ItemSemRetorno`: `Zerar` = quantidade 0, `Omitir`). Quantidade separada =
`qtdeProdutoSep`; campo vazio vale 0. Diferença =
separada − original, gravada por item no estado (registro original × separado). Se a soma separada da nota é 0:
a I60 **não é enviada**; a nota vai a `Rejeitada` com `SemQuantidadeSeparada` (o WMS não associa nota sem
quantidade e o registro explica o porquê).

### 9.3 Envio e classificação (`Separacao\ProcessadorI60Separacao.cs`)

Para cada nota `Separada` **devida** (`ProximaTentativaI60Em` nula ou vencida; se `I60.Habilitada=false`, o passo
inteiro é pulado e a nota permanece `Separada`):

1. Carrega o XML **antes** de qualquer write-ahead (achado 5): `CaminhoXml` se `XmlCopiado`; fallback
   `new LocalizadorXmlDevolucao(pastas.CaminhoProcessados).Localizar(chave)` (instanciado pelo próprio processador porque a
   classe exige o caminho no construtor e não está na DI), **somente se `Chave` tem 44 dígitos** (a chave sintética
   `nf-...` nunca é passada ao localizador) e dentro de `try/catch (ArgumentException)` tratado como "não achou"
   (`LocalizadorXmlDevolucao.ValidarChave` lança em chave inválida). Parse com `LeitorNotaFiscalXml` (o mesmo da I50).
   Sem XML (ou parse falho): `Rejeitada`/`XmlIndisponivel`, `Error`, **sem consumir `TentativasI60`**.
2. Write-ahead: `Atualizar` incrementa `TentativasI60` e grava `UltimaTentativaI60Em` **antes** de chamar o WMS.
3. Monta a I60 com `INE` (`I60:TipoOperacaoNota`) e `tpacao = I`.
4. Executa o algoritmo abaixo. `Enviar(op, tpacao)` chama `IClienteWms.EnviarAsync(InterfaceWms.I60, ...)`.
   `Ler(resp)` produz `LeituraI60 { Mensagens, Ressalvas }` (mensagens de todos os `retornos[]` e blocos
   `Ressalvas : {...}` extraídos por `ParserRessalvas`). `Casa(x, padroes)` usa `TextoNormalizado` (seção 4.2) sobre
   mensagens **e** ressalvas. Há no máximo 4 chamadas por nota por ciclo (INE, INE-UPD, IMP, IMP-UPD).

```text
resp = Enviar(INE, I)

// A. ENC tem prioridade: a INE recusada por ENC já importou a nota na interface
if (Casa(resp, PadroesDocCargaEncerrado) && ReenviarEncerradoComoImp) return FluxoImp(resp)

// B. Nota já existe na interface (ex.: reenvio após queda, 9.4)
if (Casa(resp, PadroesExigeUpd) && ReenviarComUpdSeJaExiste) {
    resp = Enviar(INE, UPD)                                   // no máximo 1 reenvio
    if (Casa(resp, PadroesDocCargaEncerrado) && ReenviarEncerradoComoImp) return FluxoImp(resp)
}
return FinalizarIne(resp)

FinalizarIne(resp):                                           // primeira regra que casar
  1. Casa(resp, PadroesExigeUpd)                  -> Rejeitada, JaExiste        (UPD já tentado ou desligado)
  2. Casa(resp, PadroesDocCargaEncerrado)         -> Rejeitada, DocCargaEncerrado (ReenviarEncerradoComoImp=false)
  3. Casa(resp, PadroesQuantidadeDiferente)       -> Rejeitada, QuantidadeDiferente
  4. Casa(resp, PadroesVolumeDiferente)           -> Rejeitada, VolumeDiferente
  5. alguma ressalva casa PadroesRessalvaBloqueante -> Rejeitada, Outro (com as ressalvas)
  6. EhSucesso(resp) ou Casa(resp, PadroesAssociadaSucesso)
                                                   -> I60Enviada e em seguida Associada (Motivo Nenhum)
  7. EhPendencia(resp), sem ressalvas              -> I60Enviada, PendenteNaInterface
  8. EhPendencia(resp), só ressalvas não bloqueantes -> I60Enviada, PendenteComRessalva (ressalvas gravadas)
  9. qualquer outra (Outro)                        -> TentativasOutro++; se < MaxTentativasOutro: a nota PERMANECE
                                                     Separada com ProximaTentativaI60Em (mesmo backoff da
                                                     comunicação), Warning com a mensagem truncada;
                                                     se >= MaxTentativasOutro: Rejeitada, Outro (primeira mensagem).
                                                     (achado 3; Rejeitada sai só por --reenfileirar-i60)

FluxoImp(respIne):                                            // regra ENC -> IMP
  tp   = TpAcaoReenvioImp                                      // padrão UPD
  resp = Enviar(IMP, tp)                                       // tipoOperacaoNota e Tpsit = TipoOperacaoEncerrado
  if (Casa(resp, PadroesExigeUpd) && tp != UPD && ReenviarComUpdSeJaExiste) { tp = UPD; resp = Enviar(IMP, UPD) }
  if (Casa(resp, PadroesExigeUpd))                -> Rejeitada, JaExiste (sem novo reenvio)
  else if (EhSucesso(resp) || EhPendencia(resp))  -> I60Enviada, OperacaoI60 = IMP, TpAcaoI60 = tp,
                                                     Motivo = DocCargaEncerrado,
                                                     Mensagem = "Doccarga encerrado (ENC): I60 enviada como IMP
                                                       (tpacao=<tp>), sem associação automática. WMS(INE): <msg>"
  else                                            -> mesmo tratamento do item 9 (tentativas limitadas por
                                                     MaxTentativasOutro; ao esgotar, Rejeitada, Outro com as
                                                     mensagens das duas chamadas)
```

Pontos que a ordem resolve (achado 1): a resposta real do homolog é "pendente" **e** "precisa ser UPD" na mesma
mensagem; como `Casa(PadroesExigeUpd)` é testada **antes** de qualquer regra de sucesso ou pendência, ela nunca é
classificada como importada sem o UPD ter sido aplicado. O IMP nunca vira `Associada` (o IMP não associa). A
sequência `I60Enviada → Associada` do passo 6 usa dois `Atualizar` (6.2).

5. **Falha de comunicação nunca é terminal** (achado 4). Exceção de comunicação = `HttpRequestException` (inclusive
   timeout e `EnsureSuccessStatusCode`), `TaskCanceledException` por timeout (não por `ct`), `JsonException` na
   resposta e `InvalidOperationException` do `GetToken` (hoje o homolog responde 401 no token). A nota
   **permanece `Separada`**, `FalhasComunicacao++` e
   `ProximaTentativaI60Em = agora + min(2^(FalhasComunicacao−1) × IntervaloMinutos, BackoffMaxMinutos)` (5, 10, 20, 40,
   60, 60… minutos). `TentativasI60` (envios) é separado de `FalhasComunicacao`. Ao passar de
   `MaxFalhasComunicacaoAlerta`, a saúde do relatório mostra o alerta, mas a nota continua sendo reenviada.
   Exceção: `HttpRequestException` com `StatusCode` 400 a 499, exceto 401, 403, 408 e 429, é erro **definitivo** só
   depois de `Max4xxConsecutivos` (padrão 3) ocorrências seguidas na mesma nota, com o backoff entre elas; então
   `Rejeitada`/`ErroDefinitivoHttp`. Isso evita rejeitar em massa por configuração errada passageira.
   **[OPCIONAL, cortável: `Max4xxConsecutivos` e o disjuntor; ver seção 19.1]**
   **Disjuntor:** 3 falhas de comunicação seguidas, de notas diferentes, no mesmo ciclo interrompem o passo 6 até o
   ciclo seguinte (protege o WMS e o tempo do ciclo). Cancelamento (`ct`) propaga sem contar como falha.
   Falha no meio de um fluxo de duas chamadas (ex.: INE respondida, IMP com exceção) reinicia pela INE no próximo
   ciclo, coberta pela regra B (9.4).

### 9.4 At-least-once

Como a I60 é enviada antes de gravar o resultado, uma queda entre os dois reenvia a nota no ciclo seguinte. O WMS
responde "já existe, TpAcao precisa ser UPD" (pendente + ressalva), e o passo B do algoritmo resolve com um reenvio
UPD. Se `ReenviarComUpdSeJaExiste=false`, a nota termina `Rejeitada`/`JaExiste` (visível no relatório, com a mensagem
do WMS). Cada chamada ao WMS pode criar um registro novo na interface (observado em `respostas-homolog.txt`, ids
187161 e 187162), por isso o número de chamadas por nota é limitado (4) e o write-ahead de `TentativasI60` existe.

### 9.5 ENC e a pergunta aberta de detecção

O layout da I51 não traz o status do doccarga. A detecção padrão é por texto na resposta da I60 INE, tanto em
mensagem de recusa quanto em ressalva de resposta pendente (`PadroesDocCargaEncerrado`). As respostas reais do
homolog são "importado e pendente" com ressalvas e não "recusa", então o casamento cobre as ressalvas. Se no homolog o
texto for outro, basta ajustar a lista (`I60__PadroesDocCargaEncerrado`), sem release. Ver pergunta Q1.

## 10. Ondas TSP

Mesmo fluxo, sem ramificação de código além do registro: o registro grava `Tipo=TSP`, `Onda` e o transporte da
nota. A consulta e o casamento não dependem da onda (a chave é CNPJ + número). O relatório mostra Tipo e Onda
nas tabelas e agrupa por pacote (que contém as ondas). Dentro de ZIP, o XML copiado vem da pasta temporária
`Estado\tmp\...`, que existe no momento do registro (a limpeza do temporário acontece só no `finally` de
`ProcessarAsync`).

## 11. Relatório diário às 12:00

### 11.1 Conteúdo

Identidade visual atual ("Relatório de Integração", `IdentidadeVisual.Titulo`, cores e logo Friozem),
subtítulo "Separação (I51) e associação (I60)". Seções, nesta ordem:

1. **Cabeçalho** com data, janela (`[slot − 24 h, agora]`) e saúde da consulta (última consulta OK, falhas
   consecutivas, arquivos em `falhos`, documentos sem nota correspondente na janela).
2. **Cartões**: aguardando separação, atrasadas, separadas aguardando I60, I60 associadas, I60 enviadas como IMP
   (ENC), I60 enviadas (pendente na interface, com idade), rejeitadas. Os de fluxo contam movimentos da janela; os de fila contam o
   estado atual.
3. **Aguardando separação** com idade (`1 d 3 h`), tipo, transporte/onda.
4. **Separadas** na janela e **I60 pendentes de envio**.
5. **I60 associadas** na janela.
6. **I60 com problema**: `I60Enviada` com `OperacaoI60=IMP`/`Motivo` diferente de `Nenhum` e `Rejeitada`, com
   categoria (`MotivoI60`), ressalvas e mensagem. Notas `I60Enviada` sem problema (pendente na interface) entram
   numa tabela própria "I60 enviadas aguardando confirmação" com a idade no estado (Q11).
7. **Ajustes de quantidade**: uma linha por item com diferença (nota, item, produto, original, separada,
   diferença).
8. **Atrasadas** com idade e número de UNICO tentados.
9. **Visão por pacote** (11.4).
10. **Notas sem associação: candidatas à devolução pela I40** (última alternativa): `I60Enviada` com
    `OperacaoI60=IMP` ou com `Motivo` de problema, e `Rejeitada`, quando `ListarCandidatasI40=true`. Texto informa se `Devolucao:Habilitada` está desligada.
    Sem link: a tela é isolada e sem login.

Cada seção tem teto `MaxLinhasPorSecao` com a linha "e mais N não exibidas".

### 11.2 Arquivos

Novos: `Relatorio\ModeloRelatorioI60.cs` (modelo único consumido por HTML e PDF), `MontadorRelatorioI60.cs`
(puro: recebe snapshot de notas + saúde + relógio), `IGeradorRelatorioI60.cs`, `GeradorRelatorioI60.cs`
(mesmo padrão de `GeradorResumoSemanal`, incluindo o `GlobalFontSettings` idempotente),
`RenderizadorPdfRelatorioI60.cs` (A4 paisagem, blocos paginados em duas passadas como
`RenderizadorPdfResumo`; a lógica de paginação é replicada em vez de refatorar o renderizador existente, para
não tocar o resumo semanal), `Email\CorpoEmailRelatorioI60.cs` (tabelas com CSS inline compatível com Outlook,
logo por CID, tudo HTML-encoded, como `CorpoEmailResumoSemanal`). O HTML gravado em disco usa a logo como data
URI (parâmetro `ModoLogo`), porque o `cid:` só funciona dentro do e-mail.

### 11.3 Agendamento e envio (`Monitoramento\`)

- `AgendadorRelatorioDiario.DeveEnviar(agora, ultimoSlot, hora)`: função pura. `slot` = hoje às `hora`;
  `Deve = agora >= slot && (ultimoSlot is null || ultimoSlot < slot)`. Recupera slot perdido no mesmo dia e
  nunca envia mais de um por dia (parada de vários dias gera um único relatório, não uma enxurrada).
- `MarcaUltimoRelatorioI60`: igual à `MarcaUltimoResumo`, arquivo `ultimo-relatorio-i60.txt` na pasta de
  telemetria (escrita atômica, nunca lança).
- `FonteDestinatariosRelatorioI60`: lê `RelatorioI60:CaminhoDestinatarios` com `LeitorListaEmails`; vazio cai
  na lista do resumo semanal (`FonteDestinatariosResumo.Ler()`, que por sua vez cai em `ti@friozem.com.br`).
- `EnviadorRelatorioDiarioI60`: espelha `EnviadorResumoSemanal`. Sempre grava HTML + PDF em
  `<Relatorio:PastaSaida>\i60\relatorio-i60_<yyyyMMdd_HHmm>.{html,pdf}`; se o SMTP está configurado, envia o
  e-mail (PDF anexo `relatorio-i60.pdf`, assunto `"<prefixo> Relatório de Integração - I51/I60 - dd/MM/yyyy"`).
  Falha de e-mail só loga; nunca lança.
- `ServicoRelatorioDiarioI60.VerificarEEnviarAsync(ct)`: chamado no loop do `Worker` existente, logo depois de
  `ServicoResumoSemanal.VerificarEEnviarAsync` (mesma cadência de 5 s, mesmo isolamento por try/catch). A marca
  só é gravada depois de HTML e PDF gravados em disco. Sem atividade (nada em acompanhamento e nenhum movimento
  na janela) e `EnviarQuandoVazio=false`: marca o slot sem gerar.

### 11.4 Decisão: visão por pacote

Avaliado: (a) relatório separado por pacote; (b) seção no relatório diário; (c) acrescentar a informação ao
relatório do pacote. Escolhido **(b)**. Motivos: o relatório do pacote é gerado e enviado minutos depois da I50,
muito antes da separação, e é imutável (sem como atualizá-lo); um relatório por pacote multiplicaria e-mails
(vários pacotes por dia); o estado já guarda `IdentificadorPacote`, `Tipo` e `Onda`, então agrupar custa
só uma consulta em memória. A seção lista, por pacote ainda com pendência ou movimentado na janela: id do
pacote, tipo, notas, e a contagem por situação (aguardando, atrasadas, separadas, enviadas, associadas, IMP/ENC,
rejeitadas), com as ondas TSP entre parênteses. Pacotes totalmente encerrados e sem movimento na janela não
aparecem. Reavaliar se o usuário pedir um relatório por pacote enviado ao concluir (Q9).

## 12. Tela de devolução I40 (Plano B)

Continua como última alternativa, pronta e funcionando, atrás de `Devolucao:Habilitada=false`. Nenhum arquivo
de `Devolucao\*`, `TelaDevolucao\*`, `ConstrutorI40` ou `EnvioManualI40` é alterado funcionalmente. Pontos de
contato, todos cobertos por testes de regressão (seção 16.5):

- `ClienteWmsHttp`: o refatoramento para `PostCoreAsync` preserva o `IDENTIFICADOR=WEBAPI-I40-Json` e a query.
- `ProcessadorLote`: o XML continua indo para `Processados\<pacote>\` com o mesmo nome (a busca por chave e por
  número + série depende disso); a cópia do I51 não substitui o move.
- DI: `IClienteWms` passa a ser a instância única compartilhada (D4); `ServicoDevolucao` a consome sem mudança.
- Relatório diário lista candidatas, mas a I40 continua fora do fluxo automático e sem link.

Regras preservadas e já testadas: `tipoOperacaoNota=IMP`, `InverterPartes=false`, busca por chave **ou** número
+ série, sem placa e sem motorista. O teste real no homolog continua pelo `docs\ROTEIRO-TESTE-I40.md` (seção 9),
repetido junto do roteiro novo.

## 13. Worker e Program

- `ConsultaI51Worker : BackgroundService` (novo, `Worker\ConsultaI51Worker.cs`). `Habilitada=false`: loga uma
  linha e termina sem criar timer (padrão de `ServidorTelaDevolucao`). Habilitada: espera
  `AtrasoInicialSegundos` (mínimo 5); **antes do 1º ciclo chama `OpcoesI51.Validar()` e `OpcoesI60.Validar()`** (achado 6)
  e, havendo problema, loga `Error` e encerra o serviço sem consultar (o `Worker` faz o `StopApplication()`; o serviço
  não depende dele para não consultar com configuração inválida); loop `CicloAsync` e `Task.Delay(IntervaloMinutos)`. Exceção de ciclo: `LogError`
  e segue; cancelamento sai limpo. Roda isolado do `Worker` de pacote: um pacote demorado não atrasa a consulta
  e vice-versa.
- `Worker.cs`: injeta `IOptions<OpcoesI51>` e `ServicoRelatorioDiarioI60`; valida `OpcoesI51.Validar()`,
  `OpcoesI60.Validar()` e `OpcoesRelatorioI60.Validar()` junto de `Pastas`/`Zip`, **só quando `I51:Habilitada=true`**
  (achado 4); chama o serviço do relatório após o resumo semanal.
- `Program.cs`: `Bind` de `OpcoesI51` e `OpcoesRelatorioI60`; registro de `ClienteWmsHttp` como instância única
  (D4); singletons de `IRepositorioSeparacao`, `ArmazenamentoBrutoI51`, `ClienteI51`, `CalculadorAjuste`,
  `ProcessadorI60Separacao`, `ServicoSeparacaoI51`, `IRegistradorSeparacao`, `MarcaUltimoRelatorioI60`,
  `FonteDestinatariosRelatorioI60`, `IGeradorRelatorioI60`, `EnviadorRelatorioDiarioI60`,
  `ServicoRelatorioDiarioI60`; `AddHostedService<ConsultaI51Worker>()`.
- `appsettings.json`: seções `I51` e `RelatorioI60` novas, chaves novas na `I60`, todas com `_comentario` no
  estilo atual e `Habilitada=false` para I51 e I60.
- CLIs de apoio ao homolog (padrão `EnvioManualI60`, montagem manual de objetos, sem tocar o host), reduzidas ao
  que o roteiro exige (achado 10): `ConsultaManualI51` (`--consultar-i51 --tipo UNICO --cnpj <cnpj> --numero <n>`;
  **só UNICO**, que é repetível e não consome; `--tipo TODOS` é recusado com saída 2 e mensagem explicando que o
  TODOS consome documentos e só roda pelo serviço; grava o bruto em `envio-manual-i51\` e imprime só contagens e
  números de documento) e `RelatorioManualI60` (`--relatorio-i60 [--enviar]`, gera HTML/PDF do estado atual em
  `Relatorio:PastaSaida\i60`, necessária ao roteiro para validar o relatório sem esperar as 12:00) e
  `ReenfileirarI60` (`--reenfileirar-i60 --chave <chave>|--motivo <MotivoI60>`, 6.2; achado 3). Códigos de
  saída: 0 ok, 1 erro de envio/geração, 2 parâmetro ou configuração faltando.

## 14. Tratamento de erros

| Operação | Falha | Recuperável? | O que o chamador recebe | Log |
|---|---|---|---|---|
| `PostBrutoAsync` (I51) | rede, timeout, 5xx esgotado, 4xx | Sim, no ciclo seguinte | Exceção no serviço, capturada: ciclo segue | `Warning` (tipo da exceção e status, sem corpo); `Error` se `FalhasConsecutivas ≥ 3` |
| Corpo I51 `sucesso=N`, sem documentos | nada a retornar | Não é erro | `NadaARetornar` | `Debug`; `Information` se a mensagem mudar |
| Corpo I51 inválido | JSON ilegível | Sim (reprocesso) | Arquivo permanece em `pendentes`; após N → `falhos` | `Warning` por tentativa, `Error` ao ir a `falhos` |
| Gravar bruto | disco | Parcial | Segue processando em memória; `TODOS` já consumido pode se perder, `UNICO` recupera | `Error` |
| Documento sem nota | nota ainda não registrada/desconhecida | Sim | Gravado em `orfaos\` e casado nos ciclos seguintes e no `Registrar`; já separada = descartado | `Information` (número) |
| `sucesso=N` com `PadroesSemRetorno` preenchida e mensagem fora da lista | EDI/CNPJ mal configurado | Sim | `FalhasConsecutivas++` | `Warning` (mensagem truncada) |
| Resposta da I60 desconhecida (`Outro`) | texto novo do WMS | Sim, até `MaxTentativasOutro`; depois manual | Nota permanece `Separada` com backoff; ao esgotar `Rejeitada`; `--reenfileirar-i60` | `Warning` por tentativa; `Error` ao rejeitar |
| Fallback de XML | `ArgumentException` (chave inválida/sintética) ou arquivo ausente | Não | `Rejeitada`/`XmlIndisponivel`, sem consumir `TentativasI60` | `Error` |
| `Atualizar` estado | disco | Sim | `false`; transição não ocorre; bruto fica pendente | `Error` |
| Transição inválida | bug | Não | `InvalidOperationException` capturada no serviço | `Error` |
| XML indisponível | cópia e fallback ausentes | Não | Nota `Rejeitada`/`XmlIndisponivel` | `Error` |
| I60 recusa de negócio | ver 9.3 | Conforme a tabela | Estado terminal com motivo | `Information`/`Warning` (número, motivo, sem corpo) |
| I60 falha de comunicação | rede, timeout, 5xx, 401/403/408/429, token | Sim, sempre (nunca terminal) | Nota permanece `Separada`, com backoff até `BackoffMaxMinutos`; disjuntor de 3 falhas por ciclo | `Warning` (tipo e status, sem corpo); `Error` ao passar de `MaxFalhasComunicacaoAlerta` |
| I60 HTTP 4xx definitivo | 400-499 exceto 401/403/408/429 | Só após `Max4xxConsecutivos` | `Rejeitada`/`ErroDefinitivoHttp` | `Error` |
| I60 resposta pendente + UPD (formato real) | "importado e pendente" com `Ressalvas : {... UPD!}` | Sim | Reenvio UPD (no máximo 1 por operação), 9.3 | `Information` |
| Registro na I50 | disco/cópia | Parcial | Nota sai sem acompanhamento (cópia falha: segue com fallback) | `Error` |
| `I51:Habilitada=false` | n/a | n/a | `RegistradorSeparacao` e serviço retornam sem tocar disco | nenhum |
| Relatório (gerar/gravar) | exceção | Sim | Slot **não** marcado; tenta no próximo ciclo | `Error` |
| E-mail do relatório | SMTP | Sim | Relatório em disco permanece; slot marcado | `Warning`/`Error` |
| Config inválida com I51 ligada | validação | Não | `StopApplication()` na partida | `Error` por problema |

Todos os componentes novos de I/O seguem a regra do projeto: **nunca lançam por falha de disco**.

## 15. Validação de entradas

- **Configuração**: seção 4 (faixas e formatos); `Validar()` único por seção; valores de enum desconhecidos
  são problema de configuração.
- **Resposta da I51** (entrada externa não confiável): tamanho máximo do corpo aceito 20 MB (acima, tratado
  como inválido sem desserializar); `documentos` ausente = vazio; `Numero` obrigatório (documento sem número é
  ignorado e contado); `itens` ausente = vazio; quantidade não numérica invalida **o documento**, não a
  resposta; `numeroSequencialItem` não inteiro cai no casamento por `codProduto`.
- **Nome de arquivo**: toda chave usada como nome de arquivo passa por sanitização `[0-9A-Za-z_-]`; nunca se
  concatena texto vindo da I51 em caminho (os nomes dos brutos usam carimbo e tipo próprios).
- **XML da NF-e**: lido pelo `LeitorNotaFiscalXml` existente (mesmas regras da I50).
- **`ReenfileirarI60`**: exige `--chave` ou `--motivo` (enum `MotivoI60` válido, exceto `Nenhum`); só afeta notas `Rejeitada`.
- **CLIs**: `--tipo` aceita só `UNICO` (`TODOS` é recusado, saída 2); `--cnpj` precisa de 14 dígitos; `--numero` só dígitos, até 9;
  credenciais ausentes → saída 2.

## 16. Estratégia de testes

Todos sem rede e sem WMS real; xUnit, `FakeClock` e `AmbientePacote`/`AmbienteDevolucao` existentes.

### 16.1 WMS falso (`tests\...\Infra\FakeWmsI51.cs`, novo)

Implementa `IClienteWms` e `IClienteWmsBruto`. Simula o WMS: `Separar(cnpj, numero, itens)` coloca um documento
disponível; `TODOS` devolve os disponíveis **ainda não puxados** e os marca como puxados; `UNICO` devolve o
documento pedido mesmo já puxado; sem nada devolve `sucesso=N`. Falhas programáveis (`ThrowNoProximo`), corpos
brutos programáveis (JSON inválido), respostas da I60 por predicado (ex.: `INE → "doccarga encerrado"`,
`IMP + I → "TpAcao precisa ser UPD"`, `IMP + UPD → sucesso`) e registro de cada envio (interface, `Tpsit`,
`tipoOperacaoNota`, `tpacao`, quantidades dos itens).

### 16.2 Casos obrigatórios (arquivos de teste novos)

| Caso | Verifica |
|---|---|
| TODOS consumível | 2 consultas seguidas: a 2ª vem vazia; nada duplicado; estado correto. |
| Bruto antes de processar | `ProcessarBruto` falha de propósito: o bruto está em `pendentes`; o ciclo seguinte processa **sem chamar o WMS**. |
| UNICO repetível | Mesmo UNICO duas vezes devolve o mesmo documento; recuperação de atrasada funciona. |
| `sucesso=N` | Não é erro, não grava bruto, não muda estado, `FalhasConsecutivas` não sobe. |
| Ajuste de quantidade | I60 sai com `qtdeProduto`/`qtdeProdutoConfirmada`/`qtdeEmbalagem` = separadas; `qtdeTotalNota` e `qtdeVolume` recalculados; estado guarda original × separada; `Omitir` e `Zerar`; soma 0 não envia. |
| Volume | `QtdeVolumeOrigem=SomaCaixas` (padrão) = comportamento atual; `VolumesI51` usa `volumes.Count`; contagem 0 cai em `SomaCaixas`; resposta "Total de volume(s)... não confere com a qtdvolume" → `Rejeitada`/`VolumeDiferente`. |
| ENC → IMP | INE com "encerrado" (como recusa **e** como ressalva de resposta pendente): reenvio com `Tpsit` e `tipoOperacaoNota` = `IMP`, `tpacao=UPD` (padrão); estado `I60Enviada` com `OperacaoI60=IMP`, `Motivo=DocCargaEncerrado` e erro registrado; nunca `Associada`; `TpAcaoReenvioImp=I` + resposta "precisa ser UPD" gera 1 reenvio UPD; IMP que ainda pede UPD → `Rejeitada`/`JaExiste`; no máximo 4 chamadas; sem reenvio infinito. |
| Resposta real "pendente + UPD" | Mesmo texto do `respostas-homolog.txt` (pendente e "TpAcao precisa ser UPD" na mesma mensagem) para INE e para IMP: nunca vira `I60Enviada` sem o reenvio UPD; depois do UPD pendente sem ressalva → `I60Enviada`. |
| Pendente sem ressalva / com ressalva | `I60Enviada`/`PendenteNaInterface`; com ressalva não bloqueante `PendenteComRessalva`; com ressalva bloqueante `Rejeitada` (`QuantidadeDiferente` etc.). |
| Sucesso real | `EhSucesso` (INE) → `I60Enviada` e `Associada`; `PadroesAssociadaSucesso` configurável. |
| Normalização de texto | "encerrado" × "ENCERRADO" × "Encerrádo", "não confere" × "nao confere"; padrão vazio não casa tudo. |
| Falha de comunicação | 5 falhas seguidas **não** rejeitam; backoff 5/10/20/40/60 min com `FakeClock`; `TentativasI60` ≠ `FalhasComunicacao`; 4xx só rejeita no 3º; 401/5xx nunca; disjuntor interrompe o ciclo; WMS volta e a nota é enviada. |
| Registro na I50 | Registra com `Integrada`, com `Estado=Pendente` (I50 "pendente na interface", `Integrada=false`) e com reprocesso "já existe" (queda entre I50 e registro); idempotente; rejeitada não registra. |
| Número do documento | `"000000000"` ignorado e contado como inválido; `"000012345"` casa com nota `12345`. |
| UNICO `CampoUnico` | Cliente com `FakeHttpMessageHandler`: `Numero` coloca 9 dígitos em `numero` e `nroDocumento` neutro; `NroDocumento` coloca o número em `nroDocumento` e `numero` neutro; valor inválido de `CampoUnico` reprovado em `Validar` (achado 1). |
| Órfãos e desempate | Documento chega antes do `Registrar`: fica em `orfaos\`, a nota registrada depois vira `Separada` (no `Registrar` e no ciclo seguinte); duas candidatas com mesmo número: filtro por CNPJ destinatário, depois a mais antiga + `Observacao`; órfão de nota já separada é descartado; órfão expira pela retenção (achado 2). |
| `Outro` e reenfileirar | Resposta desconhecida: nota fica `Separada` com backoff até `MaxTentativasOutro`, depois `Rejeitada`; mudar `Padroes*` e `Reenfileirar` zera contadores e a nota é reenviada e classificada corretamente; `Atualizar` genérico recusa `Rejeitada → Separada` (achado 3). |
| Validação de configuração | `OpcoesI60.Validar`/`OpcoesRelatorioI60.Validar`: `TpAcaoReenvioImp=XYZ`, `QtdeVolumeOrigem=Foo`, `TipoOperacaoEncerrado` vazio, `BackoffMaxMinutos=0`, `MaxTentativasOutro=0`, `MaxLinhasPorSecao=0`, lista de encerrado vazia com ENC ligado; `Worker` só valida com I51 ligada (achado 4). |
| Fallback de XML | Cópia ausente + chave sintética → `XmlIndisponivel`, sem exceção e `TentativasI60=0`; chave de 44 dígitos inválida → idem; chave válida acha em `Processados` (achado 5). |
| Partida do consumidor | `ConsultaI51Worker` com opções inválidas não chama o WMS; `AtrasoInicialSegundos<5` reprovado (achado 6). |
| `PadroesSemRetorno` | Lista vazia: `N` sem documentos = nada a retornar; lista preenchida e `N` fora dela = `FalhasConsecutivas++` e `Warning`; `N` dentro da lista = nada a retornar (achado 7). |
| `INE` normal | Operação `INE`, **nunca** `IMB`; padrões configuráveis (texto diferente muda a detecção). |
| Onda TSP | Pacote `PuxadaTSP` (ZIP) registra `Tipo=TSP` e `Onda`; segue até a I60. |
| Reinício no meio | Nova instância de repositório e serviço sobre a mesma pasta após cada etapa: aguardando, bruto pendente, separada, I60 gravada. |
| Atrasada | Com `FakeClock` +25 h vira `Atrasada`; UNICO recupera; relatório lista. |
| I51 desligada | `Orquestrador` com `I60.Habilitada=true` envia a I60 junto da I50 como hoje; `ProcessadorLote` com `RegistradorSeparacao` **real** (como na DI de produção) e `Habilitada=false` não cria `Estado\i51` nem lê o disco; repositório preguiçoso (construir não cria pasta); nenhum bruto; `ConsultaI51Worker` termina sem timer; relatório diário não agendado. |
| I51 ligada | `Orquestrador` não envia I60; histórico traz a linha "I60 adiada". |
| I51 ligada, I60 desligada | Nota chega a `Separada` e fica; nenhuma I60 enviada. |
| Casamento | Zeros à esquerda; filtro por CNPJ; ambiguidade; documento desconhecido ignorado. |
| Parser | "24.0000", datas, campos ausentes/desconhecidos, JSON inválido, documento com quantidade inválida isolado. |
| Cliente I51 | `FakeHttpMessageHandler`: query com `IDENTIFICADOR` configurável, corpo da CONSULTA (9 dígitos, máscara de CNPJ), retry do token reaproveitado. |
| Repositório | `.tmp` não sobra; arquivo corrompido preservado; transição inválida rejeitada; falha de disco não lança e mantém o índice; `Registrar` idempotente; limpeza só de terminais vencidos. |
| Agendador diário | Antes/depois das 12:00; idempotência; slot perdido no mesmo dia; dia seguinte reabre. |
| Relatório | Modelo (contagens, idade, ajustes, por pacote, candidatas I40); HTML sem `<script>`/`<style>` externo e com texto encoded; PDF gerado e legível com `PdfTextoHelper`; teto de linhas. |
| Segredos | Logger coletor: nenhum log contém token, senha, trecho do XML ou do bruto. |
| Config | `OpcoesI51.Validar` (faixas); `appsettings.json` real: `I51:Habilitada`, `I60:Habilitada` e `Devolucao:Habilitada` = `false`. |

### 16.3 Existentes que devem continuar verdes sem edição

`ClienteWmsHttpTests`, `OrquestradorTests`, `OrquestradorCapturaTests`, `ConstrutorI60Tests`,
`OpcoesI60Tests`, `ProcessadorLoteTests`, `ProcessadorLoteTspTests`, `PacoteEndToEndTests`,
`PacoteTspEndToEndTests`, `MedicaoTempoPacoteTests`, `Stress\*`. Linha de base: 655 aprovados, 9 ignorados.

### 16.4 Exemplos visuais

Helper de teste `ExemplosRelatorioI60` (padrão `ExemplosResumoSemanal`) gera HTML/PDF com dados fictícios para a
conferência com screenshot do Edge headless (sem XML, sem ZIP, sem dado real).

### 16.5 Regressão da tela I40 (obrigatória)

Rodar a suíte inteira da I40 sem edição (`ConstrutorI40Tests`, `ServicoDevolucaoTests`,
`BuscaNumeroSerieDevolucaoTests`, `LocalizadorXmlDevolucaoTests`, `ValidadorChavesDevolucaoTests`,
`OpcoesDevolucaoTests`, `TelaDevolucaoEndpointTests`) e acrescentar `RegressaoI40ComI51Tests`: (a) com
`I51:Habilitada=true`, um pacote processado continua deixando o XML em `Processados\<pacote>\` e a busca por
chave e por número + série o acha; (b) a I40 enviada pelo `ServicoDevolucao` com `ClienteWmsHttp` real sobre
`FakeHttpMessageHandler` usa `IDENTIFICADOR=WEBAPI-I40-Json`, `IMP`, sem placa e sem motorista (garante o
refatoramento de `PostCoreAsync`); (c) a tela sobe e responde com `I51` ligada e `Devolucao:Habilitada=true`;
(d) com `Devolucao:Habilitada=false` o `ServidorTelaDevolucao` não abre porta.

### 16.6 Verificação final

`dotnet build KitWmsIntegrador.sln -c Release` com 0 avisos; `dotnet test` verde. Se o Worker estiver rodando
e travar DLL, testar em cópia temporária.

## 17. Invariantes e quem as garante

| Invariante | Camada responsável | Motivo |
|---|---|---|
| Uma nota tem no máximo um estado por chave | Repositório (`Registrar` idempotente) | Único ponto de escrita. |
| Só transições da tabela 6.2 | Repositório (`Atualizar`) | Serviços não conseguem burlar. |
| Memória = disco após `Atualizar` | Repositório | Troca do índice só depois do `Move`. |
| Resposta bruta do TODOS em disco antes de processar | `ServicoSeparacaoI51` (ordem das chamadas) + teste | O TODOS não repete. |
| I60 só sai para nota `Separada` | `ProcessadorI60Separacao` | Fonte única do disparo. |
| I60 nunca sai junto da I50 com I51 ligada | `Orquestrador` | Ponto atual do envio. |
| Operação normal é `INE`; `IMB` nunca é gerado | `OpcoesI60` + `ConstrutorI60` + teste | Decisão do usuário. |
| `IMP` só como reenvio ENC | `ProcessadorI60Separacao` | Regra do usuário. |
| `Associada` só com `INE` e sucesso; `IMP` nunca associa | `ProcessadorI60Separacao` + repositório (tabela 6.2) | O IMP só importa a nota. |
| Falha de comunicação nunca torna a nota terminal (salvo 4xx definitivo repetido) | `ProcessadorI60Separacao` | Indisponibilidade do WMS não pode rejeitar notas que ele nunca analisou. |
| Chave `I51:Habilitada=false` = nenhuma escrita em disco do I51 | `RegistradorSeparacao`, serviço, worker e repositório preguiçoso | Requisito 5 do brief. |
| No máximo 4 chamadas I60 por nota por ciclo | `ProcessadorI60Separacao` | Cada chamada pode criar registro na interface do WMS. |
| Nenhum segredo/corpo em log | Convenção + teste do logger coletor | Regra do projeto. |
| Arquivos da entrada seguem para Processados/Erro como hoje | `ProcessadorLote`/`OrganizadorArquivos` (intocados) | Tela I40 e retenção dependem disso. |

## 18. Log, telemetria e segurança

Logs com `ILogger` e parâmetros nomeados: número da nota, série, transporte, situação, contagens, motivo.
**Proibido** logar token, senha, cabeçalho `Authorization`, corpo do bruto, XML ou JSON da I60. Mensagens do WMS
entram truncadas (200 caracteres) e só no `Estado` (500). A telemetria JSONL (`RegistradorTelemetria`) não muda;
a saúde da consulta vai em `consulta.json` e no relatório. Credenciais continuam só em `WMS__Usuario`/`WMS__Senha`.
O repositório é público: `.gitignore` ganha `destinatarios-i60.txt` e `ultimo-relatorio-i60.txt`; nenhum `*.xml`
ou `*.zip` é versionado e os exemplos de teste são gerados em runtime com dados fictícios.

## 19. Arquivos

**Novos (Core)**: `ClienteWms\IClienteWmsBruto.cs`; `Configuracao\OpcoesI51.cs`, `OpcoesRelatorioI60.cs`;
`Separacao\` (`NotaSeparacao.cs`, `SituacaoNota.cs`, `MotivoI60.cs`, `IRepositorioSeparacao.cs`,
`RepositorioSeparacaoArquivo.cs`, `ArmazenamentoBrutoI51.cs`, `ClienteI51.cs`, `ParserI51.cs`,
`Dtos\RequisicaoI51.cs`, `Dtos\RespostaI51.cs`, `CasadorDocumentoNota.cs`, `CalculadorAjuste.cs`,
`IRegistradorSeparacao.cs`, `RegistradorSeparacao.cs`, `ClassificadorMotivoI60.cs`,
`ProcessadorI60Separacao.cs`, `ServicoSeparacaoI51.cs`, `TextoNormalizado.cs`, `ParserRessalvas.cs`,
`LeituraI60.cs`, `RepositorioOrfaosI51.cs` (órfãos, achado 2)); `Construtores\ParametrosI60.cs`;
`Relatorio\` (`ModeloRelatorioI60.cs`, `MontadorRelatorioI60.cs`, `IGeradorRelatorioI60.cs`,
`GeradorRelatorioI60.cs`, `RenderizadorPdfRelatorioI60.cs`); `Email\CorpoEmailRelatorioI60.cs`;
`Monitoramento\` (`AgendadorRelatorioDiario.cs`, `MarcaUltimoRelatorioI60.cs`,
`FonteDestinatariosRelatorioI60.cs`, `EnviadorRelatorioDiarioI60.cs`, `ServicoRelatorioDiarioI60.cs`).

**Alterados**: `ClienteWms\ClienteWmsHttp.cs`; `Configuracao\OpcoesI60.cs`; `Construtores\ConstrutorI60.cs` e
`IConstrutorI60.cs` (overload novo); `Orquestrador\Orquestrador.cs`; `Pacote\ProcessadorLote.cs`;
`Worker\Worker.cs`, `Program.cs`, `appsettings.json`; `.gitignore`.

**Novos (Worker)**: `ConsultaI51Worker.cs`, `ConsultaManualI51.cs`, `RelatorioManualI60.cs`, `ReenfileirarI60.cs`.

### 19.1 Itens opcionais, cortáveis se as horas estourarem (achado 8)

Para o plano de implementação marcar como "corte permitido" (nenhum é requisito do brief):
- `I60:Max4xxConsecutivos` e a regra de 4xx definitivo (sem ele, 4xx é falha de comunicação comum, nunca terminal).
- Disjuntor por ciclo (3 falhas seguidas interrompem o passo da I60).
- `I51:IntervaloUnicoMinutos` / `I51:MaxUnicoPorCiclo` (cortar = UNICO de recuperação fixo em 1 por nota por hora e 10 por ciclo, sem chaves).
- `I51:ValoresNeutros` (cortar = constantes `0`/`0`/vazio/`0`; só mantido se Q4 voltar do homolog diferente).
- `ConsultaManualI51` (o roteiro usa o UNICO manual; cortar exige rodar o UNICO só via ciclo).
Não cortáveis: `CampoUnico`, `PadroesSemRetorno`, órfãos, `MaxTentativasOutro` + `ReenfileirarI60`, `Validar()` das três
seções, `RelatorioManualI60` (valida o relatório sem esperar as 12:00).

**Testes e docs**: `tests\...\Infra\FakeWmsI51.cs` e os arquivos de teste da seção 16;
`docs\ROTEIRO-TESTE-I51-I60.md` (novo: pré-requisitos, UNICO manual, ciclo completo, ENC, quantidade diferente,
TSP, reinício, atrasada, **UNICO nas duas variantes `Numero`/`NroDocumento` (Q13)**, mensagem real do `sucesso=N` (Q14), reenfileirar, relatório forçado com `--relatorio-i60`, **aviso de que a primeira ativação do TODOS consome o backlog** da proprietária (conferir `brutos\processados` e, se aparecerem documentos de outros tipos, anotar `Tipo/SubTipo`), registro das respostas reais da I60 (sucesso, pendente, ENC, UPD, volume: perguntas Q1, Q2, Q11, Q12), regressão da tela I40 pelo
`ROTEIRO-TESTE-I40.md` seção 9); `docs\ESCOPO-I51-I60.md` (seção "implementado"); `docs\ARQUITETURA.md`
(seção 2.3 e diagrama); `docs\OPERACAO.md`; deck e manuais por último, depois da aprovação do review, com a
I40 só como "última alternativa" em seção própria e fácil de remover.

## 20. Perguntas abertas (com padrão configurável)

| # | Pergunta | Padrão adotado | Como mudar |
|---|---|---|---|
| Q1 | Como o ENC aparece na resposta do WMS? | Texto "encerrado" em mensagem de recusa ou em ressalva de resposta pendente da I60 INE | `I60__PadroesDocCargaEncerrado` |
| Q2 | O reenvio IMP exige `TpAcao=UPD`? | `UPD` (a INE recusada por ENC já importou a nota); se usar `I` e o WMS pedir UPD, 1 reenvio automático | `I60__TpAcaoReenvioImp`, `I60__ReenviarComUpdSeJaExiste` |
| Q3 | Qual CNPJ vai em `CONSULTA.CNPJ` (proprietário, armazém ou destinatário)? | Proprietário (`Operacional:CnpjDepositante`) | `I51__CnpjConsulta` |
| Q4 | Valores dos campos não usados da CONSULTA | `numero=0`, `serie=0`, `chaveNFe` vazio, `nroDocumento=0` | `I51__ValoresNeutros__*` |
| Q5 | (removida na revisão) quantidade separada: sempre `qtdeProdutoSep` nos três campos, como a I60 atual (1:1) | `qtdeProdutoSep` | Reabrir só se o homolog mostrar embalagem diferente de produto |
| Q6 | Item da nota que o WMS não devolveu: zerar ou omitir? | Zerar | `I51__ItemSemRetorno` |
| Q7 | `Numero` do documento da I51 é mesmo o `numeroPedido` (nNF) da I50? | Sim (premissa A1) | Confirmar no homolog |
| Q8 | Destinatários do relatório diário | Mesma lista do resumo semanal | `RelatorioI60__CaminhoDestinatarios` |
| Q9 | Quer relatório por pacote enviado ao concluir a separação do pacote? | Não, só a seção no diário (11.4) | Nova decisão |
| Q10 | Notas já integradas antes de ligar a I51 devem entrar no acompanhamento? | Não (só notas processadas depois da chave ligada; reprocesso "já existe" de nota nova entra, 7.2) | Nova decisão |
| Q11 | Como o WMS sinaliza a I60 associada com sucesso? Todas as respostas vistas foram `sucesso="N"` + "pendente na interface" | `ClassificadorRetorno.EhSucesso` ("Registrado com sucesso" ou `Sucesso=S`); o resto fica `I60Enviada`, visível no relatório | `I60__PadroesAssociadaSucesso`; registrar a resposta real no `ROTEIRO-TESTE-I51-I60.md` |
| Q13 | O `UNICO` usa `numero` (9 dígitos) ou `nroDocumento` para o número da nota? O exemplo do manual traz `nroDocumento` preenchido; o usuário disse "só CNPJ e número". | `Numero` | `I51__CampoUnico=NroDocumento`; roteiro testa as duas variantes |
| Q14 | Qual mensagem o WMS devolve em `sucesso=N` quando realmente não há nada, e quando o EDI/CNPJ está errado? | Vazio: todo `N` sem documentos é "nada a retornar" | `I51__PadroesSemRetorno` |
| Q12 | A conferência de volumes da I60 usa volumes físicos (que só existem após a separação)? | Soma das caixas (`SomaCaixas`, comportamento atual) | `I60__QtdeVolumeOrigem=VolumesI51` e `I60__PadroesVolumeDiferente` |

## 21. Premissas assumidas

- **A1**: o `Numero` do documento da I51 é o `numeroPedido` enviado na I50 (= `nNF`), casado sem zeros à esquerda e
  filtrado por CNPJ do proprietário.
- **A2**: o ajuste mexe só em quantidades (item, total e, conforme `QtdeVolumeOrigem`, volume). `ValorTotal`, pesos e valores unitários seguem
  o XML, porque a nota é o documento fiscal já emitido.
- **A3**: sem separação parcial (decisão do usuário): a presença do documento na I51 significa separação concluída.
- **A4**: o doccarga ENC é detectado pela resposta da I60, em mensagem ou ressalva (hipótese do escopo).
- **A5**: a resposta real do WMS à I60 é "importado e pendente na interface" com `sucesso="N"` (`respostas-homolog.txt`); por isso `I60Enviada` é o estado normal e `Associada` depende de sinal de sucesso confirmado (Q11).

## 22. Riscos

- Nota cujo registro falha (disco) fica sem acompanhamento e sem alerta além do log `Error`. Mitigação: relatório
  do pacote lista a I50 aceita; ajuste futuro possível (contador na saúde).
- O TODOS não repete: bruto perdido por falha de disco + falha de processamento só se recupera pelo UNICO (que
  exige a nota virar `Atrasada` ou o uso manual da CLI).
- `at-least-once` na I60 (9.4) depende da resposta "UPD" do WMS; confirmar no homolog (Q2).
- Sem sinal de sucesso confirmado (Q11), notas ficam em `I60Enviada` e o relatório as mostra com a idade; não há consulta posterior ao WMS para promovê-las a `Associada`.
- O primeiro TODOS consome tudo o que estiver disponível na proprietária, inclusive documentos que não são nossos; o WMS não devolve de novo. Mitigação: o TODOS só roda com nota aguardando, o bruto fica em `brutos\processados` pela retenção, e o roteiro avisa e manda conferir (achado 12). Um filtro por `Tipo/SubTipo` não evitaria o consumo (acontece no WMS), então não foi criado.
- Atrasadas nunca expiram automaticamente; ficam no relatório até alguém tratar. Descarte automático fica fora
  do escopo.
- Texto das mensagens do WMS pode variar (Q1); por isso as listas são configuráveis.
- Duplicação da paginação do PDF (dívida técnica aceita para não alterar o resumo semanal).

## 23. Esforço (somente horas)

Estimativa de IA para esta implementação, revista pelo desenho (o escopo previa 8,0 h de IA no total):

| Fase | Entrega | Horas IA (estim.) | Horas humanas |
|---|---|---|---|
| 1 | Cliente bruto, I51 (inclui `CampoUnico`, `PadroesSemRetorno`), estado, repositório, órfãos, consulta periódica, Worker | aprox. 4,0 | a informar pelo usuário |
| 2 | I60 por separação, ajuste, ENC → IMP, `MaxTentativasOutro`, `ReenfileirarI60`, `Validar()`, `Orquestrador`/`ProcessadorLote` | aprox. 3,5 | a informar pelo usuário |
| 3 | Relatório diário (HTML, PDF, e-mail, agendador, visão por pacote) | aprox. 3,0 | a informar pelo usuário |
| 4 | Testes com WMS falso e regressão da I40 | aprox. 1,5 | a informar pelo usuário |
| 5 | Roteiro, ESCOPO, ARQUITETURA, OPERACAO | aprox. 1,0 | a informar pelo usuário |
| 6 | Deck e manuais (depois da aprovação) | aprox. 2,0 | a informar pelo usuário |
| 7 | Teste no homolog (proprietária BRF pendente no fornecedor) | aprox. 0,5 | a informar pelo usuário |

A revisão acrescentou aprox. 1,0 h de IA no total (órfãos, `CampoUnico`, reenfileirar, validações). Cortar os
itens da seção 19.1 devolve aprox. 0,5 h de IA.

Referência do escopo para horas humanas: aprox. 3,0 h no total. Horas humanas só entram no registro quando o
usuário as informar; o registro mestre (`docs\CONTROLE-ESFORCO.md`) não é editado por esta tarefa.

## 24. O que não foi verificado

- Nenhum teste foi executado nesta etapa (só leitura de código e documentos).
- O layout real da I51 e o texto das recusas do WMS (ENC, quantidade, UPD) dependem do homolog, hoje bloqueado
  pela proprietária BRF no fornecedor.
- Não verifiquei contra o WMS real o formato das ressalvas (`Ressalvas : {...}`): o parser foi desenhado a partir do texto de `respostas-homolog.txt`.
- Não li `deploy\config-exemplo` nem os manuais: o plano de implementação deve conferir se há appsettings de
  exemplo a atualizar.
- Não confirmei se algum teste existente instancia `Worker` diretamente (a busca em `tests` por construtores de
  `Orquestrador`/`ProcessadorLote` mostrou apenas os que usam parâmetros posicionais, compatíveis com os
  parâmetros opcionais novos).

## 25. Fora de escopo

Tela ou endpoint para operar a I51; qualquer outro `TIPO` da I51 além de `TODOS`/`UNICO`; separação parcial;
leitura de volumes/detalhes (etiqueta, lote, validade) da I51; acompanhamento de notas integradas antes da chave
ser ligada; descarte automático de atrasadas; alteração do resumo semanal e da telemetria; mudança de
comportamento da tela I40; push e worktree.

## 26. Respostas à revisão do design (`design-review-i51-i60.md`)

| # | Sev. | Decisão | Onde |
|---|---|---|---|
| 1 | HIGH | **Aceito.** `TpAcaoReenvioImp` padrão `UPD`; algoritmo único (INE, UPD, ENC, IMP) com `Casa(PadroesExigeUpd)` antes de qualquer sucesso/pendência; IMP que ainda pede UPD → `Rejeitada`/`JaExiste`; testes com resposta "pendente + UPD" no formato real para INE e IMP. | 4.2, 9.3, 9.4, 16.2 |
| 2 | MEDIUM | **Aceito.** Cadeia do brief (`Separada → I60Enviada → Associada \| Rejeitada`); `ImportadaImp` removido (ENC/IMP = `I60Enviada` com `OperacaoI60=IMP`); `Associada` só com `INE` e sucesso; parser de ressalvas e `PadroesRessalvaBloqueante`; Q11; `I60Enviada` terminal para limpeza e idade no relatório. | 4.2, 6.1, 6.2, 9.3, 11.1, 20 |
| 3 | MEDIUM | **Aceito.** Lê só `volumes.Count`; `I60:QtdeVolumeOrigem = SomaCaixas \| VolumesI51` (padrão `SomaCaixas`); `VolumeDiferente` e `PadroesVolumeDiferente`; Q12 e roteiro. | 4.2, 5.3, 6.1, 9.1, 16.2, 20 |
| 4 | MEDIUM | **Aceito.** Comunicação nunca terminal; backoff exponencial até `BackoffMaxMinutos`; `FalhasComunicacao` separado de `TentativasI60`; alerta na saúde; 4xx definitivo só após `Max4xxConsecutivos`; disjuntor por ciclo. `MaxTentativasEnvio` removido. | 4.2, 6.1, 9.3, 14 |
| 5 | MEDIUM | **Aceito.** `RegistradorSeparacao` no-op com chave desligada; repositório preguiçoso; serviço/worker/relatório não tocam o disco; teste com registrador real. | 6.4, 7.2, 16.2, 17 |
| 6 | MEDIUM | **Aceito.** Registra também `Estado=Pendente` e reprocesso "já existe" (idempotente por `Chave`); Q10 ajustada. | 7.2, 16.2, 20 |
| 7 | NIT | **Aceito.** `TextoNormalizado` (FormD sem `NonSpacingMark`, minúsculas) em mensagem e padrão; teste. | 4.2, 16.2 |
| 8 | NIT | **Aceito.** `new LocalizadorXmlDevolucao(pastas.CaminhoProcessados)` dentro do processador. | 9.3 |
| 9 | NIT | **Aceito (opção removida).** Sem `QuantidadeSeparadaUsa`; os três campos usam `qtdeProdutoSep`; Q5 removida. | 4.1, 9.1, 9.2 |
| 10 | NIT | **Parcial.** Removidos `GravarRespostasVazias`, `QuantidadeSeparadaUsa` e o `TODOS` da CLI. Mantidos, com justificativa, `IntervaloUnicoMinutos`, `MaxUnicoPorCiclo` (limitam carga no WMS), `ValoresNeutros` (Q4) e `RelatorioManualI60` (o roteiro precisa validar o relatório sem esperar as 12:00). | 4.1, 13, 15 |
| 11 | NIT | **Aceito.** Número vazio após `TrimStart('0')` = documento inválido, ignorado e contado; compara com `nota.Numero.TrimStart('0')`. | 8.1, 16.2 |
| 12 | NIT | **Aceito.** Aviso e conferência no roteiro; filtro por `Tipo/SubTipo` **não** criado (o consumo acontece no WMS); registrado como risco. | 19, 22 |

Itens do backlog: nenhum. Nada foi ignorado sem justificativa.

### Respostas à 2ª revisão (`design-review-i51-i60.md`, veredito CHANGES_REQUESTED: 0 HIGH, 5 MEDIUM, 3 NIT)

| # | Sev. | Decisão | Onde |
|---|------|---------|------|
| 1 | MEDIUM | **Aceito.** `I51:CampoUnico` (`Numero`\|`NroDocumento`, padrão `Numero`), validado, Q13, teste das duas variantes e caso no roteiro. | 4.1, 5.2, 16.2, 20 |
| 2 | MEDIUM | **Aceito.** Desempate determinístico (CNPJ destinatário, depois a mais antiga + `Observacao`) e órfãos em `Estado\i51\orfaos` casados nos ciclos e no `Registrar`. | 6.3, 8.1, 14, 16.2 |
| 3 | MEDIUM | **Aceito.** `MaxTentativasOutro` (padrão 3) com backoff, transição manual `Rejeitada → Separada` e CLI `--reenfileirar-i60`. | 4.2, 6.2, 9.3, 13, 16.2 |
| 4 | MEDIUM | **Aceito.** `OpcoesI60.Validar()` e `OpcoesRelatorioI60.Validar()` com as regras do achado, chamadas no `Worker` só com I51 ligada. | 4.2, 4.3, 13, 16.2 |
| 5 | MEDIUM | **Aceito.** Fallback só com chave de 44 dígitos, `try/catch (ArgumentException)`, XML carregado antes do write-ahead. | 9.3, 14, 16.2 |
| 6 | NIT | **Aceito.** `ConsultaI51Worker` valida as opções antes do 1º ciclo; `AtrasoInicialSegundos` mínimo 5. | 4.1, 13, 16.2 |
| 7 | NIT | **Aceito.** `I51:PadroesSemRetorno` (padrão vazio); `N` fora da lista conta como falha de consulta; Q14. | 4.1, 5.4, 16.2, 20 |
| 8 | NIT | **Aceito.** Itens opcionais marcados para corte (`Max4xxConsecutivos`, disjuntor e demais). | 4.2, 9.3, 19.1 |

Decisão do orquestrador incorporada: UNICO usa `I51:CampoUnico = Numero` por padrão (número da NOTA, 9 dígitos),
configurável para `NroDocumento`; as duas variantes são testadas no roteiro do homolog.
