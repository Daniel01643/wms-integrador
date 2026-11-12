# Brief do workflow — I51 como gatilho da I60 (NÃO disparado; aguardando ordem do usuário)

Este é um trabalho PESADO: novo fluxo assíncrono (consulta periódica), estado persistente,
mudança no disparo da I60, novo relatório diário. Atravessa ClienteWms, Construtores, Pacote,
Relatorio, Email, Monitoramento e Worker.

## Repositório e regras

- Repo: `c:\wms-integrador` (.NET 8, `KitWmsIntegrador.sln`), branch `main`, sem worktree.
- **Não fazer push.** Commits locais com mensagens claras.
- Repo GitHub PÚBLICO: nunca versionar `*.xml`, `*.zip` nem dados reais (hooks bloqueiam).
- Não editar `docs/CONTROLE-ESFORCO.md`, `docs/controle/*.md`, `docs/diario/*`.
- Esforço só em horas (nunca valores monetários).
- Devolução (I40) fica fora dos manuais oficiais.

## Fonte da verdade

- Escopo e decisões do usuário: `c:\wms-integrador\docs\ESCOPO-I51-I60.md` (ler inteiro).
- Layout da I51 (resumo do manual do fornecedor, enviado pelo usuário em PDF):
  - Identificador: `WEBAPI-I51` (deve ser CONFIGURÁVEL; o número pode mudar).
  - Requisição: `{"CONSULTA":{"TIPO":"TODOS|UNICO","CNPJ":"00.000.000/0000-00","numero":"0","serie":"0","chaveNFe":"0","nroDocumento":"5287"}}`.
    Usar apenas TIPO, CNPJ e número. `numero` = 9 dígitos com zeros à esquerda; `serie` 3 dígitos;
    `chaveNFe` vazio ou 44 dígitos; `nroDocumento` até 20 dígitos.
  - TODOS: devolve o que estiver disponível e não devolve de novo o que já foi puxado
    → gravar a resposta bruta em disco ANTES de processar.
  - UNICO: devolve só o pesquisado, repetível → recuperação/conferência.
  - Retorno: `{transacao, sucesso: "S"|"N", mensagem, documentos:[{CnpjProprietario, CnpjDestinatario,
    CodDestinatario, Numero (pedido/doccarga), Tipo, SubTipo, ..., Placa, Doca, itens:[{numeroSequencialItem,
    codProduto, codProdCliente, CodEmbalagem, qtdeProduto, qtdeProdutoSep, qtdeEmbalagem, qtdeEmbalagemSep,
    detalhes:[{nroEtiqueta, nroLote, codVolume, dataFabricacao, dataValidade, qtdeCaixaSep, qtdeProdutoSep}]}],
    volumes:[...]}]}`. Quantidades vêm como string decimal com ponto ("24.0000"). Datas "DD/MM/YYYY HH:MM:SS".
  - `sucesso = "N"` = nada a retornar (não é erro).
  - O retorno só existe com a separação CONCLUÍDA (evento do WMS); não há parcial.

## Pontos de partida no código

- Cliente WMS: `src\KitWmsIntegrador.Core\ClienteWms\` (ClienteWmsHttp com GetToken/Bearer e retry,
  InterfaceWms, JsonWms, OpcoesWms, RespostaWms/RetornoWms).
- I60 atual: `src\KitWmsIntegrador.Core\Construtores\ConstrutorI60.cs`, `Configuracao\OpcoesI60.cs`
  (`I60:Habilitada=false` nos appsettings). Descobrir onde a I60 é disparada hoje (provavelmente
  junto da I50 em `Pacote\ProcessadorLote.cs`) e passar a dispará-la SOMENTE pelo fluxo da I51.
- Pacote/TSP: `src\KitWmsIntegrador.Core\Pacote\` (ProcessadorLote, FechadorPacote, RegistroOndasTsp).
- Relatórios: `src\KitWmsIntegrador.Core\Relatorio\`, e-mail `src\KitWmsIntegrador.Core\Email\`,
  agendamento do resumo semanal em `src\KitWmsIntegrador.Core\Monitoramento\` (reaproveitar para 12:00 diário).
- Worker: `src\KitWmsIntegrador.Worker\` (appsettings.json, registro de serviços).
- Histórico do Plano A/I60: `docs\wms-codigo-customizado\HISTORICO-PLANO-A.md`, `docs\ANALISE-I60-VS-I50.md`.

## Requisitos

1. `I51:Habilitada` (padrão false), `I51:Identificador` (padrão `WEBAPI-I51`), `I51:IntervaloMinutos`
   (padrão 5), `I51:HorasParaAtraso` (padrão configurável, sugerir 24), retenção do estado.
2. Estado persistente por nota em `Estado\i51\` (sobrevive a reinício; escrita atômica; nunca lança
   por falha de disco): AguardandoSeparacao → Separada → I60Enviada → Associada | Rejeitada; Atrasada.
   Registrar nota, série, chave, CNPJ, transporte, onda (inclusive TSP), datas de cada transição.
3. Consulta periódica (BackgroundService) com TODOS; gravar resposta bruta antes de processar;
   casar documentos às notas aguardando; UNICO (CNPJ + número) para recuperação de notas atrasadas
   ou após falha no processamento de um TODOS.
4. Ao separar: montar a I60 com a MESMA NF-e (XML) da I50 (confirmado pelo usuário),
   AJUSTANDO as quantidades para as separadas; registrar original × separada por item.
4a. Doccarga em status ENC (encerrado): enviar a I60 como `IMP` (`tipoOperacaoNota` e `Tpsit` = `IMP`;
   exemplo de payload IMP em `c:\wms-integrador\docs\chamado\I60-associacao-manual-IMP.json`)
   e registrar o erro "doccarga encerrado" no estado e no relatório.
   Caso normal: **`INE`** (padrão definido pelo usuário; NÃO usar `IMB` — o arquivo
   `I60-associacao-automatica-IMB.json` é só referência de estrutura).
   Detecção do ENC a confirmar com o usuário (ver escopo);
   hipótese padrão: recusa da I60 `INE` com mensagem de encerrado (padrões configuráveis) → reenvio `IMP`.
   Atenção ao "Nota [...] já existe, TpAcao precisa ser UPD!" (`docs\chamado\respostas-homolog.txt`):
   o reenvio pode exigir `TpAcao = UPD`; deixar configurável e coberto por teste com WMS falso.
4b. Tela de devolução pela I40 (Plano B; `c:\wms-integrador\docs\ARQUITETURA-PLANO-B-I40.md`,
   roteiro `c:\wms-integrador\docs\ROTEIRO-TESTE-I40.md`): é a ÚLTIMA alternativa para devolução, mas deve
   continuar PRONTA E FUNCIONANDO atrás de `Devolucao:Habilitada` (false nos appsettings). Não pode
   quebrar: rodar/ampliar os testes existentes da I40 (tipoOperacaoNota IMP, InverterPartes=false, busca
   por chave OU número+série, sem placa/motorista). Avaliar listar no relatório diário as notas que
   ficaram `IMP`/rejeitadas como candidatas à devolução pela I40. Continua FORA dos manuais oficiais.
5. I60 não é mais enviada junto da I50 quando `I51:Habilitada=true`. Com I51 desligada, o
   comportamento atual permanece idêntico.
6. Ondas TSP também passam pelo fluxo.
7. Relatório diário às 12:00 (HTML + PDF + e-mail, identidade visual atual "Relatório de Integração"):
   aguardando separação (com idade), separadas, I60 associadas, I60 rejeitadas com motivo
   (doccarga encerrado, quantidade diferente etc.), ajustes de quantidade item a item, atrasadas.
   AVALIAR e, se viável, incluir também visão por pacote (seção no relatório do pacote ou relatório
   por pacote) — documentar a decisão.
8. Telemetria/log sem segredos (nunca token, senha ou corpo integral da nota).
9. Documentação técnica: atualizar `docs\ESCOPO-I51-I60.md` (seção "implementado") e ARQUITETURA;
   roteiro de teste no homolog em `docs\ROTEIRO-TESTE-I51-I60.md`. Manuais oficiais só depois do teste no homolog.

## Verificação

- `dotnet build c:\wms-integrador\KitWmsIntegrador.sln -c Release` com 0 avisos.
- `dotnet test c:\wms-integrador\KitWmsIntegrador.sln -c Release` (linha de base: 655 aprovados, 9 ignorados).
  Se o Worker estiver rodando e travar DLL, testar em cópia temporária.
- Testes novos com WMS falso: TODOS consumível (não repete), UNICO repetível, `sucesso=N`, ajuste de
  quantidade, nota TSP, reinício no meio (estado persistido), atrasada, I51 desligada = comportamento antigo.
- Gerar relatório diário de exemplo (dados fictícios) e conferir com screenshot do Edge headless.
- Teste real no homolog fica para depois (proprietária BRF pendente no fornecedor).

## Forma do workflow

Design + revisão de design primeiro (pesado), depois plano, depois loop implementação/revisão
(revisor por último, veredito em `c:\wms-integrador\.agents\tasks\review-i51-i60.json`).
Relatório final em `c:\wms-integrador\.agents\tasks\relatorio-i51-i60.md`.

## Deck e manuais (pedido do usuário, 12/11/2026: "tudo isso deve ser incluído no deck e nos manuais, conforme fizemos antes")

- **Escopo:** fluxo I51 → I60 (`INE`), regra ENC → `IMP`, ajuste para quantidade separada, consulta
  TODOS/UNICO a cada N min, ondas TSP no fluxo, relatório diário 12:00, chaves `I51:Habilitada`/`I60:Habilitada`,
  e a **tela de devolução I40 como última alternativa**.
- **Mudança de decisão:** antes a devolução (I40) ficava FORA dos manuais. Agora o usuário pediu "tudo isso"
  nos manuais. Incluir a I40 nos manuais **apenas como "última alternativa"** (pronta, desligada por padrão),
  numa seção própria e fácil de remover. Registrar no relatório final que a I40 entrou nos manuais, para o
  orquestrador confirmar com o usuário.
- **Status:** marcar com clareza o que está implementado e o que aguarda teste no homolog (proprietária BRF
  pendente no fornecedor; detecção do ENC a confirmar).
- **Manuais:** fontes em `c:\wms-integrador\docs\manuais\`. Gerar com
  `powershell -NoProfile -ExecutionPolicy Bypass -File c:\wms-integrador\docs\manuais\gerar-manuais.ps1`.
  PDFs via Edge:
  `& 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe' --headless=new --disable-gpu --no-pdf-header-footer "--print-to-pdf=<pdf>" ("file:///"+<html>.Replace('\','/'))`.
  Seguir o padrão dos manuais já existentes (o mesmo usado para a Puxada TSP).
- **Deck:** `c:\wms-integrador\docs\apresentacao\apresentacao.html` (UTF-8 COM BOM; 12 slides; logo em base64
  grande em cada slide). Fazer **edições cirúrgicas**, sem reescrever o arquivo inteiro e preservando o BOM.
  Incluir/atualizar o slide do fluxo (I50 → Separação → I51 → I60, ENC → IMP, I40 por último). Regerar
  `apresentacao.pdf` pelo Edge e conferir com screenshots (cópia temporária com CSS injetado antes de `</head>`).
  Usar classes de compactação já existentes (compact2/compact7/compact12) se o rodapé ou o slide estourar.
- **Regra de esforço:** deck e manuais com esforço SOMENTE em horas (humanas e IA), nunca valores monetários.
  Não inventar horas humanas. Não mexer no slide comparativo de horas (o orquestrador consolida).
- **Ordem:** deck e manuais por último, depois da implementação aprovada no review, refletindo o que foi
  realmente implementado.
