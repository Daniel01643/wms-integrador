# Templates das interfaces WMS (I10, I50, I60) — contrato de payload

Fonte de verdade para os **nomes exatos de propriedade**, aninhamento e valores fixos
dos payloads JSON. Baseado no manual oficial (I10/I50/I60 WebAPI REST, Assist/SolWMS) e
nos templates comentados do cliente Unidasul. Os comentários `-->` indicam a regra de
preenchimento. Para a BRF/Friozem, os valores operacionais (CNPJ proprietário/armazém,
depósito) vêm da configuração — ver `AMBIENTE-SERVIDOR.md` e a seção de config do projeto.

> Atenção a maiúsculas/minúsculas: os nomes são sensíveis. Ex.: `NroEmb`, `Tpsep`,
> `Tpsit`, `Tpnota`, `DhprevEmbarque`, `NOTAS_VENDA`, `PEDIDO`, `EMPRESA`,
> `ITEM_PEDIDO`, `ITEM_NOTA_VENDA`.

## Identificadores (query `IDENTIFICADOR`)

- `WEBAPI-I10-Json` → cadastro de empresa (`EMPRESA`)
- `WEBAPI-I30-Json` → cadastro de produto (`PRODUTO`) — contenção (caso produto não cadastrado)
- `WEBAPI-I50-Json` → pedido/DocCarga (`PEDIDO`)
- `WEBAPI-I60-Json` → nota de venda (`NOTAS_VENDA`)

> **Envelope header/body**: o template do I30 (Unidasul) usa o envelope
> `{ "header": {...}, "body": {...} }`, com os 5 parâmetros (identificador,
> requisitante, cnpjrequisitante, destino, cnpjdestino) dentro de `header` e o payload
> em `body`. Nos testes de I50/I60 passamos os 5 parâmetros na **query string** e
> funcionou. **A CONFIRMAR** se a API aceita as duas formas para todas as interfaces ou
> se o envelope é preferível — ver `TESTE-COMUNICACAO.md`.

### I30 de contenção: cadastro PROVISÓRIO + revisão do colaborador (regra)

O I30 é montado só com os dados disponíveis no XML da NF-e (código, descrição, EAN,
NCM, unidade), então o produto criado é **provisório/incompleto** — faltam atributos
mestres (curva ABC, dimensões, temperatura, shelf-life, picking) que o XML não traz e
que a operação exige. Decisão do cliente:

- Com a flag ligada (padrão em **homolog**), ao detectar produto não cadastrado o agente
  envia o I30 automaticamente **para desbloquear a integração da nota** (a I50 passa a
  ser aceita).
- O cadastro gerado pelo I30 **NÃO é considerado completo**: ele **entra em revisão pelo
  colaborador**, porque vai gerar erro em outros pontos (separação/endereçamento). O
  agente deve **registrar uma PENDÊNCIA de revisão** (relatório + alerta) para cada
  produto criado por contenção, não tratar como sucesso silencioso.
- Em produção a flag pode ser desligada (produto não cadastrado vira só pendência, sem
  auto-criar) — decisão operacional futura.

Portanto o resultado de uma contenção I30 é: **nota desbloqueada E pendência de revisão
registrada**. Os dois ao mesmo tempo.

---

## I50 — PEDIDO (DocCarga)

Template comentado (base Unidasul; substituir valores operacionais pelos da BRF/config).
Na BRF, `tipoCarga`, `tipoSeparacao` e `tipoSeparacao2` vêm de **configuração**.

```jsonc
{
  "PEDIDO": [
    {
      "doca": "",                                      // Mandar em branco
      "cnpjProprietario": "00.000.000/0001-00",        // CNPJ do proprietário/depositante (config: 01.838.723/0098-50)
      "cnpjDestinatario": "11.111.111/0001-11",        // CNPJ do destinatário da entrega (dest/CNPJ do XML)
      "cnpjTransportadora": "",                        // Mandar em branco
      "numeroPedido": "1001",                          // Nº do pedido/remessa; não pode mudar após enviado
      "tipoCarga": "CN",                               // CONFIG. "CD" distribuição / "CN" normal / "TR" transferência
      "tipoAcondicionamento": "PP",                    // "PP" palete padrão / "PE" / "PC" picking / "PA" packing
      "tpAcao": "I",                                   // Sempre "I"
      "tipoSeparacao": "RES;FEF;PLI",                  // CONFIG (busca 1a carga)
      "tipoSeparacao2": "AGL;FEF;PLL",                 // CONFIG (busca 2a carga)
      "dataEmissao": "05/12/2022 00:00:00",            // Data geração da remessa/pedido (DD/MM/YYYY HH:MM:SS)
      "dataprev": "06/12/2022 18:30:00",               // Previsão de entrega
      "numeroPlanejamento": "000001",                  // Carga/embarque/carro (a definir)
      "numeroEmbarque": "",                            // A definir
      "nroagrupseparacao": "000001",                   // AGRUPADOR DA ONDA = Nº do transporte (string, zeros à esquerda)
      "placaVeiculo": "",                              // Branco
      "deposito": "RS",                                // CONFIG operacional (BRF: JD)
      "cnpjArmazem": "14.605.065/0016-51",             // CONFIG operacional (BRF: 14.605.065/0001-75)
      "numeroLacre": "",                               // Branco
      "indicaLocalEntregaNome": "R JOAO FERNANDES,L01",// Logradouro + número do dest
      "indicaLocalEntregaBairro": "JOAO FERNANDES",    // Bairro do dest
      "indicaLocalEntregaCidade": "Armação dos Búzios",// Cidade do dest
      "indicaLocalEntregaCep": "28950-000",            // CEP do dest
      "indicaLocalEntregaUf": "RJ",                    // UF do dest
      "indicaLocalEntregaTel": "",                     // Telefone do dest
      "atributo1": "A", "atributo2": "", "atributo3": "",
      "seqEmbarque": 1,                                // Sequência de embarque (carga montada)
      "DhprevEntrega": "",                             // Branco
      "DhprevEmbarque": "06/12/2022 18:30:00",         // Data emissão da remessa + 2 dias (Unidasul)
      "DhprevServico": "",                             // Branco
      "numeroRefPedCliente": "",                       // Branco
      "Observacao": "Teste de importações API - Pedido",
      "ITEM_PEDIDO": [
        {
          "numeroSequencialItem": 1,                   // Sequência do item no pedido/remessa
          "tpAcao": "I",                               // Sempre "I"
          "codigoProduto": "001COD",                   // Código do produto (cProd)
          "qtdeEmbalagem": 10,                         // Qtd de caixas a separar
          "quantidadeProduto": 300,                    // Qtd na unidade menor (UND/KG)
          "qtdMandante": "E",                          // "E" embalagens/caixas ou "U" produtos
          "tipoSeparacao": "RES;FEF;PLI",              // CONFIG (mesmo da sep1)
          "tipoSeparacao2": "AGL;FEF;PLL",             // CONFIG (mesmo da sep2)
          "valorDetalhe1": "", "valorDetalhe2": "", "valorDetalhe3": "", "valorDetalhe4": "",
          "tipoEspecificacaoItem": "",                 // LOTE/DATA/ShelfLife quando aplicável
          "valorEspecificacaoItem": ""
        }
      ]
    }
  ]
}
```

---

## I60 — NOTAS_VENDA (nota fiscal, associa ao DocCarga)

```jsonc
{
  "NOTAS_VENDA": [
    {
      "cnpjEmitente": "00.000.000/0001-00",            // emit/CNPJ do XML
      "cnpjRemetente": "11.111.111/0001-11",           // emit/CNPJ do XML
      "cnpjDestinatario": "22.222.222/0001-22",        // dest/CNPJ
      "cnpjLocalEntrega": "22.222.222/0001-22",        // dest/CNPJ
      "cnpjTransportadora": "03.333.333/0001-33",      // transp/transporta/CNPJ; se ausente, CNPJ armazém (config)
      "codNatOperacaoFiscal": "5.906",                 // CFOP do primeiro item (det/prod/CFOP)
      "numeroNota": "000174639",                       // nNF
      "serieNota": "001",                              // serie
      "chavenfe": "3315...0000",                       // Chave da NFe (44 dígitos)
      "protocolo": "333150131636849",                  // protNFe/infProt/nProt
      "cnpjNota": "00.000.000/0001-00",                // emit/CNPJ
      "tipoNota": "AR",                                // Fixo "AR"
      "dataEmissao": "14/12/2015 10:48:35",            // dhEmi (DD/MM/YYYY HH:MM:SS)
      "qtdeTotalNota": 5,                              // Qtd total de caixas da nota
      "pesoBrutoKg": 0,                                // transp/vol/pesoB
      "pesoLiquidoKg": 22.44,                          // transp/vol/pesoL
      "qtdeVolume": 1,                                 // transp/vol/qVol (template usa 0)
      "valorFrete": 0, "valorSeguro": 0, "valorDespesaAcessoria": 0, "valorDesconto": 0,
      "valorTotal": 179.52,                            // total/ICMSTot/vNF
      "formaTransporte": "PR",                         // Fixo "PR"
      "baseICMS": 0, "valorIPI": 0,
      "cnpjProprietariaProduto": "00.000.000/0001-00", // CONFIG proprietária
      "deposito": "RS",                                // CONFIG (BRF: JD)
      "cnpjArmazem": "14.605.065/0016-51",             // CONFIG (BRF: 14.605.065/0001-75)
      "numeroCertificadoSif": "", "numeroPedidoCliente": "", "nomeMotorista": "",
      "valorICMS": 0, "numeroDoca": "",
      "tipoOperacaoNota": "INE",                       // Fixo "INE" (Unidasul)
      "numeroRefPedCliente": "", "placaVeiculoPrevistoNota": "",
      "atributo1": "", "atributo2": "", "atributo3": "",
      "tpacao": "I",                                   // Sempre "I"
      "NroEmb": "",                                    // Branco
      "Tpsep": "",                                     // Branco
      "Tpsit": "INE",                                  // Fixo "INE" (IMB=gera embarque / INE=vincula sem gerar embarque)
      "Tpnota": "NV",                                  // Fixo "NV" (nota venda)
      "pedidos": ["1532082", "1532083"],              // Remessas/pedidos que geraram a nota (associa ao DocCarga da I50)
      "ITEM_NOTA_VENDA": [
        {
          "numeroSequencialItem": 1,                   // Sequência do item na nota
          "codProduto": "AAAAAAAAA3",                  // cProd
          "qtdeProduto": 50,                           // qCom (unidade menor)
          "qtdeProdutoConfirmada": 50,                 // qCom
          "qtdeEmbalagem": 50,                         // Qtd de caixas
          "valorOriginalItem": 0, "valorAtualItem": 0,
          "aliquotaICMS": 0, "aliquotaIPI": 0,
          "codSituacaoTributaria": "040",              // Fixo "040" (template)
          "numeroLote": "",                            // rastro/nLote se houver
          "valorDetalhe1": "", "valorDetalhe2": "", "valorDetalhe3": "", "valorDetalhe4": "",
          "dataFabricacao": "",                        // rastro/dFab (DD/MM/YYYY HH:MM:SS)
          "dataValidade": ""                           // rastro/dVal (DD/MM/YYYY HH:MM:SS)
        }
      ]
    }
  ]
}
```

---

## I10 — EMPRESA (cadastro; usado no caso "empresa não localizada")

Montar a partir do destinatário do XML (`dest/CNPJ`, `dest/xNome`, `enderDest`). Campos
não disponíveis no XML: vazio `""` ou default documentado. `indireto="CL"` (cliente),
`tpacao="I"`. Valores de armazém/proprietária/depósito vêm da config operacional.

```jsonc
{
  "EMPRESA": [
    {
      "codigoempresa": "223344",                       // Código ERP da empresa (ver config UsaCodigosERP)
      "razaosocial": "EMPRESA TESTE",                  // dest/xNome
      "titular": "TESTE",
      "cnpj": "02.999.460/9999-72",                    // CHAVE — dest/CNPJ
      "ie": "ISENTO",                                  // dest/IE se houver, senão ISENTO
      "endereco": "R: 15 DE ABRIL",                    // enderDest/xLgr
      "numero": "555",                                 // enderDest/nro
      "complemento": "Sl. 12",                         // enderDest/xCpl
      "bairro": "JARDIM DO TREVO",                     // enderDest/xBairro
      "cidade": "CAMPINAS",                            // enderDest/xMun
      "uf": "SP",                                      // enderDest/UF
      "cep": "111",                                    // enderDest/CEP
      "indireto": "CL",                                // "CL" cliente
      "nomeabrev": "BRF",                              // Nome abreviado
      "codtipocliente": "", "prazopagto": "0",
      "endfatura": "", "bairrofatura": "", "cidadefatura": "", "uffatura": "", "cepfatura": "",
      "telfatura": "", "emailfatura": "",
      "endcobranca": "", "bairrocobranca": "", "cidadecobranca": "", "ufcobranca": "",
      "cepcobranca": "", "telcobranca": "", "emailcobranca": "",
      "fax": "", "codtipodocto": "", "codtipocobranca": "", "nrosif": "", "dhinifaturamento": "",
      "regiaodecidade": "",
      "tpoperacao": "FI",                              // Tipo operação (ver manual; a confirmar p/ BRF)
      "tpacao": "I",                                   // "I" inclusão
      "siglaempresa": "TESTE-111",                     // Sigla única da empresa
      "classe": "B",                                   // "A" preferencial / "B" normal / "C" problemática
      "perfilfiscal": "",                              // "Normal"/"Suframa"/"Regime Especial"
      "cnpjArmazem": "98.522.246/0028-48",             // CONFIG (BRF: 14.605.065/0001-75)
      "cnpjProprietaria": "09.034.308/0001-22",        // CONFIG (BRF: 01.838.723/0098-50)
      "deposito": "R2"                                 // CONFIG (BRF: JD)
    }
  ]
}
```

---

## Campos "a confirmar" para a BRF/Friozem (defaults do template, revisar com o cliente)

| Campo | Interface | Default assumido | A confirmar |
|-------|-----------|------------------|-------------|
| `tipoCarga` | I50 | `CN` (config) | valor correto BRF |
| `tipoSeparacao` / `tipoSeparacao2` | I50 | config | buscas corretas BRF |
| `tipoAcondicionamento` | I50 | `PC` (manual) ou `PP` (Unidasul) | qual p/ BRF |
| `dataprev` / `DhprevEmbarque` | I50 | emissão + N dias | regra da BRF |
| `qtdMandante` | I50 item | `U` (produtos) | U vs E |
| `tipoOperacaoNota` / `Tpsit` | I60 | `INE` | IMB vs INE p/ BRF |
| `pedidos[]` | I60 | `[nNF]` | nNF vs `xPed` |
| `tpoperacao` | I10 | `FI` | valor p/ cliente BRF |
| `codigoempresa` | I10 | — | usa código ERP? (3509308 é do depositante) |

---

## I30 — PRODUTO (cadastro de produto; contenção)

Template comentado (base Unidasul). Usado como **contenção** quando a I50 falha com
"produto não ativo/não cadastrado". Montado a partir dos dados do XML (código,
descrição, EAN/DUN, NCM, unidade); o restante usa defaults do template. O produto
resultante é **provisório** e **entra em revisão do colaborador** (ver regra acima).
Valores operacionais (armazém/proprietária/depósito) vêm da config BRF.

Usa o envelope `{ "header": {...}, "body": {...} }` (confirmar se query string também
serve). `IDENTIFICADOR` = `WEBAPI-I30-Json`. Muitos campos são obrigatórios (S no
manual) e sem correspondência no XML → preencher com os defaults do template.

```jsonc
{
  "header": {
    "identificador": "WEBAPI-I30-Json",
    "requisitante": "<config: BRF 01.838.723/0098-50>",
    "cnpjrequisitante": "",
    "destino": "<config: 14.605.065/0001-75>",
    "cnpjdestino": ""
  },
  "body": {
    "PRODUTO": [
      {
        "codtpprod1": "2",                 // Fixo "2" (picking)
        "codigo": "<cProd do XML, SEM zeros à esquerda>",   // obrigatório
        "descricao": "<xProd do XML>",     // obrigatório
        "codempresa": "<config: código empresa proprietária, ou usar cnpjProprietaria>",
        "codunidade": "<uCom do XML; precisa existir no WMS (CX, UN...)>", // obrigatório
        "ctrunifiscal": "E",               // E=embalagem / U=unidade (a confirmar p/ BRF)
        "ctrunifisica": "E",               // peso padrão "E" / peso variável "U"
        "aliasproduto": "<descrição abreviada>",
        "codcliente": "<cProd COM zeros à esquerda, como no XML>",
        "eancliente": "<cEAN do XML (EAN13) ou branco>",
        "validaeancliente": "N",
        "codtipologia": "SEC",             // SEC/RES/CONG — a confirmar origem (BRF é alimento congelado/resfriado!)
        "tipoqtd": "C",                    // C calculada / I informada
        "organimal": "N",                  // (template Unidasul comenta "S"); a confirmar p/ BRF
        "exportacao": "N",
        "qtdliquido": "1.0000", "qtdbruto": "1.0000", "qtdmedio": "1.0000", "qtdm3": "1.0000",
        "codclassabc": "A", "codclassabcf": "B", "prioridade": "0",
        "ean": "<DUN14 do XML ou branco>",
        "aliqicms": "0.00", "aliqipi": "0.00",
        "codmensagemanter": "", "codmensagempost": "",
        "tipovalidade": "N",               // D diária / M mensal / N nenhuma — a confirmar (alimento tem validade!)
        "qtddiascriticos": "0", "qtddiasvalidade": "0", "qtddiasmaturacao": "0", "qtddiasvencimento": "0",
        "codclassificacao": "<NCM ou DEF; a confirmar>",
        "codclassificacaosif": "",
        "codgrupofatura": "", "ctrunifatura": "U", "percentualseguro": "0",
        "codtpprod2": "1", "codtpprod3": "3",
        "temppadrao": "25.00",             // temperatura — a confirmar p/ BRF (congelado -12, resfriado 5, seco 27)
        "tempcongelamento": "0.00", "temprcf": "0.00", "qtdmedcxpick": "0",
        "tpacao": "I",                     // só inclusão (contenção); sem update
        "codigotributario": "040",
        "codunidadefisica": "", "tparmazpicking": "",
        "qtdunembalag": "1.000",           // qtd produto por caixa
        "qtdpcembalag": "1.000",           // peso bruto da caixa
        "codcatmanuseio": "4",
        "qtddiasvalcrisup": "", "qtddiasvalcriinf": "", "qtddiasvalncrisup": "", "qtddiasvalncriinf": "",
        "coddetalhe1": "", "coddetalhe2": "", "coddetalhe3": "", "coddetalhe4": "",
        "qtdunperigosa": "",
        "qtdcxcamada": "1",                // se 0 não cria embalagem; template sugere "10" fallback
        "largura": "1.00", "altura": "1.00", "profundidade": "1.00",
        "codtipocontenedor": "PALLET",
        "qtdpadraopallet": "1",            // fallback template "50"
        "psindividual": "1.000",           // peso líquido da caixa; se 0 não cria embalagem
        "classificacaoncm": "<NCM do XML>",
        "qtddiascongelamento": "0",
        "cnpjArmazem": "<config: 14.605.065/0001-75>",
        "cnpjProprietaria": "<config: 01.838.723/0098-50>",
        "deposito": "<config: JD>",
        "usactlindividual": "N"
      }
    ]
  }
}
```

### Campos "a confirmar" do I30 para a BRF (produto entra em revisão de qualquer forma)

Como o produto criado por contenção já entra em revisão do colaborador, os defaults
abaixo são aceitáveis para desbloquear a nota, mas idealmente seriam ajustados:

| Campo | Default | Observação p/ BRF |
|-------|---------|-------------------|
| `codtipologia` | `SEC` | BRF é alimento — provável RES/CONG, não SEC |
| `tipovalidade` | `N` | alimento tem validade (D?) |
| `temppadrao` | `25.00` | congelado/resfriado tem temperatura própria |
| `ctrunifiscal`/`ctrunifisica` | `E` | confirmar E vs U |
| `codempresa` | config | usa código empresa ou cnpjProprietaria |
| `qtdliquido`/`qtdbruto` | `1.0000` | peso real da caixa não vem no XML |

> Esses defaults fazem o produto nascer "genérico"; por isso a revisão do colaborador é
> obrigatória. O objetivo do I30 de contenção é só **não travar a nota**.
