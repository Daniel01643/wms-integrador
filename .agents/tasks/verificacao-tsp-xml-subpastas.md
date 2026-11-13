# Verificação: TSP com XML solto e ZIP com subpastas

- `dotnet build c:\wms-integrador\KitWmsIntegrador.sln -c Release`: 0 avisos, 0 erros.
- `dotnet test c:\wms-integrador\KitWmsIntegrador.sln -c Release`: 851 aprovados, 0 falhas, 9 ignorados (baseline 839 aprovados, 9 ignorados; +12 testes novos).
- `dotnet test ... --filter "FullyQualifiedName~I51|FullyQualifiedName~I60|FullyQualifiedName~FluxoI51"`: 169 aprovados, 0 falhas (inclui `FluxoI51I60Tests`, a TSP passa por eles).
- Manuais regenerados com `docs\manuais\gerar-manuais.ps1` e PDFs com Edge headless (Usuário e Técnico).
- Deck `apresentacao.html` não alterado: não afirma a regra "PuxadaTSP só ZIP".

Testes novos: ClassificarPastasTests (XML solto aceito), LeitorZipSeguroTests (subpastas aninhadas, mesmo nome em subpastas, traversal em subpasta, ZIP aninhado em subpasta profunda), PacoteTspEndToEndTests (XML solto, dois XMLs = mesma onda, XML + ZIP = ondas 01/02, reprocesso = novo sequencial, ZIP com subpastas e relatório com caminho, Raiz/Puxada inalterados).
