namespace GestorManutencao.UI.Modelos;

// Gestão (só supervisor, só online): equipamentos, histórico de estado, instruções, tarefas e periodicidades, tal como a API os devolve.

public sealed record EquipamentoGestao(
    string Id, int NoInterno, string Ref, string Design, string Tipo, string Marca, string Modelo, string Serie,
    int Ano, decimal Valor, long NoFornecedor, string NomeFornecedor, DateOnly? Data, string Local, string Estado, bool Obsoleto,
    string ManualPdf, string Email, string Versao);

// Campos a null ficam como estão. PlaneadasConfirmadas: o nº de planeadas que o utilizador viu antes de marcar obsoleto.
public sealed record EdicaoEquipamento(
    string? Ref = null, string? Design = null, string? Tipo = null, string? Marca = null, string? Modelo = null, string? Serie = null,
    int? Ano = null, decimal? Valor = null, long? NoFornecedor = null, DateOnly? Data = null, string? Local = null, string? Estado = null,
    bool? Obsoleto = null, string? ManualPdf = null, string? Email = null, string? Versao = null, int? PlaneadasConfirmadas = null);

public sealed record OpcoesEquipamento(IReadOnlyList<string> Tipos, IReadOnlyList<string> Marcas, IReadOnlyList<string> Locais, IReadOnlyList<string> Estados);

public sealed record HistoricoEstado(DateOnly Data, string Estado, string Quem);

public sealed record InstrucaoManutencao(string Id, string EquipamentoId, string Versao, DateOnly Data, bool Obsoleta, string Obs, int NumTarefas, string Concorrencia);

public sealed record EdicaoInstrucao(string? Versao = null, DateOnly? Data = null, bool? Obsoleta = null, string? Obs = null, string? Concorrencia = null);

public sealed record TarefaManutencao(string Id, string InstrucaoId, string Periodo, string Responsabilidade, string Responsavel, string Descricao, string TipoManut, string Concorrencia, string? Aviso = null);

public sealed record EdicaoTarefa(string? Periodo = null, string? Responsabilidade = null, string? Responsavel = null, string? Descricao = null, string? TipoManut = null, string? Concorrencia = null);

public sealed record Periodicidade(string Id, string Periodo, string Unidade, int Intervalo, bool Aviso, string Concorrencia);

public sealed record EdicaoPeriodicidade(string? Unidade = null, int? Intervalo = null, bool? Aviso = null, string? Concorrencia = null);

// Limites e regras da gestão (os mesmos do servidor e da LandeiroMaui), só para avisar logo no ecrã.
public static class RegrasGestaoUi
{
    public const int MaxRef = 30, MaxDesign = 45, MaxTipo = 25, MaxMarca = 25, MaxModelo = 25, MaxSerie = 55, MaxLocal = 65, MaxManual = 200, MaxEmail = 254,
        MaxVersao = 10, MaxPeriodo = 20, MaxResponsabilidade = 45, MaxResponsavel = 25, MaxDescricao = 4000, MaxIntervalo = 9999;

    public static readonly string[] Estados = ["Em Serviço", "Fora de Serviço"];


    // O texto da periodicidade da tarefa, cortado ao tamanho da coluna (a u_periocid tem textos maiores do que o campo da tarefa), sem repetidos.
    public static IReadOnlyList<string> PeriodosParaEscolher(IEnumerable<Periodicidade> periodicidades) =>
        periodicidades.Select(p => Cortar(p.Periodo, MaxPeriodo)).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static string Cortar(string? texto, int tamanho)
    {
        var t = (texto ?? "").Trim();
        return t.Length <= tamanho ? t : t[..tamanho].TrimEnd();
    }

    public static string? ValidarPeriodicidade(string unidade, int? intervalo)
    {
        var u = (unidade ?? "").Trim().ToLowerInvariant();
        if (u is not ("dias" or "meses" or "nenhum")) return "A unidade é dias, meses ou nenhum.";
        return u != "nenhum" && intervalo is null or <= 0 or > MaxIntervalo ? $"Indique o intervalo (de 1 a {MaxIntervalo})." : null;
    }

    public static string TextoPeriodicidade(Periodicidade p) => p.Unidade switch
    {
        "dias" => p.Intervalo == 1 ? "todos os dias" : $"de {p.Intervalo} em {p.Intervalo} dias",
        "meses" => p.Intervalo == 1 ? "todos os meses" : $"de {p.Intervalo} em {p.Intervalo} meses",
        _ => "sem intervalo fixo"
    };

    public static string? ValidarTarefa(string? descricao, string? periodo, string? responsabilidade, string? responsavel) =>
        string.IsNullOrWhiteSpace(descricao) ? "Descreva a tarefa."
        : descricao.Trim().Length > MaxDescricao ? $"A descrição pode ter no máximo {MaxDescricao} letras."
        : (periodo ?? "").Trim().Length > MaxPeriodo ? $"A periodicidade pode ter no máximo {MaxPeriodo} letras."
        : (responsabilidade ?? "").Trim().Length > MaxResponsabilidade ? $"A responsabilidade pode ter no máximo {MaxResponsabilidade} letras."
        : (responsavel ?? "").Trim().Length > MaxResponsavel ? $"O responsável pode ter no máximo {MaxResponsavel} letras."
        : null;
}

// Formulário do equipamento em passos (telemóvel): identificação, aquisição e fornecedor, estado e manual.
public sealed class FormEquipamento
{
    public string Ref { get; set; } = "";
    public string Design { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Marca { get; set; } = "";
    public string Modelo { get; set; } = "";
    public string Serie { get; set; } = "";
    public string Local { get; set; } = "";
    public string Estado { get; set; } = "Em Serviço";
    public bool Obsoleto { get; set; }
    public long NoFornecedor { get; set; }
    public string NomeFornecedor { get; set; } = "";
    public int? Ano { get; set; }
    public decimal? Valor { get; set; }
    public DateOnly? Data { get; set; }
    public string ManualPdf { get; set; } = "";
    public string Email { get; set; } = "";

    public static FormEquipamento De(EquipamentoGestao e) => new()
    {
        Ref = e.Ref, Design = e.Design, Tipo = e.Tipo, Marca = e.Marca, Modelo = e.Modelo, Serie = e.Serie, Local = e.Local, Estado = e.Estado,
        Obsoleto = e.Obsoleto, NoFornecedor = e.NoFornecedor, NomeFornecedor = e.NomeFornecedor, Ano = e.Ano == 0 ? null : e.Ano, Valor = e.Valor == 0 ? null : e.Valor,
        Data = e.Data, ManualPdf = e.ManualPdf, Email = e.Email
    };

    private static bool Igual(string? a, string? b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal);

    // Equipamento novo (original null): vai tudo. Edição: só o que mudou, mais a versão lida; null se nada mudou.
    public EdicaoEquipamento? ParaEdicao(EquipamentoGestao? o)
    {
        if (o is null)
            return new EdicaoEquipamento(Ref.Trim(), Design.Trim(), Tipo.Trim(), Marca.Trim(), Modelo.Trim(), Serie.Trim(), Ano ?? 0, Valor ?? 0, NoFornecedor, Data, Local.Trim(), Estado, Obsoleto,
                ManualPdf.Trim(), Email.Trim());
        var e = new EdicaoEquipamento(
            Ref: Igual(Ref, o.Ref) ? null : Ref.Trim(), Design: Igual(Design, o.Design) ? null : Design.Trim(), Tipo: Igual(Tipo, o.Tipo) ? null : Tipo.Trim(),
            Marca: Igual(Marca, o.Marca) ? null : Marca.Trim(), Modelo: Igual(Modelo, o.Modelo) ? null : Modelo.Trim(), Serie: Igual(Serie, o.Serie) ? null : Serie.Trim(),
            Ano: (Ano ?? 0) == o.Ano ? null : Ano ?? 0, Valor: (Valor ?? 0) == o.Valor ? null : Valor ?? 0, NoFornecedor: NoFornecedor == o.NoFornecedor ? null : NoFornecedor,
            Data: Data == o.Data ? null : Data, Local: Igual(Local, o.Local) ? null : Local.Trim(), Estado: Igual(Estado, o.Estado) ? null : Estado,
            Obsoleto: Obsoleto == o.Obsoleto ? null : Obsoleto, ManualPdf: Igual(ManualPdf, o.ManualPdf) ? null : ManualPdf.Trim(), Email: Igual(Email, o.Email) ? null : Email.Trim());
        return e == new EdicaoEquipamento() ? null : e with { Versao = string.IsNullOrEmpty(o.Versao) ? null : o.Versao };
    }

    public bool TemAlteracoes(EquipamentoGestao? o) => o is null ? Ref.Length + Design.Length > 0 : ParaEdicao(o) is not null;

    // Marcar obsoleto (passar de ativo a obsoleto) pede confirmação com o nº de planeadas que ficam canceladas.
    public bool PedeConfirmacaoDeObsoleto(EquipamentoGestao? o) => Obsoleto && o is not { Obsoleto: true };
}

public static class PassosEquipamento
{
    public static readonly string[] Titulos = ["Identificação", "Aquisição", "Estado e manual"];

    public static string? Validar(int passo, FormEquipamento f)
    {
        static string? Tam(string? v, int max, string nome) => (v ?? "").Trim().Length > max ? $"{nome} pode ter no máximo {max} letras." : null;
        switch (passo)
        {
            case 0:
                if (string.IsNullOrWhiteSpace(f.Ref)) return "Indique o nº da infraestrutura.";
                if (string.IsNullOrWhiteSpace(f.Design)) return "Indique a designação.";
                return Tam(f.Ref, RegrasGestaoUi.MaxRef, "O nº") ?? Tam(f.Design, RegrasGestaoUi.MaxDesign, "A designação") ?? Tam(f.Tipo, RegrasGestaoUi.MaxTipo, "O tipo")
                    ?? Tam(f.Marca, RegrasGestaoUi.MaxMarca, "A marca") ?? Tam(f.Modelo, RegrasGestaoUi.MaxModelo, "O modelo") ?? Tam(f.Serie, RegrasGestaoUi.MaxSerie, "O nº de série")
                    ?? Tam(f.Local, RegrasGestaoUi.MaxLocal, "O local");
            case 1:
                if (f.Ano is < 0 or > 9999) return "O ano tem de estar entre 0 e 9999.";
                if (f.Valor is < 0 or > 999_999_999m) return "O valor de aquisição é inválido.";
                if (f.Data is { } d && (d.Year < 1900 || d.Year > 2999)) return "A data tem de estar entre 1900 e 2999.";
                return null;
            default:
                // Um equipamento antigo sem estado só o ganha se o utilizador escolher um (senão não se grava estado nem histórico).
                if (f.Estado.Length > 0 && !RegrasGestaoUi.Estados.Contains(f.Estado)) return "O estado é Em Serviço ou Fora de Serviço.";
                if (!string.IsNullOrWhiteSpace(f.Email) && (f.Email.Trim().Contains(' ') || !f.Email.Contains('@') || f.Email.Trim().StartsWith('@') || f.Email.Trim().EndsWith('@'))) return "O e-mail não é válido.";
                return Tam(f.ManualPdf, RegrasGestaoUi.MaxManual, "O manual") ?? Tam(f.Email, RegrasGestaoUi.MaxEmail, "O e-mail");
        }
    }

    // O primeiro passo com erro (para o ecrã levar o utilizador até lá ao guardar); -1 se está tudo bem.
    public static int PrimeiroComErro(FormEquipamento f)
    {
        for (var i = 0; i < Titulos.Length; i++) if (Validar(i, f) is not null) return i;
        return -1;
    }

    public static string TextoConfirmacaoObsoleto(int planeadas) => planeadas switch
    {
        0 => "Este equipamento não tem planeadas por fazer. Marcar como obsoleto?",
        1 => "Vai ficar cancelada 1 ficha planeada deste equipamento. As que estão em curso não são mexidas.",
        _ => $"Vão ficar canceladas {planeadas} fichas planeadas deste equipamento. As que estão em curso não são mexidas."
    };
}
