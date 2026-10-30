# Verificação — Etapa 4 (Construtores de payload I50/I60/I10)

Executado nesta máquina via `C:\Program Files\dotnet\dotnet.exe` (SDK 8.0.x),
no diretório `c:\wms-integrador`. ESTA ETAPA NÃO FAZ CHAMADAS HTTP — os construtores
só montam objetos/JSON a partir da `NotaFiscal` já parseada.

## Comandos executados e resultados

1. `dotnet build KitWmsIntegrador.sln -c Debug`
   → **Compilação com êxito, 0 avisos, 0 erros** (Core, Worker, Tests).

2. `dotnet test KitWmsIntegrador.sln`
   → **Aprovado! Com falha: 0, Aprovado: 59, Ignorado: 0, Total: 59.**
   - 30 testes pré-existentes (etapas 1–3) permanecem verdes.
   - +29 testes novos dos construtores (`ConstrutorI50Tests` 14, `ConstrutorI60Tests` 10,
     `ConstrutorI10Tests` 5).

3. `dotnet build KitWmsIntegrador.sln -c Release`
   → **Compilação com êxito, 0 avisos, 0 erros.**

4. Varredura de segredos em `appsettings*.json` (`Usuario|Senha|BLUEFOOD|b$qNg`)
   → apenas o comentário pré-existente `_comentario_credenciais` (instrução de usar
   env `WMS__Usuario`/`WMS__Senha`); **nenhum valor de credencial/senha gravado.**

## O que foi verificado pelos testes (offline, a partir de 1 XML real)

XML: `NFE35251201838723009850550060156658071604491612.XML`
(nNF 15665807, transporte 0130693549, 2 itens, emitente BRF 01.838.723/0098-50,
destinatário FREE MAN 28.288.914/0001-19).

- **I50**: `nroagrupseparacao = "0130693549"` (zeros preservados); 1 nota → 1 DocCarga;
  `numeroPedido = "15665807"`; chave de 44 dígitos; CNPJs operacionais e depósito vindos
  da config; `tipoCarga`/`tipoSeparacao`/`tipoSeparacao2`/`tipoAcondicionamento`/
  `qtdMandante` vindos da config (testado com config custom) e aplicados ao cabeçalho E
  a cada item; itens mapeados com `qCom`; `dataEmissao`/`DhprevEmbarque` = `15/12/2025
  08:29:34` (DD/MM/YYYY HH:MM:SS); decimais com ponto; `numeroTransporte` vazio lança
  `ArgumentException`; `numeroEmbarque` nulo é omitido do JSON; JSON contém os nomes
  exatos `PEDIDO`, `nroagrupseparacao`, `ITEM_PEDIDO`, `DhprevEmbarque`, `tipoSeparacao`.
- **I60**: `chavenfe` (44 dígitos), `numeroNota`, `serieNota` da nota; `pedidos = ["15665807"]`
  (amarração bate com o `numeroPedido` do I50 para a mesma nota); `Tpsit="INE"`,
  `tipoOperacaoNota="INE"`, `Tpnota="NV"`, `tipoNota="AR"`; CFOP do 1º item; pesos/volume/
  valorTotal da nota; CNPJs operacionais da config; decimais com ponto; JSON contém
  `NOTAS_VENDA`, `chavenfe`, `pedidos`, `Tpsit`, `Tpnota`, `NroEmb`, `ITEM_NOTA_VENDA`.
- **I10**: `cnpj`/`razaosocial`/endereço do destinatário; CNPJs armazém/proprietária e
  depósito da config; valores fixos `indireto="CL"`, `tpacao="I"`, `ie="ISENTO"`,
  `classe="B"`; JSON contém `EMPRESA`, `cnpj`, `cnpjArmazem`, `deposito`.

## Campos "a confirmar" marcados no código (TODO/comentário)

`tipoAcondicionamento` (PC vs PP), `dataprev`/`DhprevEmbarque` (emissão + N dias?),
`qtdMandante` (U vs E), `tipoOperacaoNota`/`Tpsit` (IMB vs INE), conteúdo de `pedidos[]`
(nNF vs xPed), `codigoempresa`/`siglaempresa`/`tpoperacao` da I10. Defaults razoáveis do
template aplicados e documentados (ver `docs/TEMPLATES-INTERFACES.md` e os comentários
nos DTOs/construtores).

## Fora de escopo (NÃO feito)

Chamadas HTTP/envio ao WMS; orquestração/máquina de estados; store de estado;
notificador; movimentação de arquivos. Leitura somente em `tests/.../Recursos`
(cópia dos XMLs reais); `C:\Envia xml` não foi tocado.
