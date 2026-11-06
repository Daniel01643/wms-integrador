# Verificação — scripts de deploy permanentes no servidor (`tools\deploy`)

Objetivo: tornar o servidor autossuficiente, replicando os scripts de operação de `deploy\` para a
pasta **permanente** `C:\Friozem\importacao_notas\tools\deploy`, de modo que o técnico opere direto
do servidor (RDP) sem depender de `C:\temp` nem de WinRM da máquina de build.

## O que mudou

### `deploy\_comum.ps1`
- Nova função `Copy-ScriptsDeployParaServidor -PastaOrigem -PastaDestino` (dot-sourced, com
  `-WhatIf`). Copia **todos** os `.ps1` de `PastaOrigem` (inclusive `_comum.ps1`) para
  `PastaDestino`, **sobrescrevendo** (idempotente). Cria a pasta de destino se faltar via
  `Confirm-Directory`. Se origem == destino (já rodando de `tools\deploy`), é **no-op**. Loga o
  caminho de destino. Só gerencia a pasta informada — não apaga outros conteúdos de `tools\`.

### `deploy\instalar.ps1`
- Novos parâmetros: `-CopiarScripts` (`[bool]`, default `$true`) e `-PastaScriptsDestino`
  (default `<RaizInstalacao>\tools\deploy`), com documentação no cabeçalho.
- Após criar a estrutura de pastas (incl. `tools\`), chama `Copy-ScriptsDeployParaServidor` a partir
  de `$PSScriptRoot` para replicar os scripts na pasta permanente (loga o destino). Todo o resto
  (NSSM, ACL config, TLS opcional, não iniciar sem segredos) permanece inalterado.

### `deploy\publicar-release.ps1`
- Novos parâmetros: `-CopiarScripts` (default `$true`) e `-PastaScriptsDestino`
  (default `<RaizInstalacao>\tools\deploy`), documentados.
- Ao promover a release (após retenção), atualiza os scripts em `tools\deploy` (idempotente), para
  acompanharem a versão.

### `docs\INSTALACAO-SERVIDOR.md`
- Nota de destaque no topo: servidor autossuficiente; `tools\deploy` é a fonte de verdade dos
  scripts no servidor; `C:\temp\deploy` só no 1º install.
- Diagrama de estrutura: acrescentado `tools\deploy\`.
- §2 RDP: primeira instalação (de `C:\temp\deploy`) vs. dia a dia (de `tools\deploy`).
- §3.1 diagnóstico: exemplos para os dois momentos.
- §3.4 instalar: documenta a replicação dos scripts e os parâmetros `-CopiarScripts`/
  `-PastaScriptsDestino`.
- §3.4b / §3.5 / §3.7 / §3.8 / §3.9 / §3.10: exemplos do dia a dia passaram de `.\deploy\<script>`
  para rodar a partir de `C:\Friozem\importacao_notas\tools\deploy`.
- §3.9: fluxo de ATUALIZAR versão (build → copiar zip/deploy → `tools\deploy\publicar-release.ps1`),
  que também atualiza os scripts.
- §5: item sobre scripts de operação permanentes acompanhando a release.

## Coerência dos demais scripts (sem mudança necessária)

Todos fazem `. (Join-Path $PSScriptRoot '_comum.ps1')` e resolvem o `nssm.exe` por
`Resolve-NssmPath ... -RaizScripts $PSScriptRoot` (parâmetro/default/fallback). Nenhum depende de
caminho absoluto de `deploy\`. Logo, rodar de `tools\deploy` funciona sem alteração. Não foram
encontrados caminhos relativos frágeis — nenhuma correção extra foi necessária.

Nenhum código `.cs` de negócio, `diario` ou `controle` foi tocado.

## Verificações executadas

### 1. Parse de todos os `.ps1` de `deploy\` (0 erros)
`[System.Management.Automation.Language.Parser]::ParseFile` em cada arquivo:

```
OK  : compartilhar-pasta.ps1
OK  : configurar-segredos.ps1
OK  : diagnostico.ps1
OK  : instalar.ps1
OK  : publicar-release.ps1
OK  : publicar.ps1
OK  : remover.ps1
OK  : rollback.ps1
OK  : servico.ps1
OK  : _comum.ps1
Total de arquivos: 10; com erro: 0
```

### 2. Simulação da cópia para pasta temporária (sem lógica de NSSM)
- `Copy-ScriptsDeployParaServidor` copiou os **10** `.ps1`, incluindo `_comum.ps1`.
- Dot-source de `_comum.ps1` a partir do destino: OK (`Write-Passo` disponível) — confirma que um
  script rodando de `tools\deploy` acha o `_comum.ps1` ao lado.
- Idempotência: com origem == destino, a função não copia (no-op, apenas loga).
- Pasta temporária removida ao final.

`instalar.ps1` completo **não** foi executado localmente (depende do servidor). Nada foi executado
contra o servidor real.

## Caminho destino dos scripts no servidor
`C:\Friozem\importacao_notas\tools\deploy` (default; ajustável por `-PastaScriptsDestino`).
