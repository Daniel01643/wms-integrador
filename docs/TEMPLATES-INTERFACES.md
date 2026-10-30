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
- `WEBAPI-I50-Json` → pedido/DocCarga (`PEDIDO`)
- `WEBAPI-I60-Json` → nota de venda (`NOTAS_VENDA`)

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
