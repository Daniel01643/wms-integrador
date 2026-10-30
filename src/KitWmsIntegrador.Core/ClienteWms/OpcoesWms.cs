namespace KitWmsIntegrador.Core.ClienteWms;

/// <summary>
/// Opções de configuração do cliente WMS. Configurável via <c>appsettings.json</c>
/// (seção "Wms") e variáveis de ambiente (ex.: <c>Wms__BaseUrl</c>).
///
/// SEGREDOS: <see cref="Usuario"/> e <see cref="Senha"/> (Basic Auth do GetToken)
/// NUNCA devem ser gravados no <c>appsettings.json</c> nem no código. Vêm apenas de
/// variável de ambiente (<c>WMS__Usuario</c> / <c>WMS__Senha</c>) ou user-secrets.
/// </summary>
public sealed class OpcoesWms
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string Secao = "Wms";

    /// <summary>
    /// URL base da API (até <c>/api</c>, sem barra final), ex.:
    /// <c>https://friozem.assistsoftware.com.br:8888/wmsapi/api</c>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Parâmetro <c>REQUISITANTE</c> da query (homolog: <c>02.559.193/0001-40</c>).</summary>
    public string Requisitante { get; set; } = string.Empty;

    /// <summary>Parâmetro <c>DESTINO</c> da query (homolog: <c>14.605.065/0001-75</c>).</summary>
    public string Destino { get; set; } = string.Empty;

    /// <summary>
    /// Parâmetro <c>CNPJREQUISITANTE</c> da query. Presente na query mesmo vazio
    /// (a ausência causava HTTP 404). Configurável; vazio por ora.
    /// </summary>
    public string CnpjRequisitante { get; set; } = string.Empty;

    /// <summary>
    /// Parâmetro <c>CNPJDESTINO</c> da query. Presente na query mesmo vazio
    /// (a ausência causava HTTP 404). Configurável; vazio por ora.
    /// </summary>
    public string CnpjDestino { get; set; } = string.Empty;

    /// <summary>
    /// Usuário do Basic Auth do GetToken. SEGREDO — vem de <c>WMS__Usuario</c>.
    /// NUNCA preencher no appsettings/código. Usuário de homolog: <c>BLUEFOOD</c>.
    /// </summary>
    public string Usuario { get; set; } = string.Empty;

    /// <summary>
    /// Senha do Basic Auth do GetToken. SEGREDO — vem de <c>WMS__Senha</c>.
    /// NUNCA preencher no appsettings/código e NUNCA logar.
    /// </summary>
    public string Senha { get; set; } = string.Empty;

    /// <summary>Timeout por requisição, em segundos (default 30).</summary>
    public int TimeoutSegundos { get; set; } = 30;

    /// <summary>Número máximo de tentativas para erros transitórios (default 3).</summary>
    public int MaxTentativas { get; set; } = 3;

    /// <summary>
    /// Margem (em segundos) antes do <c>exp</c> do JWT para renovar o token
    /// proativamente (default 60).
    /// </summary>
    public int MargemRenovacaoTokenSegundos { get; set; } = 60;
}
