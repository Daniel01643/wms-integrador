# Relatório final: Puxada TSP

Data: 12/11/2026. Sem push (tudo local na `main`).

## Escopo entregue
- Duas pastas de entrada: `Puxada` e `PuxadaTSP`. Arquivos soltos na raiz são tratados como puxada normal.
- Entrada por ZIP (leitor seguro, limites `Zip:MaxArquivos` e `Zip:MaxTamanhoMb`); categoria de erro `ZipInvalido`.
- Puxada TSP: uma onda por ZIP, nomeada pelo nome do ZIP (sem texto SJC/Sorocaba, sobe sem). Puxada normal segue o desenho anterior.
- Relatório (HTML, PDF, e-mail e telemetria) com colunas Tipo (TSP/PUXADA), Origem e TSP por onda, para o usuário corrigir pasta errada.
- Manual oficial do usuário atualizado com o fluxo novo (ZIPs em Puxada/PuxadaTSP).

## Arquivos alterados (commits 0611d3d a d7b8f90, mais os ajustes desta verificação)
Código (`src/`):
- Core/Pacote: `CategoriaErro`, `ClassificadorEntrada`, `DiarioPacote`, `GerenciadorPacote`, `LeitorZipSeguro`, `ProcessadorLote`
- Core/Relatorio: `LinhaRelatorioErro`, `ModeloRelatorio`, `RenderizadorHtml`, `RenderizadorPdf`, `RenderizadorPdfResumo`
- Core/Email: `CorpoEmailResumo`, `CorpoEmailResumoSemanal`
- Core/Monitoramento: `AgregadorTelemetria`, `MapeadorTelemetria`, `RegistroTelemetria`, `ResumoPeriodo`
- Worker: `Program.cs`

Testes (`tests/`): `ClassificarPastasTests`, `LeitorZipSeguroTests`, `PacoteTspEndToEndTests`, `ProcessadorLoteTspTests`, `RelatorioTipoOrigemTests`, `Infra/AmbientePacote`, `Stress/AmbienteStress`.

Documentação: `ARQUITETURA`, `OPERACAO`, `INSTALACAO-SERVIDOR`, `ENCERRAMENTO`, `ESCOPO-PUXADA-TSP`, `ROTEIRO-TESTE-PUXADA-TSP`, `CONTROLE-ESFORCO`, `controle/SEMANAL`, `controle/MENSAL`, `diario/2026-11-12`, manuais (usuário, técnico, agente de IA; `.md`, `.html`, `.pdf`), deck (`apresentacao.html` e `.pdf`).

Ajuste desta verificação: removidos valores monetários dos diários `2026-10-29`, `2026-10-30`, `2026-11-05` e `2026-11-06` (valor/hora, R$, custo, câmbio), substituídos por registro somente em horas.

## Build e testes
- `dotnet build KitWmsIntegrador.sln` Debug: 0 avisos, 0 erros.
- `dotnet build KitWmsIntegrador.sln -c Release`: 0 avisos, 0 erros.
- `dotnet test -c Release`: 593 no total, 584 aprovados, 9 ignorados (cenários de stress, marcados como ignorados), 0 falhas.

## Verificações de repositório
- `git ls-files`: nenhum `.xml`, `.XML` ou `.zip` versionado.
- `Habilitada=false` mantido: `I60` (código `OpcoesI60`, `appsettings.json` e `appsettings.Production.json`) e `Devolucao` (`OpcoesDevolucao` e `appsettings.Production.json`).
- Valores monetários: o deck (`apresentacao.html`) está limpo. Os diários acima foram corrigidos. Restam apenas a frase da regra em `CONTROLE-ESFORCO.md` (proíbe valores) e "R$" de impostos dentro de XML/observação de exemplo da nota (`docs/exemplos`), que não são custo de esforço.
- Pacotes NuGet: nenhum `.csproj` mudou desde antes da Puxada TSP (`git diff` sem alterações em `PackageReference`). Nenhum pacote novo.

## Screenshots conferidos (de `verificacao-docs.md`)
- Slide 8 do deck (1280x720): 10 linhas na tabela com nota de rodapé, nada truncado, rodapé visível.
- Slide 12 do deck (1280x720): três caixas completas, nada truncado.
- Conferências complementares: manuais regenerados (usuário 7 páginas, técnico 15 páginas), `apresentacao.pdf` com 12 páginas, 0 U+FFFD, BOM mantido no `apresentacao.html`.
- Limitação: não validei a fidelidade visual dos PDFs dos manuais página a página (só contagem de páginas).

## Pendências para o teste do usuário no homolog
Seguir `docs/ROTEIRO-TESTE-PUXADA-TSP.md`:
- Criar as pastas `Puxada` e `PuxadaTSP` no servidor e conferir o compartilhamento herdado.
- Testar com ZIP real de Puxada TSP: uma onda por ZIP, nome da onda e transporte por nota.
- Testar um ZIP em cada pasta e um arquivo solto na raiz (deve subir como puxada).
- Testar ZIP inválido/corrompido (categoria `ZipInvalido`) e conferir os limites de `Zip`.
- Conferir no relatório as colunas Tipo, Origem e TSP por onda, inclusive o caso de ZIP na pasta errada.
- Fora do escopo: I60 e Devolução seguem desligadas.

## Horas (somente horas)
| Data | Humanas | IA |
|---|---|---|
| 11/11 | 2,0 h | aprox. 3,5 h |
| 12/11 | a informar pelo usuário | aprox. 2,5 h (parcial) |

Total de IA nos dois dias: aprox. 6,0 h.

## Git
Sem push. Os demais artefatos de `.agents/tasks` (planos, reviews, verificações) seguem sem versionar, conforme a convenção do repositório. Este relatório também fica local.
