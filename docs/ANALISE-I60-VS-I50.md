# Análise: dispensar a I60? Comparação I60 × I50

Contexto: avaliar se o envio da **I60 pode ser dispensado**, já que a **I50** cria o
DocCarga e associa a nota. Para isso, é preciso saber quais dados **só a I60 carrega** e
se há onde acomodá-los na I50 (campos livres / observação). **Decisão em avaliação** —
este doc é a base técnica.

## Premissa a validar com o fornecedor (no chamado)

A I50 realmente **registra a nota fiscal** (chave/série/protocolo) ou só cria o
documento de carga e os itens? Se a I50 NÃO persiste os dados fiscais da nota, dispensar
a I60 significa **perder rastreabilidade fiscal** no WMS — e aí a I60 não é dispensável,
só o fluxo muda. **Confirmar no chamado antes de decidir.**

## Dados que SÓ a I60 carrega (sem campo equivalente na I50)

| Dado (origem no XML) | Campo na I60 | Campo na I50 | Como acomodar na I50 |
|----------------------|--------------|--------------|----------------------|
| Chave NF-e (44 díg.) | `chavenfe` | — | `atributo1` (44 chars) |
| Protocolo autorização | `protocolo` | — | `atributo2` (~15 chars) |
| Série da nota | `serieNota` | — | observação ou atributo |
| CFOP | `codNatOperacaoFiscal` | — | observação |
| Valor total da nota | `valorTotal` | — | observação |
| Peso bruto / líquido | `pesoBrutoKg`/`pesoLiquidoKg` | — | observação |
| Qtd de volumes | `qtdeVolume` | — (deriva dos itens) | — |
| Emitente/remetente CNPJ | `cnpjEmitente`/`cnpjRemetente` | — (só `cnpjProprietario`) | observação |
| Valor unitário do item | `valorOriginalItem` (item) | — (item I50 sem valor) | `valorDetalhe*` do item |
| Tipo/forma/ICMS/IPI | vários | — | geralmente dispensável no DocCarga |

## Campos livres disponíveis na I50 para receber esses dados

- **`atributo1`, `atributo2`, `atributo3`** — campos genéricos (até ~200 chars cada,
  conforme manual). Candidatos naturais: chave NF-e, protocolo, série.
- **`Observacao`** — texto livre (até ~255 chars). Candidato para dados adicionais
  concatenados (CFOP, valor total, pesos) **conforme o tamanho** — respeitar o limite.
- **`valorDetalhe1..4`** (por item) — para dados do item (ex.: valor unitário).

## DECISÃO (confirmada pelo usuário)

Carregar os dados fiscais da I60 nos atributos/observação da I50 **para riqueza de
informação/rastreabilidade visual no WMS** (o DocCarga nasce de um XML, então é útil ter
esses dados visíveis). Mapeamento confirmado:

| Campo I50 | Conteúdo | Origem |
|-----------|----------|--------|
| **atributo1** | Chave NF-e + Protocolo | `chavenfe` + `nProt` |
| **atributo2** | Número da nota + Série + CFOP | `nNF` + `serie` + CFOP 1º item |
| **atributo3** | Valor Total + Pesos (bruto/líq.) + Qtd volumes | `vNF` + `pesoB`/`pesoL` + `qVol` |
| **Observacao** | Dados adicionais | `infCpl` (texto livre), truncado ao limite |

- **atributo3**: formatar de forma SIMPLES para leitura visual (ex.:
  `Vlr: 12.644,40 | PB: 302,675 | PL: 275,640 | Vol: 36`). Rótulos curtos + separador.
- Limites: `atributo1/2/3` = até 200 chars cada (manual I50); `Observacao` = até 255.
  Truncar `Observacao` ao limite preservando o início do infCpl.

## I60: DESLIGAR por ora, NÃO remover

- Adicionar **flag de configuração** (ex.: `OpcoesI60.Habilitada = false` por padrão
  agora) que desliga o envio da I60. O código da I60 (construtor, DTO, testes)
  **permanece** — só não é invocado quando a flag está desligada.
- Reativável quando o fornecedor esclarecer a conferência de quantidade / rastreab.
  fiscal. NÃO é remoção definitiva.

## Risco (registrado, aceito pelo usuário)

Dados fiscais em `atributo`/`Observacao` são para **rastreabilidade/consulta visual**, não
campos estruturados que o WMS trate fiscalmente (busca/validação). Decisão consciente:
serve para enriquecer a visão do DocCarga no WMS. Se futuramente for preciso tratamento
fiscal real, reativar a I60.
