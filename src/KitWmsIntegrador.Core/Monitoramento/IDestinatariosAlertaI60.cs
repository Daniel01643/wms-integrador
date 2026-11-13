namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>Destinatários do alerta imediato da I60 (relidos a cada envio).</summary>
public interface IDestinatariosAlertaI60
{
    /// <summary>Lê a lista atual de e-mails.</summary>
    IReadOnlyList<string> Ler();
}
