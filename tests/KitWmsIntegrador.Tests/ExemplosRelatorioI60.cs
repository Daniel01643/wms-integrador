using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Core.Separacao;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Dados FICTÍCIOS do relatório diário da I51/I60 (sem cliente, sem XML e sem número real) e gerador da evidência em
/// <c>docs/exemplos</c>. Só grava com <c>GERAR_EXEMPLOS=1</c>:
/// <c>$env:GERAR_EXEMPLOS='1'; dotnet test KitWmsIntegrador.sln --filter GerarExemploRelatorioI60</c>.
/// </summary>
public class ExemplosRelatorioI60
{
    private static readonly TimeSpan Fuso = TimeSpan.FromHours(-3);

    /// <summary>Instante fixo dos exemplos: 12/11/2026 12:00 (horário de Brasília).</summary>
    public static readonly DateTimeOffset Agora = new(2026, 11, 12, 12, 0, 0, Fuso);

    private static ItemSeparacao Item(int seq, string cod, decimal orig, decimal? sep) => new()
    {
        Sequencia = seq,
        CodProduto = cod,
        QtdeOriginal = orig,
        QtdeSeparada = sep,
        Diferenca = (sep ?? 0m) - orig,
        SemRetorno = sep is null,
    };

    private static NotaSeparacao Nota(
        int n, SituacaoNota s, string tipo, string pacote, string onda, TimeSpan idade,
        Func<NotaSeparacao, NotaSeparacao>? ajuste = null)
    {
        var registrada = Agora - idade;
        var nota = new NotaSeparacao
        {
            Chave = "EXEMPLO-" + n,
            Numero = n.ToString(),
            Serie = "6",
            Tipo = tipo,
            Onda = onda,
            NumeroTransporte = "10000" + (n % 7).ToString("D5"),
            IdentificadorPacote = pacote,
            Situacao = s,
            RegistradaEm = registrada,
            AtualizadoEm = Agora,
            Itens = new[] { Item(1, "PROD-A", 24, 24), Item(2, "PROD-B", 12, 12) },
        };
        return ajuste is null ? nota : ajuste(nota);
    }

    /// <summary>Notas fictícias cobrindo todas as seções do relatório.</summary>
    public static IReadOnlyList<NotaSeparacao> NotasFicticias()
    {
        var h = (double d) => TimeSpan.FromHours(d);
        return new[]
        {
            Nota(5001, SituacaoNota.AguardandoSeparacao, "PUXADA", "pacote_20261112_080000", string.Empty, h(3)),
            Nota(5002, SituacaoNota.AguardandoSeparacao, "PUXADA", "pacote_20261112_080000", string.Empty, h(5)),
            Nota(5003, SituacaoNota.AguardandoSeparacao, "TSP", "pacote_20261112_090000", "TSP-20261112-01-SJC", h(2)),
            Nota(5004, SituacaoNota.Atrasada, "PUXADA", "pacote_20261110_080000", string.Empty, h(53), n => n with { AtrasadaEm = Agora - h(29), TentativasUnico = 3 }),
            Nota(5005, SituacaoNota.Separada, "PUXADA", "pacote_20261111_150000", string.Empty, h(20), n => n with
            {
                SeparadaEm = Agora - h(1),
                FalhasComunicacao = 2,
                Itens = new[] { Item(1, "PROD-A", 24, 24), Item(2, "PROD-B", 12, 12) },
            }),
            Nota(5006, SituacaoNota.Associada, "PUXADA", "pacote_20261111_150000", string.Empty, h(19), n => n with
            {
                SeparadaEm = Agora - h(6),
                I60EnviadaEm = Agora - h(5),
                FinalizadaEm = Agora - h(5),
                OperacaoI60 = "INE",
                Itens = new[] { Item(1, "PROD-A", 24, 20), Item(2, "PROD-B", 12, 12) },
            }),
            Nota(5007, SituacaoNota.Associada, "TSP", "pacote_20261111_170000", "TSP-20261111-02-SOROCABA", h(17), n => n with
            {
                SeparadaEm = Agora - h(4),
                I60EnviadaEm = Agora - h(3),
                FinalizadaEm = Agora - h(3),
                OperacaoI60 = "INE",
            }),
            Nota(5008, SituacaoNota.I60Enviada, "PUXADA", "pacote_20261111_150000", string.Empty, h(18), n => n with
            {
                SeparadaEm = Agora - h(7),
                I60EnviadaEm = Agora - h(6),
                FinalizadaEm = Agora - h(6),
                OperacaoI60 = "INE",
                Motivo = MotivoI60.PendenteNaInterface,
                Mensagem = "Foi importado e esta pendente na interface.",
            }),
            Nota(5009, SituacaoNota.I60Enviada, "PUXADA", "pacote_20261111_150000", string.Empty, h(18), n => n with
            {
                SeparadaEm = Agora - h(8),
                I60EnviadaEm = Agora - h(7),
                FinalizadaEm = Agora - h(7),
                OperacaoI60 = "IMP",
                TpAcaoI60 = "UPD",
                Motivo = MotivoI60.DocCargaEncerrado,
                Mensagem = "Doccarga encerrado (ENC): I60 enviada como IMP (tpacao=UPD), sem associação automática.",
                Itens = new[] { Item(1, "PROD-A", 24, 18), Item(2, "PROD-B", 12, 12) },
            }),
            Nota(5010, SituacaoNota.Rejeitada, "PUXADA", "pacote_20261111_150000", string.Empty, h(18), n => n with
            {
                SeparadaEm = Agora - h(9),
                FinalizadaEm = Agora - h(8),
                OperacaoI60 = "INE",
                Motivo = MotivoI60.QuantidadeDiferente,
                Mensagem = "Quantidade caixa da nota 36 não confere com a quantidade separada 30.",
                Ressalvas = new[] { "Quantidade divergente no item PROD-A" },
            }),
            Nota(5011, SituacaoNota.Rejeitada, "TSP", "pacote_20261111_170000", "TSP-20261111-02-SOROCABA", h(17), n => n with
            {
                SeparadaEm = Agora - h(5),
                FinalizadaEm = Agora - h(4),
                OperacaoI60 = "INE",
                Motivo = MotivoI60.JaExiste,
                Mensagem = "Nota já existe no WMS e o reenvio UPD não resolveu.",
            }),
        };
    }

    /// <summary>Modelo do relatório de exemplo.</summary>
    public static ModeloRelatorioI60 ModeloFicticio(bool devolucaoHabilitada = false, int maxLinhas = 300) =>
        MontadorRelatorioI60.Montar(
            new EntradaRelatorioI60(
                NotasFicticias(),
                new SaudeConsultaI51 { UltimaConsultaOkEm = Agora.AddMinutes(-4), FalhasConsecutivas = 0 },
                BrutosFalhos: 0, OrfaosNaJanela: 1, Agora, Agora.AddHours(-24), devolucaoHabilitada),
            new OpcoesRelatorioI60 { MaxLinhasPorSecao = maxLinhas },
            new OpcoesI60());

    [Fact]
    public void GerarExemploRelatorioI60()
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

        var gerador = new GeradorRelatorioI60(Options.Create(new OpcoesRelatorio()));
        var modelo = ModeloFicticio();
        File.WriteAllText(
            Path.Combine(pasta, "relatorio-i60-exemplo.html"),
            gerador.GerarHtml(modelo, ModoLogoRelatorio.DataUri).Html, new System.Text.UTF8Encoding(true));
        File.WriteAllBytes(Path.Combine(pasta, "relatorio-i60-exemplo.pdf"), gerador.GerarPdf(modelo));
    }
}
