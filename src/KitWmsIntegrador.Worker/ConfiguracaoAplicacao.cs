using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace KitWmsIntegrador.Worker;

/// <summary>
/// Carregador ÚNICO de configuração: o serviço (Program.cs) e todas as CLIs manuais (--testar-token,
/// --smoke-wms, --enviar-i60, --enviar-i40, --consultar-i51, --relatorio-i60, --reenfileirar-i60) usam este
/// mesmo caminho, então enxergam exatamente as mesmas fontes na mesma ordem.
/// Ordem (a última vence), a do <c>Host.CreateApplicationBuilder</c> com ambiente Production e raiz de conteúdo
/// = pasta do executável: appsettings.json, appsettings.{Ambiente}.json (Production por padrão; o ambiente vem de
/// DOTNET_ENVIRONMENT/ASPNETCORE_ENVIRONMENT), variáveis de ambiente, argumentos de linha de comando e, por último,
/// email.settings.json (opcional, ao lado do exe). Não existe leitura de <c>config\</c>.
/// </summary>
public static class ConfiguracaoAplicacao
{
    /// <summary>Arquivo opcional com o SMTP (ignorado pelo git), lido depois das demais fontes.</summary>
    public const string ArquivoEmail = "email.settings.json";

    /// <summary>Configurações do host: argumentos e raiz de conteúdo (padrão: pasta do executável, não o diretório atual).</summary>
    public static HostApplicationBuilderSettings CriarSettings(string[] args, string? raizConteudo = null, string? ambiente = null)
    {
        var settings = new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = raizConteudo ?? AppContext.BaseDirectory,
        };
        if (!string.IsNullOrWhiteSpace(ambiente))
        {
            settings.EnvironmentName = ambiente;
        }

        return settings;
    }

    /// <summary>Acrescenta as fontes extras do serviço (email.settings.json, sempre por último).</summary>
    public static void AdicionarFontesExtras(IConfigurationBuilder configuracao, bool recarregar) =>
        configuracao.AddJsonFile(ArquivoEmail, optional: true, reloadOnChange: recarregar);

    /// <summary>Configuração efetiva para as CLIs: idêntica à do serviço (sem subir host nem serviços).</summary>
    public static IConfiguration Criar(string[] args, string? raizConteudo = null, string? ambiente = null)
    {
        var builder = Host.CreateApplicationBuilder(CriarSettings(args, raizConteudo, ambiente));
        AdicionarFontesExtras(builder.Configuration, recarregar: false);
        return builder.Configuration;
    }
}
