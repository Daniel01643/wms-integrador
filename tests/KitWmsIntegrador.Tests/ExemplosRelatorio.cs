using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Dados realistas para o relatório e gerador dos exemplos em <c>docs/exemplos</c>.
/// O teste só grava arquivos quando <c>GERAR_EXEMPLOS=1</c>:
/// <c>$env:GERAR_EXEMPLOS='1'; dotnet test KitWmsIntegrador.sln --filter GerarExemplos</c>.
/// </summary>
public class ExemplosRelatorio
{
    public static readonly DateTimeOffset GeradoEm = new(2026, 11, 4, 9, 15, 42, TimeSpan.FromHours(-3));

    public const string MensagemLonga =
        "Produto Cod\\EAN: \"175886\" da empresa \"3509308\" não ativo ou faltando ou não cadastrado! " +
        "Verifique o cadastro do produto no WMS, a situação (ativo/inativo), o código do depositante e o EAN " +
        "informado na nota fiscal. Caso o produto tenha sido criado por contenção (I30), revise a descrição, " +
        "a unidade de medida, o NCM, o peso e as dimensões antes de liberar a separação. Detalhes adicionais " +
        "retornados pelo WMS: estrutura de embalagem ausente, fator de conversão não informado, " +
        "classificação de armazenagem não definida e shelf-life zerado para o depositante informado.";

    public static IReadOnlyList<LinhaRelatorioErro> Linhas()
    {
        DateTimeOffset H(int h, int m, int s) => new(2026, 11, 4, h, m, s, TimeSpan.FromHours(-3));
        var hist = new[]
        {
            "I50 (tentativa 1): sucesso=N Destinataria - Empresa não localizada, sigla não informada.",
            "I10 (empresa): sucesso=S Registrado com sucesso.",
            "I50 (tentativa 2): sucesso=N Produto Cod\\EAN: \"175886\" da empresa \"3509308\" não ativo ou faltando ou não cadastrado!",
            "I30 (produto provisório — requer revisão): sucesso=S Registrado com sucesso.",
            "I50 (tentativa 3): sucesso=S Registrado com sucesso.",
        };

        return new[]
        {
            new LinhaRelatorioErro("15665807", "30288", H(9, 2, 11), "Registrado com sucesso.",
                Integrada: true, Tentativas: 1, IdTransacao: "4019964", IdDocumento: "1427353",
                Status: StatusLinhaRelatorio.Integrada,
                Historico: new[] { "I50 (tentativa 1): sucesso=S Registrado com sucesso." }),
            new LinhaRelatorioErro("15665808", "30288", H(9, 2, 14),
                "Registrado com sucesso.", Integrada: true, Tentativas: 3,
                IdTransacao: "4019971", IdDocumento: "1427360",
                Status: StatusLinhaRelatorio.Pendente,
                ProdutosProvisorios: new[] { "00175886", "00175887", "00175890" }, Historico: hist),
            new LinhaRelatorioErro("15665809", "30288", H(9, 2, 19),
                "Foi importado e esta pendente na interface.", Integrada: false, Tentativas: 1,
                IdTransacao: "4019975", IdDocumento: "1427364", Status: StatusLinhaRelatorio.Pendente,
                Historico: new[] { "I50 (tentativa 1): sucesso=N Foi importado e esta pendente na interface." }),
            new LinhaRelatorioErro("15665810", "30288", H(9, 2, 26),
                "Mensagem EDI com estrutura mal-formada. Processo Abortado.", Integrada: false, Tentativas: 1,
                IdTransacao: "4019980", Status: StatusLinhaRelatorio.NaoIntegrada,
                Historico: new[] { "I50 (tentativa 1): sucesso=N Mensagem EDI com estrutura mal-formada. Processo Abortado." }),
            new LinhaRelatorioErro("15665811", "30288", H(9, 2, 31), "Registrado com sucesso.",
                Integrada: true, Tentativas: 2, IdTransacao: "4019984", IdDocumento: "1427371",
                Status: StatusLinhaRelatorio.Integrada,
                Historico: new[]
                {
                    "I50 (tentativa 1): sucesso=N Destinataria - Empresa não localizada, sigla não informada.",
                    "I10 (empresa): sucesso=S Registrado com sucesso.",
                    "I50 (tentativa 2): sucesso=S Registrado com sucesso.",
                }),

            new LinhaRelatorioErro("15665820", "30291", H(9, 5, 3), "Registrado com sucesso.",
                Integrada: true, Tentativas: 1, IdTransacao: "4020012", IdDocumento: "1427402",
                Status: StatusLinhaRelatorio.Integrada),
            new LinhaRelatorioErro("15665821", "30291", H(9, 5, 40), MensagemLonga,
                Integrada: false, Tentativas: 3, IdTransacao: "4020019", IdDocumento: "",
                Status: StatusLinhaRelatorio.NaoIntegrada,
                ProdutosProvisorios: new[] { "00175886", "00175887" },
                Historico: new[]
                {
                    "I50 (tentativa 1): sucesso=N " + MensagemLonga,
                    "I30 (produto provisório — requer revisão): sucesso=N Registro já existe no cadastro.",
                    "I50 (tentativa 2): sucesso=N " + MensagemLonga,
                    "I30 (produto provisório — requer revisão): sucesso=N Registro já existe no cadastro.",
                    "I50 (tentativa 3): sucesso=N " + MensagemLonga,
                    "Esgotadas 3 tentativas sem sucesso.",
                }),
            new LinhaRelatorioErro("15665822", "30291", H(9, 6, 2),
                "Destinataria - Empresa não localizada, sigla não informada.", Integrada: false, Tentativas: 3,
                IdTransacao: "4020027", Status: StatusLinhaRelatorio.NaoIntegrada,
                Historico: new[]
                {
                    "I50 (tentativa 1): sucesso=N Destinataria - Empresa não localizada, sigla não informada.",
                    "I10 (empresa): sucesso=N Falha ao cadastrar empresa.",
                    "I50 (tentativa 2): sucesso=N Destinataria - Empresa não localizada, sigla não informada.",
                    "I10 (empresa): sucesso=N Falha ao cadastrar empresa.",
                    "I50 (tentativa 3): sucesso=N Destinataria - Empresa não localizada, sigla não informada.",
                    "Esgotadas 3 tentativas sem sucesso.",
                }),
            new LinhaRelatorioErro("15665823", "30291", H(9, 6, 20), "Registrado com sucesso.",
                Integrada: true, Tentativas: 1, IdTransacao: "4020031", IdDocumento: "1427415",
                Status: StatusLinhaRelatorio.Integrada),

            new LinhaRelatorioErro("15665830", "30305", H(9, 9, 8), "Registrado com sucesso.",
                Integrada: true, Tentativas: 2, IdTransacao: "4020055", IdDocumento: "1427440",
                Status: StatusLinhaRelatorio.Pendente,
                ProdutosProvisorios: new[] { "00201234" },
                Historico: new[]
                {
                    "I50 (tentativa 1): sucesso=N Produto Cod\\EAN: \"201234\" da empresa \"3509308\" não ativo ou faltando ou não cadastrado!",
                    "I30 (produto provisório — requer revisão): sucesso=S Registrado com sucesso.",
                    "I50 (tentativa 2): sucesso=S Registrado com sucesso.",
                }),
            new LinhaRelatorioErro("15665831", "30305", H(9, 9, 15),
                "Foi importado e esta pendente na interface.", Integrada: false, Tentativas: 1,
                IdTransacao: "4020058", IdDocumento: "1427443", Status: StatusLinhaRelatorio.Pendente),
            new LinhaRelatorioErro("15665832", "30305", H(9, 9, 22), "Registrado com sucesso.",
                Integrada: true, Tentativas: 1, IdTransacao: "4020061", IdDocumento: "1427446",
                Status: StatusLinhaRelatorio.Integrada),
        };
    }

    [Fact]
    public void GerarExemplos()
    {
        if (Environment.GetEnvironmentVariable("GERAR_EXEMPLOS") != "1")
        {
            return;
        }

        var raiz = AppContext.BaseDirectory;
        while (raiz is not null && !File.Exists(Path.Combine(raiz, "KitWmsIntegrador.sln")))
        {
            raiz = Path.GetDirectoryName(raiz);
        }

        Assert.NotNull(raiz);
        var pasta = Path.Combine(raiz!, "docs", "exemplos");
        Directory.CreateDirectory(pasta);

        var gerador = new GeradorRelatorio(Options.Create(new OpcoesRelatorio { PastaSaida = pasta }));
        var linhas = Linhas();
        File.WriteAllText(
            Path.Combine(pasta, "relatorio-erros-exemplo.html"),
            gerador.GerarHtml(linhas, GeradoEm), new System.Text.UTF8Encoding(true));
        File.WriteAllBytes(Path.Combine(pasta, "relatorio-erros-exemplo.pdf"), gerador.GerarPdf(linhas, GeradoEm));
    }
}
