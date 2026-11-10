# Arquitetura do Plano B: devolução pela I40 (somente desenho, nada implementado)

Data: 2026-11-09. Status: proposta para aprovação. Nenhum código foi escrito para o Plano B.

## 1. Objetivo

O colaborador da devolução hoje precisa procurar, entre muitos XMLs, as notas do processo.
A proposta é uma tela web simples, hospedada pelo próprio serviço no servidor, onde ele digita
ou bipa (leitor de código de barras) as chaves de NF-e. A rotina:

1. localiza o XML da **nossa venda original** pela chave;
2. converte o XML para a I40 (`WEBAPI-I40`, função `EDI_JS_IMP_I40`) com `tipoNota = "DE"` (devolução);
3. envia ao WMS e mostra o resultado por chave.

Decisões do usuário: sem login (a tela só lê XML e envia, não guarda nada e não altera dados
locais); retenção de 60 dias (já aplicada em `Limpeza:DiasRetencao`); XML convertido é o da
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
| Endpoints HTTP + página estática | Worker | `GET /` (tela), `POST /api/devolucao`, `GET /api/saude`. |
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

Porta e `TipoOperacaoNota` são valores a definir (P3, P4). Override por variável de ambiente
(`Devolucao__Habilitada=true`), no mesmo padrão da I60.

## 5. Reuso

- `IClienteWms` envia qualquer payload; basta o novo `InterfaceWms.I40`.
- `LeitorNotaFiscalXml`, `NotaFiscal`, `FormatoWms`, `OpcoesOperacionais`, `ClassificadorRetorno`.
- Resolução de pastas por `OpcoesPasta.CaminhoProcessados`.
- Relatório/e-mail de fechamento (fase 2), telemetria e logs do Worker.

## 6. Riscos

| Risco | Tratamento proposto |
|---|---|
| XML fora da retenção (mais de 60 dias) | Mensagem "XML não encontrado (retenção de 60 dias)". Fallback: orientar importação manual no WMS. |
| Chave inexistente ou digitada errada | Validar 44 dígitos e dígito verificador (módulo 11) na tela, antes de procurar. |
| Nota já devolvida / envio duplicado | Sem guardar estado (decisão do usuário), a proteção fica no WMS (rejeita duplicidade, a validar no homolog). Mitigação local leve: avisar se a mesma chave aparece duas vezes no mesmo envio. Se for preciso histórico, exige guardar estado (decisão a tomar, P7). |
| Nota original nunca integrada (está em `Erro\`) | Não localizar em `Erro`; informar "nota não consta como integrada". |
| Porta/firewall | Regra de entrada restrita à rede interna na porta escolhida; sem exposição externa. Sem login, qualquer máquina da LAN com acesso à porta pode enviar: aceito pelo usuário, mas restringir por origem no firewall. |
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
