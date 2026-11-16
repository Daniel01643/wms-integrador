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

    /// <summary>
    /// Validade máxima do token em minutos, contada da obtenção (default 3). O token é renovado ao atingi-la mesmo
    /// que o <c>exp</c> do JWT seja maior; 0 desliga o limite (vale só o <c>exp</c>).
    /// </summary>
    public int ValidadeMaximaTokenMinutos { get; set; } = 3;

    /// <summary>
    /// Tentativas imediatas do GetToken por episódio de falha (default 2, backoff de 0,5 s). Esgotadas,
    /// entra o cooldown global (<see cref="CooldownTokenSegundos"/>) e nenhuma nota dispara outro GetToken.
    /// </summary>
    public int TokenTentativasImediatas { get; set; } = 2;

    /// <summary>
    /// Escada do cooldown global após falha do GetToken, em segundos, separada por ';'
    /// (default <c>60;120;300;600;900</c> = 1, 2, 5, 10 e 15 min). O último valor é o teto.
    /// </summary>
    public string CooldownTokenSegundos { get; set; } = "60;120;300;600;900";

    /// <summary>Envia e-mail de alerta na indisponibilidade e na recuperação da autenticação (default true).</summary>
    public bool AlertaAutenticacao { get; set; } = true;

    /// <summary>Escada do cooldown já interpretada (valores inválidos são ignorados; vazio = escada padrão).</summary>
    public IReadOnlyList<TimeSpan> ListaCooldown
    {
        get
        {
            var lista = (CooldownTokenSegundos ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0)
                .Where(v => v > 0)
                .Select(v => TimeSpan.FromSeconds(v))
                .ToList();
            return lista.Count > 0
                ? lista
                : new[] { 60, 120, 300, 600, 900 }.Select(v => TimeSpan.FromSeconds(v)).ToList();
        }
    }
}
