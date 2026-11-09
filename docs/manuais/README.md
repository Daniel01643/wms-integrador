# Manuais — KitWms Integrador

Documentação de uso e operação do KitWms Integrador, dividida por público. Cada manual é mantido
em **Markdown** (fonte) e, para os manuais de pessoas, também publicado em **HTML** e **PDF** com a
identidade visual Friozem (paleta azul `#17479E` / ciano `#54B6E7` / verde `#009A83`, logo e
cabeçalho da apresentação, `docs\apresentacao\apresentacao.html`).

Os HTML/PDF de pessoas (Usuário e Técnico) abrem com uma **capa ilustrada** (nome do agente
"Integrador Notas Fiscais KIT → WMS", subtítulo, tipo do manual, data de geração e versão) e trazem
**diagramas de fluxo em SVG inline** (sem Mermaid, sem recurso externo): um fluxo operacional no
Manual do Usuário e, no Manual Técnico, a arquitetura de implantação e o ciclo do pacote. Os fontes
dos diagramas ficam em [`assets\*.svg`](assets) e são injetados por marcadores
(`<!--CAPA:...-->` / `<!--DIAGRAMA:...-->`) no passo de geração; o HTML final continua
**autocontido** (SVG e logo embutidos, nada externo).

## Manuais e formatos

| Manual | Público-alvo | Formatos | Conteúdo |
|---|---|---|---|
| Manual do Usuário | Operador/colaborador do depósito (não técnico) | [MD](MANUAL-USUARIO.md) · [HTML](MANUAL-USUARIO.html) · [PDF](MANUAL-USUARIO.pdf) | Como depositar os XMLs no share `kitbrf`, o que o sistema faz, onde ver o resultado, como ler os status do relatório e o e-mail de fechamento, boas práticas e perguntas frequentes. **Não** cobre IA. |
| Manual Técnico (Nível 1) | Técnico humano nível 1 / júnior (acesso ao servidor por RDP ou direto na máquina) | [MD](MANUAL-TECNICO.md) · [HTML](MANUAL-TECNICO.html) · [PDF](MANUAL-TECNICO.pdf) | Iniciar/parar/reiniciar o serviço, ver logs, trocar segredos do WMS e SMTP, trocar a URL do WMS, gerenciar destinatários, ajustar resumo/limpeza, publicar/rollback, o que fazer quando o serviço não sobe e um guia rápido de como usar a IA de apoio. |
| Manual do Agente de IA | Agente de IA acionado para diagnosticar falhas ou propor alterações | [MD](MANUAL-AGENTE-IA.md) (apenas Markdown — consumo por IA; sem HTML/PDF) | Visão do fluxo, mapa de arquivos no servidor e no repositório, padrões de diagnóstico, como verificar mudanças e os guarda-rails de segurança. |

> O **Manual do Agente de IA** fica **somente em Markdown** porque é consumido por uma IA; não há
> versão HTML nem PDF dele.

## Como regenerar HTML e PDF

Os HTML e PDF dos manuais de pessoas (Usuário e Técnico) são **gerados** a partir dos `.md`; não
edite os `.html` à mão. A geração usa um script PowerShell autocontido (sem pandoc, sem internet,
sem dotnet) e o Microsoft Edge em modo headless.

1. **Gerar os HTML** a partir dos Markdown (extrai a logo base64 de `docs\apresentacao\apresentacao.html` e aplica o template
   Friozem):

   ```powershell
   powershell -ExecutionPolicy Bypass -File docs\manuais\gerar-manuais.ps1
   ```

2. **Gerar os PDF** com o Microsoft Edge headless (a numeração de página vem do CSS `@page`, por
   isso `--no-pdf-header-footer`):

   ```powershell
   & 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe' --headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf='C:\wms-integrador\docs\manuais\MANUAL-USUARIO.pdf' 'C:\wms-integrador\docs\manuais\MANUAL-USUARIO.html'
   & 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe' --headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf='C:\wms-integrador\docs\manuais\MANUAL-TECNICO.pdf' 'C:\wms-integrador\docs\manuais\MANUAL-TECNICO.html'
   ```

   Se o Edge não estiver disponível, o Chrome (x64) aceita os mesmos parâmetros:
   `'C:\Program Files\Google\Chrome\Application\chrome.exe'`.

### Plano B — Imprimir como PDF (manual)

Se o Edge/Chrome headless falhar ou gerar um PDF de 0 bytes, gere o PDF manualmente: abra o `.html`
no Edge ou no Chrome, use **Imprimir** (Ctrl+P), escolha **Salvar como PDF**, papel **A4**, margens
padrão e **desligue** "Cabeçalhos e rodapés" do navegador (a numeração de página já vem do CSS). Os
HTML são autocontidos (logo embutida, sem recursos externos), então imprimem igual em qualquer máquina.

Referências relacionadas: [`docs\OPERACAO.md`](../OPERACAO.md) e
[`docs\INSTALACAO-SERVIDOR.md`](../INSTALACAO-SERVIDOR.md).
