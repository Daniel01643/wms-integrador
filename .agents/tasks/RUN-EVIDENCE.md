# Evidência de build / test / scan — KitWmsIntegrador (etapas 1+2)

Registro das execuções feitas pelo coder para que o gate confira contra saída real,
sem re-rodar as suítes. SDK .NET 8.0.427 em `C:\Program Files\dotnet`; invocado por
caminho absoluto (`& "C:\Program Files\dotnet\dotnet.exe"`). Shell: PowerShell/Windows.

## 1. Build

Comando:

```
& "C:\Program Files\dotnet\dotnet.exe" build KitWmsIntegrador.sln -c Release
```

Resultado: **Compilação com êxito — 0 Aviso(s), 0 Erro(s)**. Exit code 0.
Projetos: `KitWmsIntegrador.Core`, `KitWmsIntegrador.Worker`, `KitWmsIntegrador.Tests`.

## 2. Testes

Comando:

```
& "C:\Program Files\dotnet\dotnet.exe" test KitWmsIntegrador.sln -c Release `
    --logger "trx;LogFileName=test-results.trx" `
    --results-directory .agents/tasks/test-results
```

Resultado: **Aprovado! — Com falha: 0, Aprovado: 14, Ignorado: 0, Total: 14**. Exit
code 0. Log TRX: `.agents/tasks/test-results/test-results.trx`.

Cobertura (14 testes): `ExtratorTransporteTests` (regex preserva zeros à esquerda,
null quando ausente), `LeitorNotaFiscalXmlTests` (2 XMLs reais BRF — nNF, chave,
transporte, itens), `AgrupadorTransporteTests` (ondas, tamanhos, lista SemTransporte),
`PendenciaSemTransporteTests` (XML sintético sem `Transporte:` percorre parse→agrupamento
e cai em `SemTransporte` sem quebrar — fecha a lacuna não-BRF ponta-a-ponta).

## 3. Varredura real sobre `C:\Envia xml` (somente leitura)

Comando:

```
& "C:\Program Files\dotnet\dotnet.exe" run --project src/KitWmsIntegrador.Worker -c Release
```

Log bruto: `.agents/tasks/scan-run.log`. Resumo obtido:

| Métrica                              | Valor |
|--------------------------------------|-------|
| XMLs encontrados                     | 1196  |
| Notas lidas/parseadas                | 1196  |
| Erros de parse                       | 0     |
| Transportes/ondas distintos          | 398   |
| XMLs sem transporte (pendências)     | 0     |
| Tamanho das ondas — mínimo            | 1     |
| Tamanho das ondas — máximo            | 10    |
| Tamanho das ondas — média             | 3,01  |

Distribuição (notas por onda → quantidade de ondas):

| Notas/onda | Ondas |
|-----------:|------:|
| 1          | 143   |
| 2          | 45    |
| 3          | 58    |
| 4          | 70    |
| 5          | 21    |
| 6          | 38    |
| 7          | 15    |
| 8          | 3     |
| 9          | 3     |
| 10         | 2     |

Soma: 143+45+58+70+21+38+15+3+3+2 = **398 ondas**;
1·143 + 2·45 + 3·58 + 4·70 + 5·21 + 6·38 + 7·15 + 8·3 + 9·3 + 10·2 = **1196 notas**.

## 4. Garantias (offline / não-destrutivo)

- Pasta `C:\Envia xml`: 1196 XMLs antes e **1196 depois** da execução — nenhum arquivo
  movido, alterado ou deletado (apenas leitura, `FileShare.Read`).
- Nenhuma chamada de rede (grep por `HttpClient`/`System.Net`/`GetToken`/SQL retorna só
  o TODO em `IClienteWms`; nenhum código de transporte ou persistência nesta fase).
- Nenhum banco de dados criado.

Os números batem com as referências esperadas do plano (≈1196 / ≈398 / 0 sem transporte).
