namespace KitWmsIntegrador.Core.Pacote;

/// <summary>Tipo da nota na entrada: define como as ondas são montadas.</summary>
public enum TipoNota
{
    /// <summary>Puxada normal: uma onda por número de transporte.</summary>
    Puxada,

    /// <summary>Puxada TSP: uma onda por ZIP.</summary>
    Tsp,
}

/// <summary>Rótulos exibidos (relatório, e-mail, diário) para <see cref="TipoNota"/>.</summary>
public static class TipoNotaExtensoes
{
    /// <summary>Rótulo do tipo PUXADA.</summary>
    public const string RotuloPuxada = "PUXADA";

    /// <summary>Rótulo do tipo TSP.</summary>
    public const string RotuloTsp = "TSP";

    /// <summary>Rótulo da origem "raiz" (arquivo solto direto na pasta de entrada).</summary>
    public const string OrigemRaiz = "raiz";

    /// <summary>"PUXADA" ou "TSP".</summary>
    public static string Rotulo(this TipoNota tipo) => tipo == TipoNota.Tsp ? RotuloTsp : RotuloPuxada;
}

/// <summary>Arquivo candidato da entrada, com o tipo e a origem decididos só pela pasta onde está.</summary>
/// <param name="Caminho">Caminho completo do arquivo.</param>
/// <param name="Tipo">PUXADA (Puxada e raiz) ou TSP (PuxadaTSP).</param>
/// <param name="Origem">"raiz" ou o nome da pasta configurada (Puxada / PuxadaTSP).</param>
public sealed record EntradaArquivo(string Caminho, TipoNota Tipo, string Origem)
{
    /// <summary>Verdadeiro se o arquivo é um ZIP (extensão .zip, sem diferenciar maiúsculas).</summary>
    public bool EhZip => string.Equals(Path.GetExtension(Caminho), ".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>Verdadeiro se o arquivo é um XML (extensão .xml, sem diferenciar maiúsculas).</summary>
    public bool EhXml => string.Equals(Path.GetExtension(Caminho), ".xml", StringComparison.OrdinalIgnoreCase);
}
