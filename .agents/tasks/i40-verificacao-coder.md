# Verificacao do coder: busca por numero+serie e remocao da placa (I40)

Iteracao 1 (nao havia i40-review.json). Sem push, sem envio ao WMS, sem acesso ao SRVAPPALC02.

## Comandos e resultados
- `dotnet build c:\wms-integrador\KitWmsIntegrador.sln --no-incremental`: 0 avisos, 0 erros.
- `dotnet test c:\wms-integrador\KitWmsIntegrador.sln`: 487 aprovados, 0 falhas, 9 ignorados (stress), total 496.
  Antes da mudanca: 451 aprovados, 9 ignorados.
- Nenhum pacote NuGet novo. Nenhum arquivo .xml versionado (git status sem .xml; testes de busca criam arquivos vazios
  em pasta temporaria; os testes antigos continuam usando os XMLs de tests\...\Recursos, que nao sao commitados).

## O que mudou
- Core: `ValidadorChavesDevolucao` (CnpjDaChave, SerieDaChave, NumeroDaChave, ValidarNumeroSerie, SoDigitos; sem placa),
  `LocalizadorXmlDevolucao.BuscarPorNumeroSerie` (so nomes de arquivo; Processados e pacote_*; sem Erro; CNPJ do depositante;
  inteiros; chave unica = copia mais recente; ordenado do mais recente), `ServicoDevolucao.Buscar` (400/503), modelos
  `OpcaoNotaDevolucao`/`ResultadoBuscaDevolucao`, `Serie` no resultado por chave, auditoria sem coluna de placa
  (`data;ip;chave;nota;status;transacao;mensagem`), `ConstrutorI40.Construir(nota)` sem placa/motorista,
  `OpcoesDevolucao` sem EnviarPlacaPadrao/TipoOperacaoNotaComPlaca/NomeMotorista.
- Worker: `GET /api/devolucao/buscar?numero=&serie=` (200 com opcoes, 404 "Nota nao encontrada na retencao de 60 dias.",
  400, 503, 500 generico), `/api/config` so com maxChaves, POST enviar sem placa (campos antigos no JSON sao ignorados),
  pagina com campos numero+serie, tabela de opcoes quando ha mais de uma chave, labels, foco visivel, aria-live/role=alert,
  modo manual `--chave` ou `--numero --serie` (+ `--xml`; varias chaves = lista e nao envia).
- appsettings.json do Worker e deploy\config-exemplo\appsettings.Production.json limpos (Devolucao:Habilitada e I60:Habilitada
  continuam false). I50:EnviarPlacaVeiculo nao foi tocado (outra interface).
- Testes: removidos os de placa/motorista; novos para extracao da chave, busca (uma, varias, nenhuma, outra serie, outro CNPJ,
  zeros a esquerda, Erro ignorado, subpasta que nao e pacote, mais recente por pacote, raiz de Processados, minusculas,
  nomes invalidos), service.Buscar, endpoint de busca (200/404/400 parametrizado/503/500) e pagina sem "placa".
- Docs: ARQUITETURA (secao 19), ROTEIRO (secoes 9 e 10), MANUAL-USUARIO, MANUAL-TECNICO, OPERACAO, INSTALACAO-SERVIDOR,
  ENCERRAMENTO, diario 2026-11-10. Manuais HTML/PDF NAO regenerados (pedido). Esforco: so horas; horas humanas "a informar".

## Nao verificado
- A pagina nao foi aberta em navegador (so testes do servidor e do HTML servido); o JS nao tem teste automatizado.
- Envio real ao WMS e o servidor SRVAPPALC02 nao foram tocados.