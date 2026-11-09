namespace GestorManutencao.UI.Offline;

public enum TipoOperacao { RegistarAvaria, Iniciar, Concluir, AdicionarMaterial, ApagarMaterial, AdicionarFoto, ApagarFoto }

// Pendente: por enviar. Incerta: pode já ter chegado ao servidor (material e foto); verifica-se antes de reenviar.
// Conflito: o servidor recusou; já não se envia e fica a aguardar que o técnico veja o aviso.
public enum EstadoOperacao { Pendente, Incerta, Conflito }

// FichaId pode ser um id local ("L-..."), até a avaria ser criada no servidor. Payload é JSON. Nunca leva segredos.
public sealed record OperacaoSaida(
    string Id, string Dono, TipoOperacao Tipo, string FichaId, string Payload, string? OpId, DateTimeOffset Criada,
    int Tentativas = 0, EstadoOperacao Estado = EstadoOperacao.Pendente, string? UltimoErro = null, string? AvisoId = null);

public sealed record TextoDoTecnico(string Rotulo, string Texto);

// O que o técnico escreveu fica aqui para o copiar; só desaparece quando ele o descarta.
public sealed record AvisoConflito(string Id, string Dono, string FichaId, string Equipamento, string Mensagem, IReadOnlyList<TextoDoTecnico> Textos, DateTimeOffset Quando);

// Fila de operações por enviar, por dono, por ordem de criação. Sobrevive à troca de utilizador e à limpeza da cache.
public interface IFilaSaida
{
    Task AdicionarAsync(OperacaoSaida operacao);
    Task<IReadOnlyList<OperacaoSaida>> ListarAsync(string dono);
    // De todos os donos (serve a limpeza de fotos órfãs).
    Task<IReadOnlyList<OperacaoSaida>> ListarTodasAsync();
    Task AtualizarAsync(OperacaoSaida operacao);
    Task RemoverAsync(string id);
    // Pendente e Incerta (não conta as em Conflito).
    Task<int> ContarPendentesAsync(string dono);
    // Troca o id local pelo real em todas as operações da ficha.
    Task RemapearFichaAsync(string dono, string idLocal, string idReal);

    Task AdicionarAvisoAsync(AvisoConflito aviso);
    Task<IReadOnlyList<AvisoConflito>> ListarAvisosAsync(string dono);
    Task RemoverAvisoAsync(string id);
}

public sealed class FilaSaidaMemoria : IFilaSaida
{
    private readonly object _trinco = new();
    private readonly List<OperacaoSaida> _operacoes = [];
    private readonly List<AvisoConflito> _avisos = [];

    public Task AdicionarAsync(OperacaoSaida operacao) { lock (_trinco) _operacoes.Add(operacao); return Task.CompletedTask; }

    public Task<IReadOnlyList<OperacaoSaida>> ListarAsync(string dono)
    {
        lock (_trinco) return Task.FromResult<IReadOnlyList<OperacaoSaida>>(_operacoes.Where(o => o.Dono == dono).ToList());
    }

    public Task<IReadOnlyList<OperacaoSaida>> ListarTodasAsync()
    {
        lock (_trinco) return Task.FromResult<IReadOnlyList<OperacaoSaida>>(_operacoes.ToList());
    }

    public Task AtualizarAsync(OperacaoSaida operacao)
    {
        lock (_trinco)
        {
            var pos = _operacoes.FindIndex(o => o.Id == operacao.Id);
            if (pos >= 0) _operacoes[pos] = operacao;
        }
        return Task.CompletedTask;
    }

    public Task RemoverAsync(string id) { lock (_trinco) _operacoes.RemoveAll(o => o.Id == id); return Task.CompletedTask; }

    public Task<int> ContarPendentesAsync(string dono)
    {
        lock (_trinco) return Task.FromResult(_operacoes.Count(o => o.Dono == dono && o.Estado != EstadoOperacao.Conflito));
    }

    public Task RemapearFichaAsync(string dono, string idLocal, string idReal)
    {
        lock (_trinco)
            for (var i = 0; i < _operacoes.Count; i++)
                if (_operacoes[i].Dono == dono && _operacoes[i].FichaId == idLocal) _operacoes[i] = _operacoes[i] with { FichaId = idReal };
        return Task.CompletedTask;
    }

    public Task AdicionarAvisoAsync(AvisoConflito aviso) { lock (_trinco) _avisos.Add(aviso); return Task.CompletedTask; }

    public Task<IReadOnlyList<AvisoConflito>> ListarAvisosAsync(string dono)
    {
        lock (_trinco) return Task.FromResult<IReadOnlyList<AvisoConflito>>(_avisos.Where(a => a.Dono == dono).ToList());
    }

    public Task RemoverAvisoAsync(string id) { lock (_trinco) _avisos.RemoveAll(a => a.Id == id); return Task.CompletedTask; }
}
