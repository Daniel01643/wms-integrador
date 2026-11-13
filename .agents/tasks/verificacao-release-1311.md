# Verificação de integração FEAT-001..003 (release 13/11/2026), iteração 1

Executado na cópia temporária `C:\Users\DANIEL~1\AppData\Local\Temp\kitwms-build-1311` (robocopy /MIR /XD bin obj .git artifacts, XMLs mantidos), sobre o HEAD `e2f94fb`. A cópia foi apagada ao final. Nada foi tocado no servidor de produção.

## Comandos e resultados

- `dotnet build KitWmsIntegrador.sln -c Release`: 0 avisos, 0 erros.
- `dotnet test KitWmsIntegrador.sln -c Release`: 926 aprovados, 9 ignorados, 0 falhas (linha de base 891; +35 testes novos: FEAT-001 +14, FEAT-002 +5, FEAT-003 +16).

## Critérios conferidos

- FEAT-001: `Pacote` com ModoJanela=Silencio, 15 s e teto 180 s no appsettings.json e no exemplo de produção; rótulo 'Coleta (janela de silencio)' no relatório; testes antigos fixam ModoJanela=Fixa.
- FEAT-002: I60 QtdeVolumeZero/QtdeTotalNotaZero=true; I51/I60/Devolucao Habilitada=false; I50 EnviarPlacaVeiculo=true (Puxada normal com placa) e EnviarPlacaVeiculoTsp=false; DiasRetencao=30 e DiasRetencaoEstado=30. Varredura de '60 dias' em src, deploy e docs: sobras apenas em docs/ENCERRAMENTO.md (linhas 16, 70, 87, histórico, escopo da FEAT-005) e notas históricas 'ajustada para 30 dias'.
- FEAT-003: AlertaImediato=true em appsettings e exemplo; testes AlertaI60Tests e bloco Atenção do relatório passam; PNGs de evidência em .agents/tasks.
- Junções: appsettings.json e exemplo de produção coerentes entre as três FEATs (seções Pacote, Limpeza, I50, I51, I60); DI do alerta e AmbienteI51 compilam e passam; nenhum ajuste de código foi necessário.

## Pendências fora deste escopo

- docs/ENCERRAMENTO.md ainda cita retenção de 60 dias (FEAT-005); manuais e deck (FEAT-004).