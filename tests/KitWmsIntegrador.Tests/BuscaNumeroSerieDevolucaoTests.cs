using KitWmsIntegrador.Core.Devolucao;
using KitWmsIntegrador.Tests.Infra;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Busca por número + série (sem abrir XML): número, série e CNPJ saem do nome do arquivo.
/// Os arquivos são criados vazios em pasta temporária; nada de XML de cliente é versionado.
/// </summary>
public sealed class BuscaNumeroSerieDevolucaoTests : IDisposable
{
    private static readonly string Cnpj = AmbienteDevolucao.CnpjDepositanteDigitos;
    private static readonly DateTime Base = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly AmbienteDevolucao _amb = new();

    public void Dispose() => _amb.Dispose();

    private LocalizadorXmlDevolucao Localizador() => new(_amb.Processados);

    [Fact]
    public void UmaNota_DevolveUmaOpcaoComNumeroSerieEPacote()
    {
        var chave = AmbienteDevolucao.ChaveDe(Cnpj, 6, 15671289);
        _amb.CriarArquivoDaChave(chave, "pacote_20261101_100000", Base);

        var r = Localizador().BuscarPorNumeroSerie(15671289, 6, Cnpj);

        var o = Assert.Single(r);
        Assert.Equal(chave, o.Chave);
        Assert.Equal("15671289", o.Numero);
        Assert.Equal("6", o.Serie);
        Assert.Equal("pacote_20261101_100000", o.Pacote);
        Assert.Equal(Base.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), o.DataArquivo);
    }

    [Fact]
    public void ChaveHomologDoRoteiro_EhEncontradaPorNumeroESerie6()
    {
        // 15671289 / série 6 (nota candidata do homolog).
        var chave = "35251201838723009850550060156712891146443917";
        Assert.Null(LocalizadorXmlDevolucao.ValidarChave(chave));
        _amb.CriarArquivoDaChave(chave, "pacote_20261101_100000", Base);

        var o = Assert.Single(Localizador().BuscarPorNumeroSerie(15671289, 6, "01.838.723/0098-50"));

        Assert.Equal(chave, o.Chave);
    }

    [Fact]
    public void ZerosAEsquerdaNaoContam_NumeroESerieSaoInteiros()
    {
        var chave = AmbienteDevolucao.ChaveDe(Cnpj, 0, 42);
        _amb.CriarArquivoDaChave(chave, "pacote_a", Base);

        var r = Localizador().BuscarPorNumeroSerie(42, 0, Cnpj);

        Assert.Equal(chave, Assert.Single(r).Chave);
        Assert.Equal("42", r[0].Numero);
        Assert.Equal("0", r[0].Serie);
    }

    [Fact]
    public void VariasChavesDistintas_VoltamTodas_DaMaisRecenteParaAMaisAntiga()
    {
        var antiga = AmbienteDevolucao.ChaveDe(Cnpj, 6, 500, variacao: 1);
        var nova = AmbienteDevolucao.ChaveDe(Cnpj, 6, 500, variacao: 2);
        _amb.CriarArquivoDaChave(antiga, "pacote_a", Base);
        _amb.CriarArquivoDaChave(nova, "pacote_b", Base.AddDays(3));

        var r = Localizador().BuscarPorNumeroSerie(500, 6, Cnpj);

        Assert.Equal(new[] { nova, antiga }, r.Select(o => o.Chave).ToArray());
        Assert.Equal(new[] { "pacote_b", "pacote_a" }, r.Select(o => o.Pacote).ToArray());
    }

    [Fact]
    public void Nenhuma_DevolveListaVazia()
    {
        _amb.CriarArquivoDaChave(AmbienteDevolucao.ChaveDe(Cnpj, 6, 501), "pacote_a", Base);

        Assert.Empty(Localizador().BuscarPorNumeroSerie(999, 6, Cnpj));
    }

    [Fact]
    public void MesmoNumeroEOutraSerie_NaoCasa()
    {
        _amb.CriarArquivoDaChave(AmbienteDevolucao.ChaveDe(Cnpj, 7, 500), "pacote_a", Base);

        Assert.Empty(Localizador().BuscarPorNumeroSerie(500, 6, Cnpj));
    }

    [Fact]
    public void OutroCnpjEmitente_EhIgnorado()
    {
        _amb.CriarArquivoDaChave(AmbienteDevolucao.ChaveDe(AmbienteDevolucao.CnpjOutroEmitente, 6, 500), "pacote_a", Base);

        Assert.Empty(Localizador().BuscarPorNumeroSerie(500, 6, Cnpj));
    }

    [Fact]
    public void CnpjDoDepositanteComPontuacao_EComparadoSoPelosDigitos()
    {
        var chave = AmbienteDevolucao.ChaveDe(Cnpj, 6, 500);
        _amb.CriarArquivoDaChave(chave, "pacote_a", Base);

        Assert.Single(Localizador().BuscarPorNumeroSerie(500, 6, AmbienteDevolucao.CnpjDepositante));
    }

    [Fact]
    public void PastaErro_NuncaEPesquisada()
    {
        var chave = AmbienteDevolucao.ChaveDe(Cnpj, 6, 500);
        var erro = Path.Combine(_amb.Raiz, "Erro", "PorCategoria");
        Directory.CreateDirectory(erro);
        File.WriteAllText(Path.Combine(erro, $"NFE{chave}.XML"), string.Empty);
        // Subpasta de Processados que não é pacote_* também não entra.
        _amb.CriarArquivoDaChave(chave, "outra_pasta", Base);

        Assert.Empty(Localizador().BuscarPorNumeroSerie(500, 6, Cnpj));
    }

    [Fact]
    public void MesmaChaveEmDoisPacotes_ValeUmaVez_UsandoOMaisRecente()
    {
        var chave = AmbienteDevolucao.ChaveDe(Cnpj, 6, 500);
        _amb.CriarArquivoDaChave(chave, "pacote_velho", Base);
        _amb.CriarArquivoDaChave(chave, "pacote_novo", Base.AddDays(5));

        var o = Assert.Single(Localizador().BuscarPorNumeroSerie(500, 6, Cnpj));

        Assert.Equal("pacote_novo", o.Pacote);
    }

    [Fact]
    public void ArquivoNaRaizDeProcessados_ContaEMostraProcessadosComoPacote()
    {
        var chave = AmbienteDevolucao.ChaveDe(Cnpj, 6, 500);
        _amb.CriarArquivoDaChave(chave, string.Empty, Base);

        var o = Assert.Single(Localizador().BuscarPorNumeroSerie(500, 6, Cnpj));

        Assert.Equal("Processados", o.Pacote);
    }

    [Fact]
    public void NomeEmMinusculas_EAceito()
    {
        var chave = AmbienteDevolucao.ChaveDe(Cnpj, 6, 500);
        _amb.CriarArquivoDaChave(chave, "pacote_a", Base, nomeArquivo: $"nfe{chave}.xml");

        Assert.Single(Localizador().BuscarPorNumeroSerie(500, 6, Cnpj));
    }

    [Fact]
    public void ArquivosQueNaoSaoNfeValida_SaoIgnorados()
    {
        var chave = AmbienteDevolucao.ChaveDe(Cnpj, 6, 500);
        var dvErrado = chave[..43] + (chave[43] == '0' ? '1' : '0');
        _amb.CriarArquivoDaChave(chave, "pacote_a", Base, nomeArquivo: $"NFE{chave}.txt");
        _amb.CriarArquivoDaChave(chave, "pacote_a", Base, nomeArquivo: $"{chave}.XML");
        _amb.CriarArquivoDaChave(chave, "pacote_a", Base, nomeArquivo: $"NFE{dvErrado}.XML");
        _amb.CriarArquivoDaChave(chave, "pacote_a", Base, nomeArquivo: "NFE123.XML");

        Assert.Empty(Localizador().BuscarPorNumeroSerie(500, 6, Cnpj));
    }

    [Fact]
    public void PastaProcessadosInexistente_DevolveVazio()
    {
        var r = new LocalizadorXmlDevolucao(Path.Combine(_amb.Raiz, "nao-existe")).BuscarPorNumeroSerie(500, 6, Cnpj);

        Assert.Empty(r);
    }

    [Fact]
    public void CnpjInvalido_Lanca()
    {
        Assert.Throws<ArgumentException>(() => Localizador().BuscarPorNumeroSerie(500, 6, "123"));
    }

    // ---- Servico.Buscar: validação das entradas e configuração ----

    [Fact]
    public void Servico_Buscar_ResolveUmaChave_ComCnpjDaConfiguracao()
    {
        var chave = AmbienteDevolucao.ChaveDe(Cnpj, 6, 500);
        _amb.CriarArquivoDaChave(chave, "pacote_a", Base);

        var r = _amb.Servico.Buscar("0500", "006");

        Assert.Equal("500", r.Numero);
        Assert.Equal("6", r.Serie);
        Assert.Equal(chave, Assert.Single(r.Opcoes).Chave);
    }

    [Theory]
    [InlineData(null, "6")]
    [InlineData("", "6")]
    [InlineData("12a", "6")]
    [InlineData("1234567890", "6")]
    [InlineData("500", "")]
    [InlineData("500", "1234")]
    [InlineData("500", "-1")]
    public void Servico_Buscar_EntradaInvalida_LancaSolicitacaoInvalida(string? numero, string? serie)
    {
        Assert.Throws<SolicitacaoDevolucaoInvalidaException>(() => _amb.Servico.Buscar(numero, serie));
    }

    [Fact]
    public void Servico_Buscar_SemCnpjDoDepositante_LancaConfiguracao()
    {
        _amb.Operacional.CnpjDepositante = string.Empty;

        Assert.Throws<ConfiguracaoDevolucaoException>(() => _amb.Servico.Buscar("500", "6"));
    }

    [Fact]
    public void Servico_Buscar_SemPastaEntrada_LancaConfiguracao()
    {
        _amb.Pastas.Entrada = string.Empty;

        Assert.Throws<ConfiguracaoDevolucaoException>(() => _amb.Servico.Buscar("500", "6"));
    }

    [Fact]
    public void Servico_Buscar_NaoEncontrada_DevolveOpcoesVazias()
    {
        var r = _amb.Servico.Buscar("500", "6");

        Assert.Empty(r.Opcoes);
    }
}