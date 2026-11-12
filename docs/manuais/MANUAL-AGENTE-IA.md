# Manual do Agente de IA — KitWms Integrador

Guia operacional e de **guarda-rails** para um agente de IA acionado por um colaborador da Friozem
para **diagnosticar falhas** ou **propor alterações** no KitWms Integrador. Tom técnico e objetivo.
Opere com base em fatos verificáveis (código em `src/`, scripts em `deploy/`, docs em `docs/`),
nunca em suposições sobre comandos, caminhos, status ou campos.

## Índice

1. [Visão do fluxo](#1-visão-do-fluxo)
2. [Mapa de arquivos](#2-mapa-de-arquivos)
3. [Como diagnosticar falhas](#3-como-diagnosticar-falhas)
4. [Como verificar uma mudança](#4-como-verificar-uma-mudança)
5. [O que NÃO fazer (guarda-rails)](#5-o-que-não-fazer-guarda-rails)
6. [Como propor alterações com segurança](#6-como-propor-alterações-com-segurança)
7. [Referências](#7-referências)

---

## 1. Visão do fluxo

```text
ZIP/XML na entrada (subpastas Puxada e PuxadaTSP; raiz se Pastas:AceitarRaiz=true)
  → ZIP: leitor seguro extrai os XMLs para Estado\tmp (limites Zip:MaxArquivos/MaxTamanhoMb)
  → PUXADA: agrupa por transporte (campo "Transporte: N" nas infos complementares)
  → TSP (só PuxadaTSP): uma onda por ZIP, nome TSP-AAAAMMDD-NN-NOME (Estado\tsp\ondas.json)
  → Orquestrador reativo por nota: I50 → pré-condições I10/I30 → reenvio, até 3 tentativas
  → move cada arquivo (Processados\pacote_<...> ou Erro\pacote_<...>\<Categoria>)
  → gera relatório HTML + PDF (sempre)
  → envia um e-mail só do pacote (conforme Email:EnviarQuando)
  → registra telemetria do pacote
```

Pontos de comportamento confirmados no código/`docs/OPERACAO.md`:

- O serviço faz **polling** da pasta a cada `Pacote:IntervaloVarreduraSegundos` (padrão 5 s);
  `FileSystemWatcher` não é usado (perde eventos em pasta de rede).
- O pacote é uma **foto**: ao detectar o primeiro arquivo, abre-se o pacote e calcula-se **uma
  única vez** o identificador `pacote_<yyyyMMdd_HHmmss>` (horário local). Espera-se a **janela fixa**
  `Pacote:JanelaColetaSegundos` (10 s); novos arquivos não reiniciam a janela.
- Um arquivo só entra na foto quando "pronto" (tamanho/data estáveis por
  `Pacote:EstabilizacaoSegundos` + abre com acesso exclusivo). Arquivo instável vira "instável" após
  `TimeoutArquivoSegundos` (300 s) e sozinho nunca abre pacote; fotos vazias aplicam backoff
  exponencial até `BackoffMaximoSegundos` (120 s).
- **Orquestrador reativo** (`Core/Orquestrador/Orquestrador.cs`): coordena I50 e as pré-condições
  I10/I30, com até `Wms:MaxTentativas` (3) por nota. **I60 está desligada** (`I60:Habilitada=false`;
  dados fiscais vão na I50).
- Estados de negócio (`EstadoIntegracao`): `Integrada`, `Pendente`, `NaoIntegrada`. Status exibidos
  no relatório (`LinhaRelatorioErro.StatusDescricao`): **Integrada**, **Integrada — revisar
  produto**, **Pendente**, **Não integrada**. "XML inválido" **não** é status de linha: é categoria
  de erro de pasta (`Erro\XmlInvalido`) e contador no e-mail/resumo.
- **Tipo e Origem**: o tipo da nota (`PUXADA` ou `TSP`) vem da pasta (`PuxadaTSP` = TSP; `Puxada` e
  raiz = PUXADA); a origem (`Puxada`, `PuxadaTSP`, `raiz`) é gravada no diário e aparece no relatório
  e no e-mail. Não há detecção automática de pasta errada. ZIP inválido vai para
  `Erro\pacote_<...>\ZipInvalido`.
- **Reinício do serviço**: o diário (`Estado\pacote-atual.jsonl`) é a fonte de verdade; há
  deduplicação e reconciliação de moves sem reenviar ao WMS.

## 2. Mapa de arquivos

### 2.1 No servidor (SRVAPPALC02)

| Caminho | Conteúdo |
|---|---|
| `C:\Friozem\importacao_notas\logs\worker.out.log` / `worker.err.log` | Logs do serviço (stdout/stderr via NSSM). |
| `C:\Friozem\importacao_notas\config\appsettings.Production.json` | Config **não-secreta** (ex.: `Wms:BaseUrl`, `Monitoramento`, `Pacote`, `Limpeza`). |
| `C:\Friozem\importacao_notas\config\email.settings.json` | SMTP (segredo) — `smtp.friozem.com.br:587`, `UsarTls=false`. |
| env de MÁQUINA `WMS__Usuario` / `WMS__Senha` | Credenciais do WMS (segredo). |
| `C:\Friozem\importacao_notas\data` (previsto) | **Estado** (`pacote-atual.jsonl`) e **telemetria** (`telemetria.jsonl`, `servico.jsonl`, `ultimo-resumo.txt`, `resumos\`), fora do share. |
| `C:\Friozem\kitbrf` (share `kitbrf`) | Entrada (`Puxada\`, `PuxadaTSP\`, raiz) + `Processados\`, `Erro\`, `relatorios\` (visíveis na rede). `Estado\tsp\ondas.json` guarda o sequencial do dia e a onda de cada ZIP TSP; `Estado\tmp\` é a extração temporária de ZIPs. |
| `C:\Friozem\importacao_notas\app\current` | Release ativa (apontada por `publicar-release.ps1`/`rollback.ps1`). |
| `C:\Friozem\importacao_notas\app\releases\<versao>` | Releases instaladas. |
| `C:\Friozem\Gestao_Enderecos\tools\nssm.exe` | NSSM que hospeda o serviço `Friozem-ImportacaoNotas`. |

> O caminho físico exato de `Estado`/telemetria depende de `Pastas:Estado` e
> `Monitoramento:CaminhoTelemetria` na config de produção. Sem override absoluto, o código resolve
> `Estado` **relativo à Entrada** (`C:\Friozem\kitbrf\Estado`). **Confirme o valor efetivo antes de
> afirmar o caminho.**

### 2.2 No repositório

| Caminho | Conteúdo |
|---|---|
| `src\KitWmsIntegrador.Core\` | Domínio: `Pacote`, `Orquestrador`, `Email`, `Monitoramento`, `Relatorio`, `ClienteWms`, `Configuracao`, `Agrupamento`, `Construtores`, `Leitura`, `Modelos`, `Estado`, `Tempo`, `Notificacao`. |
| `src\KitWmsIntegrador.Core\Pacote\` (Puxada TSP) | `ClassificadorEntrada` (pastas e tipo), `LeitorZipSeguro` (ZIP), `NomeadorOndaTsp` e `RegistroOndasTsp` (nome e sequencial da onda), `ProcessadorLote` (onda única por ZIP). Config: `Core\Configuracao\OpcoesPasta.cs` e `OpcoesZip.cs`. |
| `src\KitWmsIntegrador.Worker\` | Host do serviço: `Program.cs` (binding de config), `Worker.cs`, `Configuracao\OpcoesEntrada.cs`. |
| `deploy\*.ps1` | `diagnostico`, `publicar`, `instalar`, `configurar-segredos`, `servico`, `publicar-release`, `rollback`, `remover`, `compartilhar-pasta`, `_comum`. |
| `docs\` | `OPERACAO.md`, `INSTALACAO-SERVIDOR.md`, `AMBIENTE-SERVIDOR.md`, `RELATORIO-LOGS.md`, `manuais\`. |
| `tests\KitWmsIntegrador.Tests` | Testes (xUnit) com fakes de WMS/SMTP. |

Telemetria é **JSON Lines** (`.jsonl`): uma linha por evento (pacote fechado / saúde do serviço).

## 3. Como diagnosticar falhas

Comece **sempre** pelos logs: `worker.err.log` e `worker.out.log`. Padrões de erro e causa provável:

| Sintoma no log / comportamento | Causa provável | Ação de diagnóstico |
|---|---|---|
| `HTTP 401 no envio WMS ...; renovando token e repetindo uma vez.` repetido | **Credencial WMS inválida** (`WMS__Usuario`/`WMS__Senha`). | Conferir env de máquina; validar com quem administra o WMS. Correção = `configurar-segredos.ps1` + restart. |
| Muitas notas **Pendente** de uma vez | Algo no **WMS** (fila/processamento), não no integrador. | Verificar o WMS; o integrador entregou a nota. |
| `Falha ao enviar o e-mail do pacote: <Motivo>` / resumo gravado em disco | **SMTP**: host/porta/credencial/`UsarTls`. | Conferir `email.settings.json` e `diagnostico.ps1` (SMTP host/porta). |
| Falha de conexão HTTPS ao WMS, sem texto fixo de "handshake" | **TLS 1.2** no SChannel (Server 2012 R2). | `diagnostico.ps1` lê o registro do SChannel; mitigação em `INSTALACAO-SERVIDOR.md` §3.6/§6. |
| Arquivo "preso" na entrada, não entra na foto | Arquivo instável (cópia travada) por mais de `TimeoutArquivoSegundos`. | Verificar se a origem terminou de gravar; o arquivo sozinho não abre pacote. |
| `Serviço não iniciado: corrija a seção 'Pastas' do appsettings.` | Config de `Pastas` inválida (ex.: `Estado` dentro de `Processados`/`Erro`). | Corrigir `appsettings.Production.json`. |

Para um retrato de saúde sem alterar nada, use `deploy\diagnostico.ps1` (só leitura, sempre
`exit 0`): checa serviço, WMS host/porta, SMTP, pasta de entrada, share e o registro do SChannel.

## 4. Como verificar uma mudança

Antes de propor qualquer alteração de código como concluída, ela precisa passar por build e testes
no repositório (fora de produção):

```bash
dotnet build
dotnet test
```

Para validar comportamento de runtime, rode o **Worker localmente** apontando para uma **pasta
temporária** de entrada e usando os **fakes** de WMS/SMTP já presentes nos testes — nunca aponte
para a entrada real nem credenciais reais em testes exploratórios.

- **Nunca** execute teste de carga pesada contra produção ou homologação **sem aprovação explícita**
  do responsável.
- Prefira cenários pequenos e determinísticos (poucos XMLs, um transporte) ao reproduzir um bug.

## 5. O que NÃO fazer (guarda-rails)

- **Não** altere as decisões do Orquestrador (I50/I10/I30, nº de tentativas, classificação de erro)
  sem necessidade comprovada e aprovação — é o núcleo do comportamento de integração.
- **Não** ligue a I60 (`I60:Habilitada` deve seguir `false`; a I60 foi testada no homolog e está pausada aguardando o fornecedor; os dados
  fiscais vão na I50).
- **Não** versione, imprima em log ou cole em respostas segredos (WMS/SMTP). Trate `email.settings.json`
  e as env `WMS__*` como sensíveis.
- **Não** apague nem edite `Estado`/telemetria (fonte de verdade da retomada e do resumo semanal).
- **Não** rode testes de carga reais (produção/homolog) sem OK do responsável.
- **Não** invente comandos, parâmetros, caminhos ou status. Use apenas o que existe nos arquivos; se
  não conseguir confirmar, sinalize a lacuna em vez de afirmar.

## 6. Como propor alterações com segurança

1. **Config antes de código.** Se o objetivo pode ser atingido ajustando `appsettings.Production.json`,
   `email.settings.json` ou os `.txt` de destinatários, prefira isso — é reversível e não exige
   recompilar. Nunca faça hardcode de segredo.
2. **Mudança de código** (quando inevitável): mantenha o escopo mínimo, siga o estilo do projeto,
   e submeta a **build + test + revisão humana** antes de promover. Pontos de extensão existem
   (ex.: trocar o envio de e-mail implementando `IEnviadorEmail` sem mexer no resto).
3. **Separe diagnóstico de ação.** Para produção, apresente o diagnóstico e a correção proposta, e
   deixe a execução (restart, publicar, rollback) para o técnico humano, conforme o
   [Manual Técnico](MANUAL-TECNICO.md).
4. **Documente a lacuna.** Divergências entre o ambiente e o código (ex.: caminho físico de `Estado`,
   URL de produção do WMS sem `:8888`, caminho do `destinatarios-resumo.txt`) devem ser reportadas,
   não resolvidas por suposição.

## 7. Referências

- [Manual do Usuário](MANUAL-USUARIO.md) — depósito de XMLs e leitura de relatórios.
- [Manual Técnico (Nível 1)](MANUAL-TECNICO.md) — operação no servidor, segredos, serviço, logs.
- [`docs\OPERACAO.md`](../OPERACAO.md) — fluxo do pacote, pastas, e-mail e monitoramento.
- [`docs\INSTALACAO-SERVIDOR.md`](../INSTALACAO-SERVIDOR.md) — instalação, TLS 1.2, publicar/rollback.
