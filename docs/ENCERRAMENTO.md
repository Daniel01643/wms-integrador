# Pausa do projeto — KitWms Integrador

Documento curto para quem voltar ao tema. Primeiro fechamento: 09/11/2026. Pausa: 10/11/2026.
**Atualizado em 13/11/2026, fim do dia** (release 2026.11.13-1 empacotada localmente; I51/I60 validadas no homolog; falta publicar no servidor).

> **Retomar:** publicar a release **2026.11.13-1** no servidor seguindo o checklist
> `.agents\tasks\checklist-release-2026-11-13.md` (pacote em `artifacts\2026.11.13-1`, gerado localmente,
> **não enviado ao servidor**). Ligar I51 e I60 (e a tela de devolução) só depois do OK do usuário.
> Escopo da I51/I60 em `docs\ESCOPO-I51-I60.md`; resultado do homolog em `docs\ROTEIRO-TESTE-I51-I60.md`.
> Notas TSP do homolog de 12/11 ficaram pendentes por proprietária BRF não encontrada (aguarda o fornecedor).

## 1. Estado na pausa

- Integra NF-e (XML) da pasta de entrada para o WMS Friozem via webservice (I50), **em produção**
  no servidor SRVAPPALC02 (Windows Server 2012 R2), serviço NSSM `Friozem-ImportacaoNotas`.
- Versão em produção: a última documentada é a série **2026.11.06-1 a -5** (diário de 06/11).
  **Conferir no servidor** qual está em `app\current`. **Release 2026.11.13-1 pronta e empacotada
  localmente (self-contained win-x64), ainda NÃO publicada no servidor.** Ela reúne tudo o que estava só no
  repositório: Puxada/Puxada TSP, I40 e tela de devolução (desligada), I51/I60 (desligadas), relatório novo
  e, desta release: janela de silêncio na coleta do pacote (`Pacote:ModoJanela=Silencio`, 15 s de silêncio,
  teto de 180 s; `Fixa` volta ao comportamento antigo), defaults `I60:QtdeVolumeZero` e
  `I60:QtdeTotalNotaZero` em `true`, placa (Puxada normal COM placa, TSP sem), retenção de **30 dias**
  (`Limpeza:DiasRetencao` e `I51:DiasRetencaoEstado`) e alerta imediato da I60 (`I60:AlertaImediato`).
- Resumo semanal de telemetria funcionando (verificado e reenviado em 09/11).
- **Puxada TSP implementada em 12/11/2026** (entra na release 2026.11.13-1, ainda não publicada no servidor):
  pastas `Puxada` e `PuxadaTSP` (e a raiz, aceita como puxada), leitura segura de ZIP, uma onda por ZIP
  na TSP (`TSP-AAAAMMDD-NN-NOME`) e relatório/e-mail com as colunas Tipo e Origem. **Testada no homolog
  com ZIPs reais em 12/11:** ondas criadas e notas enviadas, mas pendentes na interface por proprietária
  BRF não encontrada (aguarda o fornecedor).
- Relatório (12/11): tempo de integração (nível B) no relatório e no e-mail, cabeçalho com logo, título
  "Relatório de Integração", cartão Transportes corrigido.
- **I51 como gatilho da I60** (13/11): implementada e validada no homolog (7 de 7 notas associadas, com
  `qtdeVolume` e `qtdeTotalNota` em 0 e o evento interno do WMS que gera o retorno da separação). Em
  produção fica **desligada** até o OK do usuário e dos pré-requisitos do WMS de produção.
- Último build (13/11, cópia temporária): 0 avisos e 0 erros (Release). Testes: **926 aprovados, 9 ignorados (stress), 0 falhas** (detalhes em `.agents\tasks\relatorio-release-1311.md`).

### Frentes pausadas

| Frente | Estado | Onde está |
|---|---|---|
| **I60 automática (Plano A)** | **Pausada, aguardando o fornecedor.** No homolog: a I50 sobe; o código customizado de pós-processamento grava `QTD_VOLUMES`; com `qtdeVolume = 0` a conferência de volume passa; a I60 para em "quantidade caixa do pedido ... não confere ... 0". Hipótese: falta o vínculo `IDITEMDOCCARGA` do item da nota de venda, que não existe na interface I60. O código de pré-processamento da nota de venda dá `ORA-20901`. A associação **manual pela tela funciona**. I60 segue **desligada** em produção. | `docs\wms-codigo-customizado\HISTORICO-PLANO-A.md` (tentativas e perguntas prontas para o fornecedor) |
| **Devolução pela I40 (Plano B)** | **Formato validado no homolog** (10/11, nota 15671241): `IMP`, partes como no XML, recebimento feito do início ao fim. **Tela implementada e desligada** (`Devolucao:Habilitada=false`), ainda não testada no homolog. Pesquisa por chave **ou por número da nota + série**. **Placa removida da tela e do código** (o `ICD` com placa foi testado e deu "erro na criação do controlador de veículo"; o `IMP` sem placa funcionou). | `docs\ARQUITETURA-PLANO-B-I40.md`, `docs\ROTEIRO-TESTE-I40.md` (seção 9: teste da tela) |

## 2. Mapa rápido

| O quê | Repo (`c:\wms-integrador`) | Servidor |
|---|---|---|
| Código | `src\KitWmsIntegrador.Core`, `src\KitWmsIntegrador.Worker`, `tests\` | `C:\Friozem\importacao_notas\app\current` |
| Config padrão | `src\KitWmsIntegrador.Worker\appsettings.json` | variáveis de ambiente (`WMS__*`, `I60__*`, `Devolucao__*`) |
| Scripts de operação | `deploy\` | `C:\Friozem\importacao_notas\tools\deploy` |
| Entrada de ZIP/XML | — | `C:\Friozem\kitbrf` (`Puxada\`, `PuxadaTSP\` e raiz) |
| Destinatários / telemetria | modelos em `deploy\config-exemplo` e `docs\exemplos` | `config\destinatarios-resumo.txt`, `data\telemetria\` |
| Códigos customizados do WMS | `docs\wms-codigo-customizado` | WMS (homolog) |
| Manuais | `docs\manuais` | `C:\Friozem\importacao_notas\manuais` |
| Apresentação | `docs\apresentacao\apresentacao.html` e `.pdf` | — |
| Histórico e horas | `docs\diario`, `docs\controle`, `docs\CONTROLE-ESFORCO.md` (só horas) | — |

## 3. Dia a dia

Não duplicado aqui. Usar `docs\manuais\MANUAL-USUARIO` (operador), `docs\manuais\MANUAL-TECNICO`
(técnico nível 1), `docs\OPERACAO.md` e `docs\INSTALACAO-SERVIDOR.md` (instalar, publicar release,
rollback, segredos).

## 4. Decisões fechadas

- **I60 desligada por flag** (`I60:Habilitada=false`); código mantido. Opções de diagnóstico:
  `I60:TipoOperacaoNota` (padrão `INE`), `I60:QtdeVolumeZero`, modo manual `--enviar-i60 <xml>`.
- **I40 (devolução):** `tipoNota = DE`, `tipoOperacaoNota = IMP` (padrão), CFOP da venda, partes como
  no XML (`InverterPartes = false`), tela sem login. **Sem placa nem motorista** (removidos em 10/11/2026; `placaVeiculoPrevistoNota` e `nomeMotorista`
  sempre em branco). A tela pesquisa por **chave ou por número + série** (número e série lidos do nome do
  arquivo `NFE<chave>.XML`, CNPJ do depositante, sem pasta `Erro`; mais de uma chave = o colaborador escolhe).
  Modo manual `--enviar-i40 --chave ...` ou `--numero ... --serie ...`.
- **Puxada x Puxada TSP (12/11/2026):** o colaborador salva só ZIPs; tudo em `PuxadaTSP` sobe como TSP
  (uma onda por ZIP, `TSP-AAAAMMDD-NN-NOME`, NOME cortado em 14 caracteres) e tudo em `Puxada` como
  puxada (onda = transporte). XML solto na raiz continua aceito como puxada (Origem `raiz`, opção
  `Pastas:AceitarRaiz`). O relatório mostra Tipo e Origem para corrigir pasta errada. Limites de ZIP em
  `Zip:MaxArquivos` (1000) e `Zip:MaxTamanhoMb` (500). Escopo em `ESCOPO-PUXADA-TSP.md`.
- **Retenção de 30 dias** (ajustada de 60 em 13/11/2026) em `Processados`, `Erro` e `Estado\i51`: a tela de devolução só acha notas dos últimos 30 dias. (Entre 09/11 e 13/11 era 60 dias.)
- **Esforço registrado só em horas** (steering `.kiro\steering\esforco-em-horas.md`).
- **Subpastas por pacote** em `Processados` e `Erro`.
- **Não-XML** vai para `Erro\<pacote>\ExtensaoInvalida` com `.motivo.txt`; temporários ignorados.
- **Placa do veículo** na I50 via `I50:EnviarPlacaVeiculo` (padrão ligado: Puxada normal COM placa) para a Puxada. A **Puxada TSP vai sempre sem placa**
  (13/11/2026): chave própria `I50:EnviarPlacaVeiculoTsp`, padrão `false`.
- **TLS 1.2/1.3 forçado** no cliente HTTP e **GetToken com retry** (causa dos 401 em massa no 2012 R2).
- **Resumo semanal**: segunda 12:00 (hora local do servidor).

## 5. Pendências e riscos

| Item | Dono sugerido |
|---|---|
| **Puxada TSP:** testar com ZIP real no homolog (duas rodadas) seguindo `docs\ROTEIRO-TESTE-PUXADA-TSP.md`; validar que o WMS aceita `numeroPlanejamento` igual ao nome da onda | Usuário |
| **Puxada TSP:** criar `Puxada` e `PuxadaTSP` em `C:\Friozem\kitbrf` e publicar a release com a Puxada TSP (`INSTALACAO-SERVIDOR.md`, §3.4c) | Usuário / TI |
| Enviar ao fornecedor as perguntas da I60 (`HISTORICO-PLANO-A.md`) | Usuário |
| Testar a tela de devolução no homolog (`ROTEIRO-TESTE-I40.md`, seção 9) | Usuário |
| Publicar a release 2026.11.13-1 (retenção de 30 dias, janela de silêncio, alerta I60, I40 com a tela desligada), criar a regra de firewall da rede interna e só então ligar a tela | Usuário / TI |
| **Texto do ENC:** o texto real do WMS para doccarga encerrado ainda não foi capturado; calibrar `I60:PadroesDocCargaEncerrado` com o e-mail de alerta e `Estado\i51\alertas` quando acontecer em produção | Usuário / IA |
| Ligar `I51:Habilitada` e `I60:Habilitada` JUNTAS em produção, só depois de EDI WEBAPI-I51 com WEBSERVI e do evento interno do WMS de produção | Usuário / equipe WMS |
| Homolog: desativar o código customizado de pré-processamento da nota de venda (teste) e voltar "ChecaItensVenda" para SIM | Usuário |
| Homolog: o pós-processamento `QTD_VOLUMES` do DocCarga continua ativo e aborta doccarga sem caixas (`QTDVOL-POS:2`) | Usuário |
| Restaurar o `email.settings.json` local (renomeado para `*.off-teste` no projeto e em `bin` durante os testes) | Usuário / IA |
| Repositório sem remote: o histórico existe só nesta máquina (push para o GitHub do usuário) | Usuário |
| Trocar senhas que apareceram em chat (e-mail, admin do servidor, SMTP) | Usuário / TI |
| Conta WMS de produção em provisionamento (401 intermitente deve estabilizar) | Usuário / equipe WMS |
| Apresentação e treinamento de 1h | Usuário |
| Atualizar o SO do servidor (2012 R2 fora de suporte) | TI |
| Arquivo local `~/.srv-cred.xml` (credencial WinRM cifrada por DPAPI): mantido por decisão do usuário | Usuário |

## 6. Como retomar

1. Obter o repositório (clonar ou copiar `c:\wms-integrador`) e abrir em .NET 8 SDK.
2. `dotnet build KitWmsIntegrador.sln` e `dotnet test tests\KitWmsIntegrador.Tests\KitWmsIntegrador.Tests.csproj`.
3. Ler o diário mais recente em `docs\diario` (hoje `2026-11-12.md`) e este documento.
4. No servidor: conferir o serviço `Friozem-ImportacaoNotas` (rodando) e a release em `app\current`.
5. Conferir a telemetria: `data\telemetria\ultimo-resumo.txt` e o e-mail de segunda 12:00.
6. I60: ver a resposta do fornecedor e retomar por `HISTORICO-PLANO-A.md`. I40: seguir `ROTEIRO-TESTE-I40.md`.
7. Manuais em `docs\manuais`; regenerar com `powershell -ExecutionPolicy Bypass -File docs\manuais\gerar-manuais.ps1`
   e depois gerar os PDF (ver `docs\manuais\README.md`).
8. PDF da apresentação via Edge headless (salve o HTML em **UTF-8 com BOM** para não quebrar acentos):

   ```powershell
   & 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe' --headless=new --disable-gpu --no-pdf-header-footer "--print-to-pdf=<saida.pdf>" ("file:///" + "<caminho/com/barras/normais>/apresentacao.html")
   ```

9. O `gerar-manuais.ps1` lê a logo base64 da linha 125 de `apresentacao.html` (com fallback para a
   primeira ocorrência); evite reordenar essa parte do arquivo.
