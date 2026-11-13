# Arquitetura do Plano B: devolução pela I40

Data: 2026-11-09 (desenho). Atualizado após a fase 1 (seções 9 a 11) e após o teste real no
homolog e a fase 2 (seções 13 a 18) e após as decisões E e F (seção 19: busca por número + série e remoção da placa). Status: fase 1 (construtor e envio manual) e fase 2 (tela
web no Worker) implementadas e testadas com fakes; **a tela está desligada**
(`Devolucao:Habilitada=false`) e nada foi ligado em produção. O roteiro de teste no homolog está
em `docs/ROTEIRO-TESTE-I40.md`.

> Atenção: as seções 9 a 12 registram o estado **antes** do teste real. Onde divergem das
> seções 13 a 18 (tipo de operação ICD por padrão, placa sempre obrigatória, `POST /api/devolucao`),
> valem as seções 13 a 18. Onde as seções 13 a 18 falam de placa, motorista ou checkbox "Enviar
> placa do veículo", vale a seção 19: **a placa foi removida da tela e do código**.

## 1. Objetivo

O colaborador da devolução hoje precisa procurar, entre muitos XMLs, as notas do processo.
A proposta é uma tela web simples, hospedada pelo próprio serviço no servidor, onde ele digita
ou bipa (leitor de código de barras) as chaves de NF-e. A rotina:

1. localiza o XML da **nossa venda original** pela chave;
2. converte o XML para a I40 (`WEBAPI-I40`, função `EDI_JS_IMP_I40`) com `tipoNota = "DE"` (devolução);
3. envia ao WMS e mostra o resultado por chave.

Decisões do usuário: sem autenticação real (a tela só lê XML e envia, não guarda nada e não altera dados
locais); retenção de 30 dias (ajustada de 60 para 30 em 13/11/2026; já aplicada em `Limpeza:DiasRetencao`); XML convertido é o da
venda original.

## 2. Fluxo

```
Tela web (chaves)  ->  POST /api/devolucao  ->  LocalizadorXml  ->  LeitorNotaFiscalXml
                                                                 ->  ConstrutorI40  ->  IClienteWms.EnviarAsync(I40)
                                       <-  resultado por chave (ok / não encontrada / já enviada / erro WMS)
```

1. A tela aceita 1 a 30 chaves por envio (limite da raiz `NOTA_FISCAL`, 1-30 notas). Cada chave
   é validada: 44 dígitos numéricos (o leitor de código de barras digita e envia Enter).
2. `LocalizadorXml` procura `NFE<chave>.XML` em `<Entrada>\Processados\pacote_*\`
   (comparação sem diferenciar maiúsculas). Se houver mais de um (reprocesso), usa o mais
   recente por `LastWriteTimeUtc`. Os XMLs de `Erro\` não entram (nota que não integrou
   não existe no WMS e não pode ser devolvida).
3. `LeitorNotaFiscalXml` (existente) gera o `NotaFiscal`.
4. `ConstrutorI40` (novo) monta o payload; um envio por requisição, com todas as notas válidas.
5. O resultado volta por chave; chaves não encontradas não bloqueiam as demais.

## 3. Mapeamento I40 <- XML/config

Fonte: `NotaFiscal`/`ItemNota`/`VolumeTransporte` (`src\KitWmsIntegrador.Core\Modelos`),
`LeitorNotaFiscalXml`, `FormatoWms` (`Data`, `Documento`, `Truncar`), `OpcoesOperacionais`
(`CnpjDepositante`, `CnpjArmazem`, `Deposito`). "Parser+" indica campo que o parser ainda
não extrai (precisa ampliar `LeitorNotaFiscalXml`/modelo); "Fixo/branco" é valor constante.

### Cabeçalho (NOTA_FISCAL)

| Campo I40 | Origem | Observação |
|---|---|---|
| cnpjEmitente | `EmitenteCnpj` -> `FormatoWms.Documento` | Ver pergunta P1 (inversão na devolução). |
| cnpjRemetente | `EmitenteCnpj` | Idem P1. |
| cnpjDestinatario | `DestinatarioCnpj` | Idem P1. |
| cnpjLocalEntrega | `DestinatarioCnpj` | Idem P1. |
| cnpjTransportadora | `TransportadoraCnpj`; se vazio, `CnpjArmazem` (config) | Mesma regra da I60. |
| codNatOperacaoFiscal | `Itens[0].Cfop` | Pergunta P2: CFOP de devolução (ex.: 1202/5202) ou o da venda? |
| numeroNota | `Numero` | |
| serieNota | `Serie` | |
| chavenfe | `ChaveNFe` (44) | |
| protocolo | `Protocolo` | Já extraído (`nProt`). |
| cnpjNota | `EmitenteCnpj` | Como na I60. |
| tipoNota | Fixo `DE` | Devolução. Valores aceitos: OR/AR/NO/OI/OE/DE/CD/OC/SU/SD. |
| dataEmissao | `DataEmissao` -> `FormatoWms.Data` (`DD/MM/YYYY HH:MM:SS`) | |
| qtdeTotalNota | Soma de `Itens[].Quantidade` | |
| pesoBrutoKg / pesoLiquidoKg | `Volumes.PesoBruto` / `PesoLiquido` | 0 se ausente. |
| qtdeVolume | Soma das quantidades (caixas) arredondada ao inteiro | Mesma regra da I60 (`ConstrutorI60.CalcularQtdeVolume`); inteiro no WMS. |
| valorFrete, valorSeguro, valorDespesaAcessoria, valorDesconto | 0 | Parser+ se o WMS exigir (`vFrete`, `vSeg`, `vOutro`, `vDesc`). |
| valorTotal | `ValorTotal` (`vNF`) | |
| formaTransporte | Fixo `PR` (como na I60) | PR/CP/CD; confirmar (P5). |
| baseICMS, valorIPI, valorICMS | 0 | Parser+ se necessário (`vBC`, `vIPI`, `vICMS` do total). |
| cnpjProprietariaProduto | `CnpjDepositante` (config) | |
| deposito / cnpjArmazem | `Deposito` / `CnpjArmazem` (config) | Ao menos um; enviamos os dois, como na I60. |
| numeroCertificadoSif, numeroPedidoCliente, nomeMotorista, numeroDoca, numeroRefPedCliente, placaVeiculoPrevistoNota | Branco | `PlacaVeiculo` já é lida do XML; pode ir em `placaVeiculoPrevistoNota` (decisão P6). |
| tipoOperacaoNota | Config (IMC/ICD/IMP) | Valor a confirmar no manual da I40 (P3). Seguir o padrão da I60: opção configurável, sem release. |
| atributo1-3 (200) | `CH:<chave> PROT:<protocolo>`, `NF:<n> S:<serie>`, resumo | Reaproveitar `FormatoWms.Truncar`; mesmo padrão da I50. |

### Itens (ITEM_NOTA_FISCAL, 1-990)

| Campo I40 | Origem | Observação |
|---|---|---|
| numeroSequencialItem | Índice + 1 | |
| codProduto | `CodigoProduto` (`cProd`) | |
| qtdeProduto / qtdeProdutoConfirmada / qtdeEmbalagem | `Quantidade` (`qCom`) | A nota já está em caixas (decisão do Plano A). |
| valorOriginalItem / valorAtualItem | `ValorUnitario` (`vUnCom`) | Como na I60. |
| aliquotaICMS, aliquotaIPI | 0 | Parser+ se necessário. |
| codSituacaoTributaria | Fixo `040` (como na I60) | A confirmar. |
| numeroLote | `Lote` | |
| valorDetalhe1-4 | Branco | |
| dataFabricacao | Branco | `dFab` não é lido hoje (Parser+). |
| dataValidade | `Validade` -> `FormatoWms.Data` | |
| ean | `Ean` (`cEAN`) | |
| descricao | `Descricao` (`xProd`) | |
| ncm | `Ncm` | |
| codunidade | `Unidade` (`uCom`) | |

Itens acima de 990 por nota e envios acima de 30 notas devem ser rejeitados na validação local
com mensagem clara (a tela divide em lotes de 30).

## 4. Componentes

| Componente | Camada | Função |
|---|---|---|
| `PayloadI40` / `ConstrutorI40` / `IConstrutorI40` | Core, `Construtores` | DTOs com nomes JSON exatos e construtor, no padrão de `ConstrutorI60`. |
| `InterfaceWms.I40` | Core, `ClienteWms` | Novo valor no enum e em `ParaIdentificador` (`WEBAPI-I40-Json`; confirmar o sufixo exato no manual da I40). |
| `LocalizadorXml` | Core, `Devolucao` | Busca `NFE<chave>.XML` em `Processados\pacote_*`. Somente leitura. |
| `ServicoDevolucao` | Core, `Devolucao` | Orquestra: localizar, ler, construir, enviar, classificar. Reusa `IClienteWms` e `ClassificadorRetorno`. |
| Endpoints HTTP + página estática | Worker | `GET /` (tela), `POST /api/devolucao/enviar`, `GET /api/config`, `GET /api/saude` (nomes finais na seção 15). |
| Relatório da devolução | Core | Reaproveita o gerador de relatório (HTML/PDF) e o e-mail de fechamento, com um bloco/lote próprio "Devolução" (opcional, fase 2). |

### Onde hospedar: recomendação

**Recomendo endpoint HTTP dentro do próprio Worker (Kestrel embutido)**, em vez de um processo
separado.

Justificativa:

- **Um serviço NSSM só** (`Friozem-ImportacaoNotas`) e um único fluxo de release
  (`publicar-release.ps1`, `rollback.ps1`). Um segundo serviço exigiria instalar, atualizar,
  monitorar e dar rollback em dois.
- **Reuso direto** de DI, configuração, `IClienteWms` (cache do token JWT com lock), pastas
  (`OpcoesPasta`) e relatório. Em processo separado haveria dois clientes pedindo token com a
  mesma conta WMS, o que já deu 401 intermitente antes.
- **Windows Server 2012 R2**: o .NET 8 roda em 2012 R2 (o projeto já roda assim,
  self-contained). Kestrel não depende do IIS nem do HTTP.sys, então não exige
  recurso do Windows nem `netsh http add urlacl`. O que importa no 2012 R2 é o TLS 1.2 já
  tratado; a tela usa HTTP simples na rede interna, sem certificado.
- **NSSM** apenas supervisiona o executável; uma porta a mais não muda nada. Reinício e logs
  (`worker.out.log`) continuam iguais.

Ponto de atenção e mitigação: o Worker passa a usar `Microsoft.AspNetCore.App` (troca do SDK
`Microsoft.NET.Sdk.Worker` para `Microsoft.NET.Sdk.Web` ou `FrameworkReference`), o que aumenta
o pacote self-contained. Falha no endpoint não pode derrubar o ciclo de pacotes: tratar
exceções por requisição e manter o Kestrel opcional por flag (`Devolucao:Habilitada`,
default `false`, para publicar o código sem expor a porta).

Alternativa descartada: processo separado (outro serviço NSSM). Só vale se a equipe quiser
ciclo de release independente da integração; hoje a carga operacional é maior que o ganho.

### Configuração prevista

```json
"Devolucao": {
  "Habilitada": false,
  "Porta": 8085,
  "TipoOperacaoNota": "IMC",
  "MaxChavesPorEnvio": 30
}
```

Previsão do desenho inicial. Os valores finais (`TipoOperacaoNota` = IMP, `TipoOperacaoNotaComPlaca`
= ICD, `EnviarPlacaPadrao` = false, `Porta` = 8085) estão na seção 14. Override por variável de
ambiente (`Devolucao__Habilitada=true`), no mesmo padrão da I60.

## 5. Reuso

- `IClienteWms` envia qualquer payload; basta o novo `InterfaceWms.I40`.
- `LeitorNotaFiscalXml`, `NotaFiscal`, `FormatoWms`, `OpcoesOperacionais`, `ClassificadorRetorno`.
- Resolução de pastas por `OpcoesPasta.CaminhoProcessados`.
- Relatório/e-mail de fechamento (fase 2), telemetria e logs do Worker.

## 6. Riscos

| Risco | Tratamento proposto |
|---|---|
| XML fora da retenção (mais de 30 dias) | Mensagem "XML não encontrado (retenção de 30 dias)". Fallback: orientar importação manual no WMS. |
| Chave inexistente ou digitada errada | Validar 44 dígitos e dígito verificador (módulo 11) na tela, antes de procurar. |
| Nota já devolvida / envio duplicado | Sem guardar estado (decisão do usuário), a proteção fica no WMS (rejeita duplicidade, a validar no homolog). Mitigação local leve: avisar se a mesma chave aparece duas vezes no mesmo envio. Se for preciso histórico, exige guardar estado (decisão a tomar, P7). |
| Nota original nunca integrada (está em `Erro\`) | Não localizar em `Erro`; informar "nota não consta como integrada". |
| Porta/firewall | Regra de entrada restrita à rede interna na porta escolhida; sem exposição externa. Sem autenticação, qualquer máquina da LAN com acesso à porta pode enviar. Decisão do usuário (13/11/2026): link aberto na rede interna, sem restrição de firewall por origem; risco aceito (ver seção 20). |
| Remetente/destinatário na devolução | Pergunta P1: na devolução o cliente devolve para nós. Pode ser preciso inverter emitente/destinatário (remetente = cliente, destinatário = nós). Não decidir sem o manual da I40. |
| Conta WMS compartilhada | Mesmo usuário do serviço; token único com lock, sem concorrência extra. |
| Falha do endpoint afetar a integração | Isolamento por flag e tratamento de exceção (seção 4). |

## 7. Perguntas em aberto

- P1. Na devolução (`tipoNota = DE`), inverter emitente/remetente e destinatário/local de entrega? O XML da venda tem emitente = nós, destinatário = cliente.
- P2. `codNatOperacaoFiscal`: CFOP da venda ou o de devolução correspondente?
- P3. Valor de `tipoOperacaoNota` da I40 (IMC, ICD ou IMP) para esta rotina.
- P4. Porta HTTP a usar e política de firewall (sub-rede liberada).
- P5. `formaTransporte` e `codSituacaoTributaria` padrão para a I40.
- P6. Enviar a placa em `placaVeiculoPrevistoNota`?
- P7. Evitar devolução em duplicidade exige guardar histórico; ou o WMS já bloqueia?
- P8. Nome exato do identificador (`WEBAPI-I40-Json`?) e se a I40 aceita lote de 30 notas por chamada na prática.
- P9. A nota original precisa estar associada ao DocCarga (I60 do Plano A) para a devolução funcionar? Se sim, notas anteriores ao Plano A não servem.

## 8. Estimativa (somente horas)

Referência já comunicada: 8 h humanas e 6 h de IA. Refinada após ler o código:

| Etapa | Horas humanas | Horas de IA |
|---|---:|---:|
| Respostas às perguntas P1 a P9 e acesso ao manual da I40 | 1,5 | 0,0 |
| `ConstrutorI40`, DTOs, `InterfaceWms.I40`, testes do construtor | 0,5 | 1,5 |
| Parser+ (campos fiscais que faltam, se exigidos) e testes | 0,5 | 0,5 |
| `LocalizadorXml` e `ServicoDevolucao` com testes | 0,5 | 1,5 |
| Endpoint e página (digitar/bipar, resultado por chave) | 1,0 | 1,5 |
| Publicação, firewall, teste no homolog e ajustes | 3,0 | 0,5 |
| Documentação e manual do colaborador | 1,0 | 0,5 |
| **Total** | **8,0** | **6,5** |

O valor de IA sobe 0,5 h sobre a referência por causa do Parser+ e dos testes do localizador;
as horas humanas dependem sobretudo das respostas do WMS e do teste ao vivo.

## 9. Perguntas respondidas (decisões do usuário e consequências)

| Pergunta | Decisão | Consequência no código/config |
|---|---|---|
| P1. Inverter emitente/destinatário? | Testar as DUAS variantes (invertido e não invertido) no homolog; a resposta do WMS decide o default. "Testar antes de montar." | `Devolucao:InverterPartes` (default atual `false`). Roteiro em `docs/ROTEIRO-TESTE-I40.md`. O default só muda após o teste ao vivo. |
| P2. CFOP | CFOP da venda, sem conversão. | `codNatOperacaoFiscal` = `Itens[0].Cfop` da nota. |
| P3. `tipoOperacaoNota` | `ICD` (gera chegada de veículo e descarga). | `Devolucao:TipoOperacaoNota`, default `ICD`. |
| P6. Placa e motorista | Placa obrigatória (a tela deve pedir) e nome de motorista FIXO, cadastrado no WMS. | Placa vai em `placaVeiculoPrevistoNota` (maiúscula, obrigatória). `Devolucao:NomeMotorista` configurável e obrigatório; o construtor falha com mensagem clara se vazio. |
| P9. Dependência da I60 | A devolução NÃO depende da I60 (Plano A). | Notas anteriores ao Plano A também servem. |

Continuam em aberto: P4 (porta e firewall, fase 2), P5 (`formaTransporte`/`codSituacaoTributaria`
usam PR e 040 até confirmação), P7 (duplicidade; o WMS decide, a validar no homolog) e P8 (lote
de 30 notas por chamada).

## 10. Pontos a confirmar no teste ao vivo

- **Nome da interface:** `InterfaceWms.I40` usa o identificador `WEBAPI-I40-Json`. O nome
  é **a confirmar** no teste; se o WMS recusar, corrigir em `ParaIdentificador`.
- **`ITEM_NOTA_FISCAL` como ARRAY:** o layout diz "looping 1-990", mas o exemplo do manual
  mostra um objeto. Foi implementado como array. Se o WMS recusar, o teste ao vivo dirá e o
  serializador será ajustado.

## 11. O que a fase 1 entregou

Implementado e com testes unitários (commits locais `feat(i40)`):

- `OpcoesDevolucao` (seção `Devolucao`: `Habilitada`, `TipoOperacaoNota`, `TipoNota`,
  `NomeMotorista`, `InverterPartes`, `FormaTransporte`, `CodSituacaoTributaria`, `Porta`) com
  override por ambiente (`Devolucao__...`).
- `InterfaceWms.I40` (`WEBAPI-I40-Json`, nome a confirmar).
- `PayloadI40` e `ConstrutorI40`/`IConstrutorI40`: `tipoNota=DE`, `tipoOperacaoNota=ICD`,
  placa e motorista obrigatórios, `InverterPartes`, CFOP da venda.
- `LocalizadorXmlDevolucao`: acha `NFE<chave>.XML` em `Processados\pacote_*` (o mais recente) e
  valida a chave (44 dígitos e dígito verificador).
- Modo manual de teste no Worker: `--enviar-i40 --chave <44 dígitos> --placa <placa> [--xml <caminho>]`
  (`EnvioManualI40`). Envia SOMENTE a I40 e grava JSON e resposta em
  `bin\Release\net8.0\envio-manual-i40\`.

Não faz parte da fase 1: tela web, porta HTTP, publicação como serviço e relatório de devolução.

## 12. Estimativa da fase 2 (tela web), em horas

**Estimativa, não registro de esforço.** Escopo: tela de chaves (digitar/bipar), placa
obrigatória, validações, porta HTTP embutida no Worker, publicação como serviço no servidor,
testes e manuais. Sem autenticação real (decisão do usuário; login simples só para registrar o usuário, seção 20).

| Etapa | Horas humanas | Horas de IA |
|---|---:|---:|
| Kestrel embutido no Worker (`Microsoft.AspNetCore.App`), porta e flag `Devolucao:Habilitada`, isolamento de falhas | 0,5 | 0,7 |
| `ServicoDevolucao`: lote de até 30 chaves, duplicadas, dígito verificador, resultado por chave, testes | 0,5 | 1,5 |
| Endpoints (`GET /`, `POST /api/devolucao`, `GET /api/saude`) e página (campo de chaves com Enter do leitor, placa obrigatória, resultado por chave) | 1,0 | 1,5 |
| Publicação no servidor (serviço NSSM, regra de firewall na porta, release e rollback) | 1,5 | 0,5 |
| Testes no homolog e no servidor, ajustes após o retorno do WMS | 2,0 | 0,5 |
| Manuais do colaborador e atualização da documentação/deck | 0,5 | 0,8 |
| **Total estimado** | **6,0** | **5,5** |

Justificativa: o grosso do código de negócio (construtor, localizador, envio) já existe da
fase 1, então a IA concentra esforço em serviço, endpoints e página. As horas humanas dependem
sobretudo de publicação no servidor, firewall e testes ao vivo, que não são automatizáveis.
O valor pode subir se o WMS exigir ajustes de layout (ex.: itens como objeto) ou se o Parser+
(campos fiscais adicionais) for necessário.

## 13. Resultados reais do teste no homolog (10/11/2026)

Nota 15671241, chave `35251201838723009850550060156712411843547443`, enviada pelo modo manual
`--enviar-i40` ao WMS de **homologação**. Nada foi enviado à produção.

| # | Hora | Tipo de operação | Placa | Resultado do WMS |
|---|---|---|---|---|
| 1 | 15:01 | ICD | ABC1D23 | Recusou a placa (não cadastrada). O nome da interface `WEBAPI-I40-Json` e o `ITEM_NOTA_FISCAL` como **array** foram **aceitos**. |
| 2 | 15:04 | ICD | EPR7871 (cadastrada), motorista `DANIEL SILVA`, transportadora 03.639.682/0001-75, doca vazia | "Erro na criacao do controlador de veiculo". |
| 3 | 15:08 | IMP, `InverterPartes=false`, placa EPR7871 | EPR7871 | **"Registrado com sucesso"** (transação 4042207). O usuário fez o **recebimento no WMS do início ao fim**: o formato está correto. |

Conclusões:

- Os pontos em aberto da seção 10 estão resolvidos: nome da interface e array de itens estão certos.
- A variante com partes invertidas **não foi necessária**.
- O ICD ainda não funciona no homolog. A causa provável é cadastro (transportadora, motorista ou
  doca), não o formato da mensagem. Por isso a placa começa **desligada** na tela.

## 14. Decisões A a D (fechadas pelo usuário)

| Id | Decisão | Onde está no código/config |
|---|---|---|
| A | `InverterPartes` padrão **false**. A variante invertida não é necessária; a opção continua existindo e está documentada como validada. | `Devolucao:InverterPartes` |
| B | `tipoOperacaoNota` padrão passa de ICD para **IMP**. | `Devolucao:TipoOperacaoNota` = `IMP` |
| C | **Substituída pela decisão F (seção 19): a placa foi removida.** Texto original: a **tela tinha um flag** "Enviar placa do veículo", que começa **desligado**. Placa desligada: `tipoOperacaoNota` = `TipoOperacaoNota` (IMP), `placaVeiculoPrevistoNota` e `nomeMotorista` em branco, sem exigir placa nem motorista. Placa ligada: `tipoOperacaoNota` = `TipoOperacaoNotaComPlaca` (ICD), placa obrigatória e `nomeMotorista` = `Devolucao:NomeMotorista` (obrigatório configurar). | chaves `EnviarPlacaPadrao`, `TipoOperacaoNotaComPlaca` e `NomeMotorista` (removidas do código) |
| D | CFOP da venda, `tipoNota` DE, sem autenticação na tela (só login declarado, seção 20). Retenção de 30 dias (`Limpeza:DiasRetencao`) já existente. | `ConstrutorI40`; `Devolucao:TipoNota` = `DE` |

## 15. Estado da fase 2 (tela web no Worker)

Implementada, com testes automatizados e **desligada** por padrão. Não foi enviado nada ao WMS
pela tela e o servidor SRVAPPALC02 não foi acessado.

### Componentes (nomes reais do código)

| Componente | Projeto | Função |
|---|---|---|
| `OpcoesDevolucao` | Core, `Configuracao` | Seção `Devolucao` (ver tabela de chaves abaixo). |
| `ConstrutorI40.Construir(nota)` | Core, `Construtores` | Monta a I40 com `tipoOperacaoNota` = `Devolucao:TipoOperacaoNota` (IMP); `placaVeiculoPrevistoNota` e `nomeMotorista` sempre em branco (decisão F). |
| `ValidadorChavesDevolucao` | Core, `Devolucao` | Normaliza (espaços e quebras), remove duplicadas mantendo a ordem, valida 44 dígitos e dígito verificador (módulo 11), extrai CNPJ do emitente, série e número da chave e valida os campos número/série da busca. |
| `ServicoDevolucao` / `IServicoDevolucao` | Core, `Devolucao` | Valida o pedido, localiza o XML de cada chave, monta a I40, envia **uma requisição** com todas as notas válidas (até 30) pelo `IClienteWms` existente e mapeia a resposta por documento (número da nota). Chave inválida ou sem XML não bloqueia as demais. `Buscar(numero, serie)` resolve número + série em chave(s) (decisão E). |
| `LocalizadorXmlDevolucao` | Core, `Devolucao` | `Localizar(chave)` acha o XML mais recente da chave; `BuscarPorNumeroSerie` acha as chaves de uma nota lendo só os nomes dos arquivos. |
| `AuditoriaDevolucao` | Core, `Devolucao` | Grava o arquivo de auditoria (abaixo). |
| `TelaDevolucaoApp` | Worker, `TelaDevolucao` | Monta o Kestrel com os endpoints. |
| `ServidorTelaDevolucao` | Worker, `TelaDevolucao` | `BackgroundService` que liga a tela só se `Devolucao:Habilitada=true`. |
| `PaginaDevolucao.html` + logo | Worker, `TelaDevolucao` | Página única embutida no executável (recurso embutido), sem CDN nem arquivo solto. |

### Chaves de configuração (seção `Devolucao`)

| Chave | Padrão | Uso |
|---|---|---|
| `Habilitada` | `false` | Liga a tela. Com `false` a porta não abre. |
| `Porta` | `8085` | Porta HTTP da tela. |
| `TipoOperacaoNota` | `IMP` | Tipo de operação da I40 (validado no homolog). |
| `MaxChavesPorEnvio` | `30` | Limite de chaves por envio. |
| `PastaAuditoria` | vazio | Vazio = `<Estado>\devolucao`. |
| `TipoNota`, `InverterPartes`, `FormaTransporte`, `CodSituacaoTributaria` | `DE`, `false`, `PR`, `040` | Como na fase 1. |

Override por variável de ambiente: `Devolucao__Habilitada`, `Devolucao__Porta`,
`Devolucao__TipoOperacaoNota`, `Devolucao__InverterPartes` etc.

### Endpoints

| Método e caminho | Resposta |
|---|---|
| `GET /` | A página HTML única. |
| `GET /api/config` | `{ "maxChaves": 30 }` |
| `GET /api/devolucao/buscar?numero=&serie=` | Resolve número + série em chave(s): 200 com `numero`, `serie` e `opcoes[]` (`chave`, `numero`, `serie`, `dataArquivo`, `pacote`); 404 "Nota não encontrada na retenção de 30 dias."; 400 para entrada inválida; 503 sem `Pastas:Entrada` ou CNPJ do depositante. Não envia nada ao WMS. |
| `GET /api/saude` | `{ "status": "ok" }` |
| `POST /api/devolucao/enviar` | Corpo `{ "chaves": [...] }`. Resposta 200 com `total`, `enviadas`, `transacao` e `resultados[]` (`chave`, `nota`, `serie`, `status`, `sucesso`, `mensagem`). |

`status`: `Enviada`, `ErroWms` (texto do WMS sem alteração), `XmlNaoEncontrado` ("XML não encontrado
na retenção de 30 dias."), `ChaveInvalida` e `ErroInterno`. Erros do pedido: **400** (corpo
inválido, lista vazia, mais chaves que o máximo), **503**
(`Pastas:Entrada` não configurado) e **500** genérico, sem detalhe
interno, para falha inesperada (o detalhe vai para o log). O corpo aceito tem no máximo 64 KB. A
resposta traz cabeçalhos de segurança (`Cache-Control: no-store`, política de conteúdo restrita
ao próprio endereço).

### Auditoria

Um arquivo por dia, `devolucao_yyyyMMdd.log`, em `Devolucao:PastaAuditoria` ou, se vazio, em
`<Estado>\devolucao` (no servidor, a pasta de estado do serviço). Uma linha por chave, separada
por `;`: `data;ip;usuario;chave;nota;status;transacao;mensagem`. O `usuario` é o login do WMS
digitado na tela (sem verificação; `;` e quebras de linha são trocados). Linhas antigas, de antes
do login (13/11/2026), têm 7 campos, sem a coluna `usuario`; nada no sistema lê esse arquivo, ele é
só para consulta. Não grava senha, credenciais nem o JSON enviado. Falha ao gravar só gera aviso no log. Cada chave também é registrada no log do Worker.

### Isolamento

A tela roda num host Kestrel próprio, criado dentro do `ServidorTelaDevolucao`. Qualquer falha
(porta ocupada, opção inválida, exceção) é registrada e encerra só a tela; o ciclo de pacotes
continua. Com `Habilitada=false` o servidor nem é criado.

## 16. Verificação de viabilidade: Windows Server 2012 R2 e NSSM

Verificação documental e de projeto (o servidor **não foi acessado**).

- **Hospedagem:** o Worker continua com o SDK `Microsoft.NET.Sdk.Worker` e ganhou
  `FrameworkReference Microsoft.AspNetCore.App`. Isso usa o Kestrel que já vem no SDK/shared
  framework; **nenhum pacote NuGet novo** foi adicionado. O publish self-contained
  (`win-x64`, como em `deploy\publicar.ps1`) leva o ASP.NET Core junto.
- **.NET 8 no 2012 R2:** o .NET 8 consta entre os sistemas suportados e o Worker atual já roda
  assim no servidor (`docs/AMBIENTE-SERVIDOR.md`). O Kestrel usa sockets, **sem IIS, sem HTTP.sys
  e sem `netsh http add urlacl`**, então não pede recurso do Windows.
- **NSSM:** só supervisiona o executável; uma porta a mais não muda nada. Logs e reinício
  continuam iguais.
- **Impedimento técnico concreto:** nenhum encontrado.
- **Riscos residuais (não bloqueiam):**
  - R1. O 2012 R2 está em atualizações estendidas (ESU). Não foi testado no servidor; o primeiro
    teste real da tela lá fica para o usuário.
  - R2. O pacote self-contained cresce com o ASP.NET Core. O tamanho não foi medido neste registro.
  - R3. O `ServicoDevolucao` e o orquestrador têm, cada um, a sua instância do cliente WMS, ou seja,
    dois tokens JWT da mesma conta. Aceito (o pedido do token já tem nova tentativa). Se
    aparecer 401 intermitente, compartilhar a instância numa mudança separada.
  - R4. Porta aberta **sem autenticação** na rede interna. Mitigação: flag desligada por padrão e
    auditoria com o IP de origem e o usuário declarado (ver seção 20 e `docs/INSTALACAO-SERVIDOR.md`).

## 17. Testes

Última execução local (após a seção 19): 487 aprovados, 0 falhas, 9 ignorados (stress); build com 0 avisos
e 0 erros. Cobrem o construtor sem placa, validação de chave, extração de número/série/CNPJ da chave,
busca por número + série (uma, várias, nenhuma, outro CNPJ, zeros à esquerda, pasta Erro, mais recente),
endpoint de busca, montagem do lote, mapeamento da resposta por documento (com
cliente WMS falso) e os endpoints. O envio real ao WMS pela tela **não foi testado**.

## 18. O que falta para ligar em produção

1. (Removido pela decisão F: a placa não existe mais; o cadastro do ICD só volta a ser assunto se a
   chegada de veículo for pedida de novo.)
2. Testar a tela no homolog nesta máquina (`docs/ROTEIRO-TESTE-I40.md`).
3. Publicar a versão e testar no servidor (2012 R2) com `Habilitada` ainda `false`.
4. Criar a regra de firewall restrita à rede interna e só então ligar `Devolucao__Habilitada=true`
   (procedimento em `docs/INSTALACAO-SERVIDOR.md`).
5. Decisão em aberto: evitar devolução em duplicidade (P7). O WMS é quem decide; não há histórico local.

## 19. Decisões E e F (fechadas pelo usuário em 10/11/2026)

### E. Pesquisar por chave OU por número da nota + série

A tela continua aceitando a chave de 44 dígitos (digitada ou bipada, com Enter) e ganhou os campos
**Número da nota** (até 9 dígitos) e **Série** (até 3 dígitos), com o botão "Buscar e adicionar".
A lista final continua sendo de **chaves** (até 30 por envio, duplicadas removidas) e mostra o
número e a série de cada uma.

Como a busca funciona, **sem abrir nem ler os XMLs**: o nome do arquivo é `NFE<chave>.XML` e a chave
traz tudo. Layout da chave (44 dígitos, posições a partir de 1): cUF 1-2, AAMM 3-6, CNPJ do emitente
7-20, modelo 21-22, série 23-25, número 26-34, tpEmis 35, cNF 36-43, DV 44.

- Pastas: as mesmas do localizador atual (`Processados` e `Processados\pacote_*`, nunca `Erro`), na
  mesma retenção de 30 dias.
- Filtro: só entram arquivos cujo nome é uma chave válida (44 dígitos e DV) e cujo CNPJ do emitente
  é o do depositante (`Operacional:CnpjDepositante`, BRF 01.838.723/0098-50; compara só os dígitos).
- Número e série são comparados como **inteiros** (zeros à esquerda não contam: `006` é igual a `6`).
- A mesma chave em mais de um pacote vale uma vez, pela cópia mais recente.
- **Uma** chave encontrada: entra na lista. **Mais de uma chave distinta** para o mesmo número e série:
  a tela mostra as opções (chave, data do arquivo, pacote) e o colaborador escolhe; **nada é enviado
  automaticamente**. **Nenhuma**: "Nota não encontrada na retenção de 30 dias."
- Endpoint: `GET /api/devolucao/buscar?numero=&serie=` (tabela da seção 15). O servidor valida as
  entradas (só dígitos; número com no máximo 9 e série com no máximo 3).
- Modo manual: `--enviar-i40 --chave <44 dígitos>` ou `--enviar-i40 --numero <n> --serie <s>`, mais
  `--xml <caminho>` opcional. Se número + série acharem mais de uma chave, o modo manual lista as
  chaves e não envia.
- Acessibilidade: campos com `label`, foco visível, uso por teclado (Enter nos campos de número e
  série), mensagens em regiões `aria-live`/`role="alert"`.

### F. Placa removida da tela e do código

A placa saiu por completo. Foram removidos o checkbox "Enviar placa", o campo placa, a regra do ICD
com placa, `OpcoesDevolucao.EnviarPlacaPadrao`, `TipoOperacaoNotaComPlaca`, `NomeMotorista` e toda
validação de placa e de motorista. O `ConstrutorI40` não recebe mais placa: `placaVeiculoPrevistoNota`
e `nomeMotorista` vão **sempre em branco**. `tipoOperacaoNota` continua configurável em
`Devolucao:TipoOperacaoNota`, padrão `IMP`. A auditoria perdeu a coluna da placa.

Nota histórica: o ICD com placa **foi testado e removido**. No homolog (seção 13) a placa `ABC1D23`
foi recusada por não estar cadastrada e, com a `EPR7871` cadastrada, o ICD deu "Erro na criacao do
controlador de veiculo". O IMP sem placa deu "Registrado com sucesso" e o recebimento foi do início
ao fim, por isso o IMP sem placa é o caminho adotado. O ICD só volta a ser assunto se a criação da
chegada de veículo for pedida de novo.

Compatibilidade: chaves antigas `EnviarPlacaPadrao`, `TipoOperacaoNotaComPlaca` e `NomeMotorista`
que ainda existam em algum `appsettings.Production.json` do servidor são ignoradas, sem erro. O corpo
do `POST /api/devolucao/enviar` com `enviarPlaca`/`placa` também é aceito e ignorado.
## 20. Login simples na tela (decis�o do usu�rio em 13/11/2026)

- **Decis�o:** o link da tela fica aberto na rede interna, sem restri��o de firewall por origem nem
  integra��o com AD, porque o uso � baixo. **Risco aceito:** qualquer pessoa com o link pode enviar,
  e a identifica��o � autodeclarada (n�o � autentica��o).
- **Objetivo:** saber quem enviou cada devolu��o. A tela tem o cart�o "Quem est� enviando" com
  "Usu�rio do WMS" e "Senha". A busca funciona sem usu�rio; o bot�o "Enviar devolu��o" s� libera com
  o usu�rio confirmado. A tela mostra "Enviando como: <usu�rio>" e o link "Trocar usu�rio". S� o
  usu�rio fica no navegador (`sessionStorage`, some ao fechar a aba).
- **Senha:** a p�gina n�o envia a senha e o servidor ignora qualquer `senha` que chegue no corpo
  (a propriedade nem � lida). Ela n�o � verificada, guardada, logada, auditada, devolvida nem
  enviada ao WMS. O WMS continua sendo chamado com a conta de servi�o (`WMS__Usuario`/`WMS__Senha`).
- **API:** `POST /api/devolucao/enviar` exige `usuario` (cortado nas pontas, 1 a 60 caracteres, s�
  letras, n�meros, `.` `_` `-` `@` e espa�o); sen�o responde 400 em pt-BR.
- **Registro:** coluna `usuario` na auditoria e `usuario=` na linha de log de cada chave.
