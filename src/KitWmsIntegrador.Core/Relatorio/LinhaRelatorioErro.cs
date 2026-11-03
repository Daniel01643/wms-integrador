namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>
/// Uma linha do relatório de erros/pendências de integração. Cada linha representa
/// uma falha ao integrar uma nota, com os dados que o usuário (mensagem 11) pediu
/// para o relatório estruturado.
/// </summary>
/// <param name="NumeroNota">Número da nota fiscal que falhou.</param>
/// <param name="NumeroTransporte">Número do transporte (DocCarga) associado à nota.</param>
/// <param name="HorarioErro">Horário do erro, com fuso (<see cref="DateTimeOffset"/>).</param>
/// <param name="Mensagem">Mensagem do erro retornada/registrada na integração.</param>
public record LinhaRelatorioErro(
    string NumeroNota,
    string NumeroTransporte,
    DateTimeOffset HorarioErro,
    string Mensagem);
