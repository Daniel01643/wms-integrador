namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>
/// Status base de uma linha do relatório. A variante "Integrada — revisar produto" não é
/// um quarto valor: é derivada de <see cref="Integrada"/> + produto provisório pendente.
/// </summary>
public enum StatusLinhaRelatorio
{
    /// <summary>Nota aceita pelo WMS.</summary>
    Integrada,

    /// <summary>Importada, porém pendente na interface do WMS.</summary>
    Pendente,

    /// <summary>Não integrada (erro não recuperável ou tentativas esgotadas).</summary>
    NaoIntegrada,

    /// <summary>Arquivo não-XML: ignorado pela integração (aviso, não é falha de WMS).</summary>
    ArquivoIgnorado,
}
