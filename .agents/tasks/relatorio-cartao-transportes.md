# Relatório: cartão "Transportes" zerado em pacote TSP

## Causa raiz
`ModeloRelatorio.Montar` calculava `Resumo.Transportes = gruposPuxada.Count`, ou seja, só os grupos de notas PUXADA. Em pacote TSP todas as notas são `EhTsp` e agrupadas por onda, então o cartão ficava 0 (independe do status). HTML, PDF e e-mail usam o mesmo `Resumo.Transportes`, então os três foram corrigidos de uma vez.

## Correção
`src/KitWmsIntegrador.Core/Relatorio/ModeloRelatorio.cs`: `Transportes` agora é a contagem de números de transporte distintos de todas as notas (PUXADA, TSP, raiz), qualquer status, ignorando vazio e os rótulos "sem transporte".

## Testes
- Novo: `Modelo_PacoteSoTsp_NaoIntegrado_CartaoTransportesContaTransportesDistintos` (4 transportes na onda SOROCABA em Pendente + 2 distintos na SJC em Não integrada, 3 notas -> cartão = 6; HTML contém `<b>6</b>Transportes</div>`).
- Ajustado: `Modelo_AgrupaTspPorOnda_...` esperava 2 (só PUXADA); agora 8 (2 PUXADA + 4 + 2 TSP).

## Comandos e resultados
- O Worker (PID 31436) está rodando a partir de `src\KitWmsIntegrador.Worker\bin\Release` e trava a DLL, então `dotnet build ... -c Release` in-place falha com MSB3021. Não encerrei o processo do usuário; verifiquei numa cópia em %TEMP% (sem bin/obj/.git), depois removida.
- `dotnet build KitWmsIntegrador.sln -c Release`: 0 Aviso(s), 0 Erro(s).
- `dotnet test KitWmsIntegrador.sln -c Release --no-build`: 655 aprovados, 0 falhas, 9 ignorados (baseline 654 + 1 novo).
- Edge headless screenshot: `C:\wms-integrador\.agents\tasks\cartao-transportes.png` mostra o cartão "6 Transportes" (7 notas, 0 integradas, 4 pendentes, 3 não integradas).

## Esforço
Não informado (horas humanas não fornecidas).
