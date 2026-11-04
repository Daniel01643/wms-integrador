# Etapa 7: watcher contínuo, pacote, movimentação, limpeza e e-mail

Substitui o Worker de execução única por um loop contínuo que abre um pacote ao detectar XMLs, integra por transporte via Orquestrador, move cada arquivo para Processados/Erro logo após o resultado, fecha o pacote na quietude com relatório HTML/PDF e envia e-mail opcional. Toda a lógica fica no Core, com o Worker fino. Build com 0 avisos e 0 erros; 206 testes passando (baseline 129). Orquestrador, construtores, OpcoesI60, docs/diario e docs/controle não aparecem no diff. A única mudança em código existente é a adição de 3 overloads `string?` em `ClassificadorRetorno`.

Watch for: (1) retomada após queda reenvia ao WMS os arquivos que ficaram na entrada, e o relatório pode contar a nota duas vezes (likely, não bloqueante); (2) `Validar()` não impede Estado/Relatório dentro de Processados/Erro, que a limpeza apagaria (possible, não bloqueante).

**Verdict**: APPROVED

## High-level view

O fluxo é uma máquina de estados (Ocioso, Coletando, Processando, Fechando) executada por ciclos determinísticos, o que mantém o Worker fino e os testes controlando o relógio. Cada nota é registrada no diário e só então movida, o que limita o reenvio após crash a uma nota, apoiado na idempotência do WMS ("já existe" vai para Processados).

A limpeza é restrita a Processados e Erro: recusa configuração perigosa (raiz de drive, Entrada igual ou contida), ignora reparse points e nunca remove as raízes nem a Entrada. Exceção em uma nota vira registro de erro `Outro` e não derruba o resto da onda nem do pacote.

E-mail desligado por padrão; ausência de configuração devolve `NaoConfigurado` sem falhar, e falha de SMTP nunca impede o fechamento. A senha só vem de env ou de `email.settings.json`, ignorado pelo git; o repositório rastreia apenas o `.example` com senha vazia. A I60 permanece desligada.

<details>
<summary>Issues (4)</summary>

1. **Reenvio e duplicidade na retomada (likely)**: se o processo cair entre registrar e mover, ou com move pendente (`_movePendentes` só em memória), o arquivo é reintegrado após reinício e gera um segundo registro para o mesmo arquivo; `Retomar` só deduplica dentro do diário. Deduplicar por arquivo ao fechar (ou ao anexar registros) e manter o aviso de que a garantia depende do WMS barrar a duplicata.
2. **Estado/Relatório dentro de Processados/Erro (possible)**: `OpcoesPasta.Validar()` não checa `Estado`; configurado dentro de Processados/Erro, o diário seria apagado pela limpeza. Adicionar a verificação.
3. **Comentário desatualizado em Program.cs (confirmed, cosmético)**: o comentário do Orquestrador diz "Worker.cs inalterado"; agora o Worker o aciona. Atualizar.
4. **Sem teste de Worker/Program (confirmed, já documentado)**: fiação de DI e loop não são cobertos; o risco é de configuração, não de lógica.

</details>

<details>
<summary>Details</summary>

## Reenvio na retomada após queda

O diário é gravado antes do move, e a retomada recarrega os registros e mantém a primeira linha por arquivo. Se a queda ocorrer entre as duas etapas, o XML continua na entrada e é processado de novo: o WMS responde "já existe", a nota vai para Processados com prefixo "Já existia no WMS", mas o novo registro é somado em `_registros` ao do diário. O mesmo vale para `_movePendentes`, que se perde no reinício. O envio duplicado é, portanto, contido pelo WMS e não pelo serviço, e o relatório pode mostrar a nota duas vezes (uma integrada, outra "já existia"). Não corrompe dados nem bloqueia; a janela é de uma nota.

## Limpeza restrita a Processados e Erro

`LimpadorPastas` valida a configuração antes de agir, usa `LastWriteTimeUtc` (carimbado pelo Organizador no move, então a idade conta desde a chegada) e confere `EstaDentro` por arquivo contra a raiz normalizada. A lacuna é `Estado`: `Validar()` não impede que ele (ou a pasta de relatórios) fique dentro de Processados/Erro por configuração absoluta; nesse caso o diário de retomada seria removido após a retenção. Com os padrões (Estado em Entrada\Estado) não ocorre.

## Isolamento de falhas e e-mail

Cada nota é integrada em try/catch próprio; `DiarioPacote.Registrar` e `OrganizadorArquivos.Mover` não lançam, portanto nenhum caminho de `Finalizar` derruba a onda. `CicloAsync` ainda é protegido pelo catch do Worker. No fechamento, falha ao gravar o relatório devolve falso e preserva o diário para nova tentativa. Falha de SMTP é logada e o pacote fecha (fail-open para o e-mail; o PDF permanece em disco). Pacote sem registros não gera relatório.

</details>

<details>
<summary>Mapa de arquivos</summary>

Core/Configuracao: OpcoesPasta, OpcoesPacote, OpcoesLimpeza, OpcoesEmail (novos). Core/Pacote: detector, diário, gerenciador, processador, organizador, limpador, fechador, classificação de categoria (novos). Core/Email: contrato, SMTP, política, corpo (novos). Core/Orquestrador/ClassificadorRetorno.cs: 3 overloads aditivos. Worker: Worker.cs (loop contínuo), Program.cs (DI), appsettings.json (seções novas, Email desligado), email.settings.example.json. .gitignore: email.settings.json. docs/ARQUITETURA.md e OPERACAO.md. 13 arquivos de teste novos. Diff completo: `git diff 5fa83d1 HEAD`.

</details>
