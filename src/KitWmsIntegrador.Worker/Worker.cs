using System.Globalization;
using KitWmsIntegrador.Core.Agrupamento;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Worker.Configuracao;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Worker;

/// <summary>
/// Worker de execução única (nesta fase): varre a pasta de entrada, agrupa as
/// notas por transporte e imprime um resumo. NÃO faz chamadas de rede e NÃO
/// move/altera arquivos (apenas leitura). O modo watcher contínuo é fase futura.
/// </summary>
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly VarredorPasta _varredor;
    private readonly AgrupadorTransporte _agrupador;
    private readonly OpcoesEntrada _opcoes;

    public Worker(
        ILogger<Worker> logger,
        IHostApplicationLifetime lifetime,
        VarredorPasta varredor,
        AgrupadorTransporte agrupador,
        IOptions<OpcoesEntrada> opcoes)
    {
        _logger = logger;
        _lifetime = lifetime;
        _varredor = varredor;
        _agrupador = agrupador;
        _opcoes = opcoes.Value;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var pasta = _opcoes.PastaEntrada;
            _logger.LogInformation("Varrendo pasta de entrada (somente leitura): {Pasta}", pasta);

            // A varredura é streaming; o agrupador consome uma vez e retém só o
            // modelo de domínio leve.
            var resultado = _agrupador.Agrupar(_varredor.Varrer(pasta));

            ImprimirResumo(resultado);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao varrer/agrupar a pasta de entrada.");
        }
        finally
        {
            _lifetime.StopApplication();
        }

        return Task.CompletedTask;
    }

    private void ImprimirResumo(ResultadoAgrupamento resultado)
    {
        var tamanhos = resultado.Ondas.Select(o => o.Notas.Count).ToList();

        _logger.LogInformation("===== RESUMO DA VARREDURA =====");
        _logger.LogInformation("Total de XMLs encontrados.......: {Total}", _varredor.ArquivosEncontrados);
        _logger.LogInformation("Total de notas lidas (parseadas): {Lidas}", resultado.TotalNotas);
        _logger.LogInformation("Erros de parse..................: {Erros}", _varredor.Erros.Count);
        _logger.LogInformation("Transportes/ondas distintos.....: {Ondas}", resultado.Ondas.Count);
        _logger.LogInformation("XMLs sem transporte (pendências): {SemTransporte}", resultado.SemTransporte.Count);

        if (tamanhos.Count > 0)
        {
            _logger.LogInformation(
                "Tamanho das ondas (notas/onda): min={Min} max={Max} média={Media}",
                tamanhos.Min(),
                tamanhos.Max(),
                tamanhos.Average().ToString("0.00", CultureInfo.InvariantCulture));

            _logger.LogInformation("Distribuição (tamanho => qtd de ondas):");
            foreach (var grupo in tamanhos.GroupBy(t => t).OrderBy(g => g.Key))
            {
                _logger.LogInformation("  {Tamanho} nota(s) => {Qtd} onda(s)", grupo.Key, grupo.Count());
            }
        }

        foreach (var erro in _varredor.Erros)
        {
            _logger.LogWarning("Erro de parse em {Arquivo}: {Mensagem}", erro.Arquivo, erro.Mensagem);
        }
    }
}
