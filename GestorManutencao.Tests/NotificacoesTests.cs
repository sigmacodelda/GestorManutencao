using GestorManutencao.Api;
using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Servicos;
using EstadoApi = GestorManutencao.Api.EstadoIntervencao;
using EstadoUi = GestorManutencao.UI.Modelos.EstadoIntervencao;
using TipoUi = GestorManutencao.UI.Modelos.TipoIntervencao;
using Perfil = GestorManutencao.UI.Modelos.Perfil;

namespace GestorManutencao.Tests;

public class AvisosPuraTests
{
    private static readonly DateTime Manha = new(2026, 11, 6, 9, 30, 0);
    private static readonly DateOnly Hoje = DateOnly.FromDateTime(Manha);
    private const int Eu = 101;

    private static Intervencao Ficha(string id, int dias, EstadoUi estado, int tecno = Eu, TipoUi tipo = TipoUi.Preventiva) =>
        new(id, "Eq " + id, "Zona", tipo, Hoje.AddDays(dias), estado, Tecno: tecno);

    private static string[] Chaves(IEnumerable<Aviso> avisos) => avisos.Select(a => a.Chave).ToArray();

    private static IEnumerable<TipoAviso> Tipos(IEnumerable<Aviso> avisos, string id) => avisos.Where(a => a.FichaId == id).Select(a => a.Tipo);

    [Fact]
    public void Mais_de_500_fichas_nao_voltam_a_ser_avisadas()
    {
        var fichas = Enumerable.Range(0, 700).Select(i => Ficha("f" + i, -2, EstadoUi.Planeada)).ToArray();
        var gerados = CentroAvisos.Gerar(Perfil.Operador, Eu, fichas, Manha);

        var primeira = CentroAvisos.Processar(EstadoAvisos.Vazio, gerados);
        var segunda = CentroAvisos.Processar(primeira.Estado, gerados);

        Assert.Empty(segunda.Avisar);
        Assert.False(segunda.Mudou);
    }

    [Fact]
    public void Tecnico_recebe_atribuida_devolvida_hoje_e_atraso()
    {
        var fichas = new[]
        {
            Ficha("nova", 3, EstadoUi.Planeada),
            Ficha("dev", 0, EstadoUi.EmCurso) with { Motivo = "Falta o material" },
            Ficha("hoje", 0, EstadoUi.Planeada),
            Ficha("atrasada", -2, EstadoUi.Planeada)
        };

        var avisos = CentroAvisos.Gerar(Perfil.Operador, Eu, fichas, Manha);

        Assert.Equal([TipoAviso.Atribuida], Tipos(avisos, "nova"));
        Assert.Equal([TipoAviso.Devolvida], Tipos(avisos, "dev"));
        Assert.Equal([TipoAviso.Atribuida, TipoAviso.Hoje], Tipos(avisos, "hoje"));
        Assert.Equal([TipoAviso.Atribuida, TipoAviso.Atraso], Tipos(avisos, "atrasada"));
        Assert.Contains("Falta o material", avisos.Single(a => a.Tipo == TipoAviso.Devolvida).Texto);
    }

    [Fact]
    public void Ficha_atribuida_ja_em_curso_tambem_avisa_uma_so_vez()
    {
        var fichas = new[] { Ficha("curso", 0, EstadoUi.EmCurso) };

        var gerados = CentroAvisos.Gerar(Perfil.Operador, Eu, fichas, Manha);
        Assert.Equal([TipoAviso.Atribuida], Tipos(gerados, "curso"));

        var primeira = CentroAvisos.Processar(new EstadoAvisos(true, [], []), gerados);
        Assert.Single(primeira.Avisar, a => a.Tipo == TipoAviso.Atribuida);
        Assert.Empty(CentroAvisos.Processar(primeira.Estado, gerados).Avisar);
    }

    [Fact]
    public void Devolvida_outra_vez_com_o_mesmo_motivo_avisa_de_novo()
    {
        var devolvida = Ficha("f1", 0, EstadoUi.EmCurso) with { Motivo = "Falta o material" };

        var a1 = CentroAvisos.Processar(new EstadoAvisos(true, [], []), CentroAvisos.Gerar(Perfil.Operador, Eu, [devolvida], Manha));
        Assert.Contains(a1.Avisar, a => a.Tipo == TipoAviso.Devolvida);

        // Ainda devolvida: não repete.
        Assert.DoesNotContain(CentroAvisos.Processar(a1.Estado, CentroAvisos.Gerar(Perfil.Operador, Eu, [devolvida], Manha)).Avisar, a => a.Tipo == TipoAviso.Devolvida);

        // O técnico conclui (a ficha sai da lista) e o estado fica guardado mesmo sem avisos novos.
        var concluida = CentroAvisos.Processar(a1.Estado, CentroAvisos.Gerar(Perfil.Operador, Eu, [], Manha));
        Assert.True(concluida.Mudou);

        // Devolvida de novo com o mesmo motivo.
        var a2 = CentroAvisos.Processar(concluida.Estado, CentroAvisos.Gerar(Perfil.Operador, Eu, [devolvida], Manha));
        Assert.Contains(a2.Avisar, a => a.Tipo == TipoAviso.Devolvida);
    }

    [Fact]
    public void Limpar_esvazia_a_lista_sem_voltar_a_avisar()
    {
        var gerados = CentroAvisos.Gerar(Perfil.Operador, Eu, [Ficha("f1", 3, EstadoUi.Planeada)], Manha);
        var estado = CentroAvisos.Processar(new EstadoAvisos(true, [], []), gerados).Estado;
        Assert.NotEmpty(estado.Lista);

        var limpo = CentroAvisos.Limpar(estado);

        Assert.Empty(limpo.Lista);
        Assert.Empty(CentroAvisos.Processar(limpo, gerados).Avisar);
    }

    [Fact]
    public void Tecnico_so_ve_o_que_e_seu_e_nao_recebe_avisos_de_supervisor()
    {
        var fichas = new[]
        {
            Ficha("semdono", 0, EstadoUi.Planeada, tecno: 0),
            Ficha("outro", -3, EstadoUi.Planeada, tecno: 999),
            Ficha("feita", -3, EstadoUi.Concluida),
            Ficha("avaria", 0, EstadoUi.EmCurso, tipo: TipoUi.Curativa)
        };

        var avisos = CentroAvisos.Gerar(Perfil.Operador, Eu, fichas, Manha);

        Assert.DoesNotContain(avisos, a => a.FichaId is "semdono" or "outro" or "feita" or "avaria");
        Assert.DoesNotContain(avisos, a => a.Tipo is TipoAviso.PorValidar or TipoAviso.Avaria or TipoAviso.SemTecnico);
    }

    [Fact]
    public void Supervisor_recebe_por_validar_avaria_atraso_e_sem_tecnico_hoje_ou_amanha()
    {
        var fichas = new[]
        {
            Ficha("val", -1, EstadoUi.Concluida) with { Feito = "Trocado", Tecnico = "Ana" },
            Ficha("propria", -1, EstadoUi.Concluida) with { PodeValidar = false },
            Ficha("av", 0, EstadoUi.EmCurso, tipo: TipoUi.Curativa) with { Anomalia = "Não liga" },
            Ficha("atr", -4, EstadoUi.Planeada, tecno: 0),
            Ficha("prazo", 0, EstadoUi.Planeada) with { DataPrevista = Hoje.AddDays(-2) },
            Ficha("sh", 0, EstadoUi.Planeada, tecno: 0),
            Ficha("sa", 1, EstadoUi.Planeada, tecno: 0),
            Ficha("sd", 2, EstadoUi.Planeada, tecno: 0),
            Ficha("comdono", 1, EstadoUi.Planeada)
        };

        var avisos = CentroAvisos.Gerar(Perfil.Supervisor, 0, fichas, Manha);

        Assert.Equal([TipoAviso.PorValidar], Tipos(avisos, "val"));
        Assert.Empty(Tipos(avisos, "propria"));
        Assert.Equal([TipoAviso.Avaria], Tipos(avisos, "av"));
        Assert.Contains("Não liga", avisos.Single(a => a.Tipo == TipoAviso.Avaria).Texto);
        Assert.Contains(TipoAviso.Atraso, Tipos(avisos, "atr"));
        Assert.Equal([TipoAviso.Atraso], Tipos(avisos, "prazo"));
        Assert.Equal([TipoAviso.SemTecnico], Tipos(avisos, "sh"));
        Assert.Equal([TipoAviso.SemTecnico], Tipos(avisos, "sa"));
        Assert.Empty(Tipos(avisos, "sd"));
        Assert.Empty(Tipos(avisos, "comdono"));
    }

    [Fact]
    public void Destino_leva_a_ficha_certa_conforme_o_perfil()
    {
        var avisos = CentroAvisos.Gerar(Perfil.Operador, Eu, [Ficha("a b", 3, EstadoUi.Planeada)], Manha);
        Assert.Equal("intervencoes/a%20b", avisos.Single().Destino(Perfil.Operador));

        var validar = new Aviso("k", TipoAviso.PorValidar, "t", "x", "F1", Manha);
        Assert.Equal("validacao", validar.Destino(Perfil.Supervisor));
        Assert.Equal("planeadas", (validar with { Tipo = TipoAviso.Atraso }).Destino(Perfil.Supervisor));
        Assert.Equal("intervencoes", (validar with { FichaId = null }).Destino(Perfil.Operador));
    }

    [Fact]
    public void Chaves_sao_iguais_entre_execucoes_e_o_hash_nao_depende_do_arranque()
    {
        var fichas = new[] { Ficha("a", -1, EstadoUi.Planeada), Ficha("b", 0, EstadoUi.EmCurso) with { Motivo = "M" } };

        Assert.Equal(Chaves(CentroAvisos.Gerar(Perfil.Operador, Eu, fichas, Manha)), Chaves(CentroAvisos.Gerar(Perfil.Operador, Eu, fichas, Manha.AddMinutes(7))));
        Assert.Equal("e40c292c", CentroAvisos.Hash("a"));
    }

    [Fact]
    public void Chave_muda_quando_o_estado_muda()
    {
        var devolvida = Ficha("d", 0, EstadoUi.EmCurso) with { Motivo = "Primeiro" };
        var k1 = Chaves(CentroAvisos.Gerar(Perfil.Operador, Eu, [devolvida], Manha)).Single(k => k.StartsWith("devolvida:"));
        var k2 = Chaves(CentroAvisos.Gerar(Perfil.Operador, Eu, [devolvida with { Motivo = "Outro motivo" }], Manha)).Single(k => k.StartsWith("devolvida:"));
        Assert.NotEqual(k1, k2);

        var hoje1 = Chaves(CentroAvisos.Gerar(Perfil.Operador, Eu, [Ficha("h", 0, EstadoUi.Planeada)], Manha)).Single(k => k.StartsWith("hoje:"));
        var amanha = Manha.AddDays(1);
        var hoje2 = Chaves(CentroAvisos.Gerar(Perfil.Operador, Eu, [Ficha("h", 1, EstadoUi.Planeada)], amanha)).Single(k => k.StartsWith("hoje:"));
        Assert.NotEqual(hoje1, hoje2);

        var porOutro = Chaves(CentroAvisos.Gerar(Perfil.Operador, 0, [Ficha("x", 3, EstadoUi.Planeada, tecno: 102)], Manha)).Single();
        var porMim = Chaves(CentroAvisos.Gerar(Perfil.Operador, 0, [Ficha("x", 3, EstadoUi.Planeada)], Manha)).Single();
        Assert.NotEqual(porOutro, porMim);
    }

    [Fact]
    public void Resumo_so_depois_das_8h_e_so_se_houver_algo()
    {
        var fichas = new[] { Ficha("a", 0, EstadoUi.Planeada), Ficha("b", -1, EstadoUi.EmCurso) };

        Assert.DoesNotContain(CentroAvisos.Gerar(Perfil.Operador, Eu, fichas, Hoje.ToDateTime(new TimeOnly(7, 59))), a => a.Tipo == TipoAviso.Resumo);
        var resumo = CentroAvisos.Gerar(Perfil.Operador, Eu, fichas, Hoje.ToDateTime(new TimeOnly(8, 0))).Single(a => a.Tipo == TipoAviso.Resumo);
        Assert.Equal($"resumo:{Hoje:yyyyMMdd}", resumo.Chave);
        Assert.Equal("1 para hoje, 1 em atraso", resumo.Texto);

        Assert.DoesNotContain(CentroAvisos.Gerar(Perfil.Operador, Eu, [Ficha("longe", 10, EstadoUi.Planeada)], Manha), a => a.Tipo == TipoAviso.Resumo);
    }

    [Fact]
    public void Resumo_do_supervisor_inclui_por_validar()
    {
        var fichas = new[] { Ficha("v", -1, EstadoUi.Concluida), Ficha("h", 0, EstadoUi.Planeada, tecno: 0) };
        var resumo = CentroAvisos.Gerar(Perfil.Supervisor, 0, fichas, Manha).Single(a => a.Tipo == TipoAviso.Resumo);
        Assert.Equal("1 para hoje, 1 por validar", resumo.Texto);
    }

    [Fact]
    public void Primeira_execucao_e_linha_de_base_silenciosa_e_so_o_novo_dispara_aviso()
    {
        var antigas = CentroAvisos.Gerar(Perfil.Operador, Eu, [Ficha("a", -1, EstadoUi.Planeada), Ficha("b", 0, EstadoUi.Planeada)], Manha);

        var primeira = CentroAvisos.Processar(EstadoAvisos.Vazio, antigas);
        Assert.Empty(primeira.Avisar);
        Assert.True(primeira.Estado.Iniciado);
        Assert.Equal(antigas.Count, primeira.Estado.Lista.Count);
        Assert.All(primeira.Estado.Lista, e => Assert.True(e.Lido));

        var nova = CentroAvisos.Gerar(Perfil.Operador, Eu, [Ficha("a", -1, EstadoUi.Planeada), Ficha("b", 0, EstadoUi.Planeada), Ficha("c", 5, EstadoUi.Planeada)], Manha.AddMinutes(1));
        var segunda = CentroAvisos.Processar(primeira.Estado, nova);
        Assert.Equal(["atribuida:c:101"], Chaves(segunda.Avisar));
        Assert.False(segunda.Estado.Lista[0].Lido);
        Assert.Equal("atribuida:c:101", segunda.Estado.Lista[0].Aviso.Chave);
    }

    [Fact]
    public void Sem_duplicados_nem_repeticao_do_mesmo_aviso()
    {
        var a = new Aviso("k1", TipoAviso.Hoje, "t", "x", "1", Manha);
        var inicial = CentroAvisos.Processar(EstadoAvisos.Vazio, []).Estado;

        var r1 = CentroAvisos.Processar(inicial, [a, a]);
        Assert.Single(r1.Avisar);
        Assert.Single(r1.Estado.Lista);

        var r2 = CentroAvisos.Processar(r1.Estado, [a]);
        Assert.Empty(r2.Avisar);
        Assert.False(r2.Mudou);
        Assert.Single(r2.Estado.Lista);
    }

    [Fact]
    public void Resumo_dispara_uma_so_vez_por_dia_e_volta_no_dia_seguinte()
    {
        var fichas = new[] { Ficha("a", 0, EstadoUi.Planeada) };
        var estado = CentroAvisos.Processar(EstadoAvisos.Vazio, []).Estado;

        var r1 = CentroAvisos.Processar(estado, CentroAvisos.Gerar(Perfil.Operador, Eu, fichas, Manha));
        Assert.Contains(r1.Avisar, x => x.Tipo == TipoAviso.Resumo);
        var r2 = CentroAvisos.Processar(r1.Estado, CentroAvisos.Gerar(Perfil.Operador, Eu, fichas, Manha.AddHours(3)));
        Assert.DoesNotContain(r2.Avisar, x => x.Tipo == TipoAviso.Resumo);

        var amanha = Manha.AddDays(1);
        var r3 = CentroAvisos.Processar(r2.Estado, CentroAvisos.Gerar(Perfil.Operador, Eu, [Ficha("a", 1, EstadoUi.Planeada) with { Tecno = Eu }], amanha));
        Assert.Contains(r3.Avisar, x => x.Tipo == TipoAviso.Resumo);
    }

    [Fact]
    public void Lista_tem_no_maximo_50_com_os_mais_recentes_primeiro_e_o_que_saiu_nao_volta_a_avisar()
    {
        var estado = CentroAvisos.Processar(EstadoAvisos.Vazio, []).Estado;
        var todos = Enumerable.Range(0, 60).Select(i => new Aviso("k" + i, TipoAviso.Hoje, "t", "x", "f" + i, Manha.AddMinutes(i))).ToList();

        var r = CentroAvisos.Processar(estado, todos);

        Assert.Equal(60, r.Avisar.Count);
        Assert.Equal(CentroAvisos.MaxLista, r.Estado.Lista.Count);
        Assert.Equal("k59", r.Estado.Lista[0].Aviso.Chave);
        Assert.Equal("k10", r.Estado.Lista[^1].Aviso.Chave);
        Assert.Empty(CentroAvisos.Processar(r.Estado, todos).Avisar);
    }

    [Fact]
    public void Marcar_lidas_funciona_uma_a_uma_e_todas()
    {
        var estado = CentroAvisos.Processar(EstadoAvisos.Vazio, []).Estado;
        var r = CentroAvisos.Processar(estado, [new Aviso("a", TipoAviso.Hoje, "t", "x", "1", Manha), new Aviso("b", TipoAviso.Hoje, "t", "x", "2", Manha)]);

        var uma = CentroAvisos.MarcarLida(r.Estado, "a");
        Assert.Single(uma.Lista, e => !e.Lido);
        Assert.All(CentroAvisos.MarcarTodasLidas(uma).Lista, e => Assert.True(e.Lido));
    }
}

public class MotivoDevolucaoTests
{
    private static readonly TecnicoAtual Tecnico = new("op", 101, "Operador");
    private static readonly TecnicoAtual Supervisor = new("sup", 900, "Supervisor");

    [Fact]
    public async Task Motivo_so_aparece_na_ficha_em_curso_devolvida()
    {
        var repo = new RepositorioIntervencoesMemoria(TimeProvider.System);
        Assert.All(await repo.ListarAsync(Tecnico, default), f => Assert.Null(f.Motivo));

        var (resultado, devolvida) = await repo.DevolverAsync("F3", "Falta o tempo gasto", Supervisor, default);
        Assert.Equal(ResultadoIntervencao.Ok, resultado);
        Assert.Equal(EstadoApi.EmCurso, devolvida!.Estado);
        Assert.Equal("Falta o tempo gasto", devolvida.Motivo);
        Assert.Equal("Falta o tempo gasto", (await repo.ListarAsync(Tecnico, default)).Single(f => f.Id == "F3").Motivo);
        Assert.Equal("Falta o tempo gasto", (await repo.ObterAsync("F3", Tecnico, default))!.Motivo);

        var (_, concluida) = await repo.ConcluirAsync("F3", Tecnico, "Feito", new TimeOnly(8, 0), new TimeOnly(9, 0), default);
        Assert.Equal(EstadoApi.Concluida, concluida!.Estado);
        Assert.Null(concluida.Motivo);
        Assert.Null((await repo.ListarPorValidarAsync(Supervisor, default)).Single(f => f.Id == "F3").Motivo);
    }

    [Fact]
    public async Task Servico_de_desenvolvimento_devolve_o_motivo_e_limpa_ao_concluir()
    {
        var s = new DevIntervencoesService();
        Assert.Equal("Falta o tempo", (await s.DevolverAsync("T5", "Falta o tempo")).Motivo);
        var feita = await s.ConcluirAsync("T5", "Refeito", new TimeOnly(8, 0), new TimeOnly(9, 0));
        Assert.Null(feita.Motivo);
    }
}

public class NotificacaoServicoTests
{
    private sealed class SistemaFalso(bool mostra) : IAvisoSistema
    {
        public List<Aviso> Mostrados { get; } = [];
        public Task PedirPermissaoAsync() => Task.CompletedTask;
        public Task<bool> MostrarAsync(Aviso aviso) { Mostrados.Add(aviso); return Task.FromResult(mostra); }
    }

    private static readonly DateTime Dez = DateTime.Today.AddHours(10);

    private static async Task<(NotificacaoService Servico, DevIntervencoesService Fichas, SistemaFalso Sistema, List<Aviso> NaApp)> CriarAsync(
        string pin, IArmazemAvisos armazem, DevIntervencoesService? fichas = null, bool sistemaMostra = true)
    {
        var sessao = new SessaoService(new DevAutenticacaoService(), new ArmazemSessaoMemoria());
        Assert.True((await sessao.EntrarAsync(pin)).Sucesso);
        fichas ??= new DevIntervencoesService();
        var sistema = new SistemaFalso(sistemaMostra);
        var servico = new NotificacaoService(sessao, fichas, armazem, sistema, new CicloVidaApp(), () => Dez);
        var naApp = new List<Aviso>();
        servico.AvisoNaApp += naApp.Add;
        return (servico, fichas, sistema, naApp);
    }

    [Fact]
    public async Task Primeira_verificacao_enche_o_centro_sem_avisar_e_depois_so_o_novo_avisa()
    {
        var (servico, fichas, sistema, naApp) = await CriarAsync("4829", new ArmazemAvisosMemoria());

        await servico.VerificarAsync();
        Assert.NotEmpty(servico.Lista);
        Assert.Equal(0, servico.NaoLidas);
        Assert.Empty(sistema.Mostrados);
        Assert.Empty(naApp);

        await fichas.AtribuirAsync("T3", 101);
        await servico.VerificarAsync();
        Assert.Equal(2, sistema.Mostrados.Count);
        Assert.Equal(2, servico.NaoLidas);

        await servico.VerificarAsync();
        Assert.Equal(2, sistema.Mostrados.Count);

        await servico.MarcarTodasLidasAsync();
        Assert.Equal(0, servico.NaoLidas);
    }

    [Fact]
    public async Task Sem_aviso_do_sistema_a_app_mostra_a_faixa()
    {
        var (servico, fichas, _, naApp) = await CriarAsync("4829", new ArmazemAvisosMemoria(), sistemaMostra: false);
        await servico.VerificarAsync();
        await fichas.AtribuirAsync("T3", 101);
        await servico.VerificarAsync();
        Assert.Equal(2, naApp.Count);
    }

    [Fact]
    public async Task Lista_e_chaves_vistas_sobrevivem_ao_fecho_da_app()
    {
        var armazem = new ArmazemAvisosMemoria();
        var (primeiro, fichas, _, _) = await CriarAsync("4829", armazem);
        await primeiro.VerificarAsync();
        var total = primeiro.Lista.Count;

        var (segundo, _, sistema, _) = await CriarAsync("4829", armazem, fichas);
        await segundo.VerificarAsync();

        Assert.Equal(total, segundo.Lista.Count);
        Assert.Empty(sistema.Mostrados);
    }

    [Fact]
    public async Task Supervisor_ve_fichas_por_validar_e_sem_sessao_nao_faz_nada()
    {
        var (servico, _, _, _) = await CriarAsync("7953", new ArmazemAvisosMemoria());
        await servico.VerificarAsync();
        Assert.Contains(servico.Lista, e => e.Aviso.Tipo == TipoAviso.PorValidar);

        var semSessao = new NotificacaoService(new SessaoService(new DevAutenticacaoService(), new ArmazemSessaoMemoria()),
            new IntervencoesIndisponiveis(), new ArmazemAvisosMemoria(), new AvisoSistemaNenhum(), new CicloVidaApp());
        await semSessao.VerificarAsync();
        Assert.Empty(semSessao.Lista);
    }

    [Fact]
    public async Task Sem_api_nao_rebenta_nem_marca_a_linha_de_base()
    {
        var sessao = new SessaoService(new DevAutenticacaoService(), new ArmazemSessaoMemoria());
        await sessao.EntrarAsync("4829");
        var servico = new NotificacaoService(sessao, new IntervencoesIndisponiveis(), new ArmazemAvisosMemoria(), new AvisoSistemaNenhum(), new CicloVidaApp());

        await servico.VerificarAsync();

        Assert.Empty(servico.Lista);
    }
}

public class EmCursoTests
{
    private static readonly DateTime Dez = DateTime.Today.AddHours(10);

    private static Intervencao Ficha(string id, bool pausa = false, TimeOnly? inicio = null, bool podePausar = true, GestorManutencao.UI.Modelos.EstadoIntervencao estado = GestorManutencao.UI.Modelos.EstadoIntervencao.EmCurso) =>
        new(id, "Empilhador 05", "Armazém", GestorManutencao.UI.Modelos.TipoIntervencao.Curativa, DateOnly.FromDateTime(Dez), estado, "x", null, inicio ?? new TimeOnly(9, 0), PodePausar: podePausar, EmPausa: pausa);

    [Fact]
    public void So_ha_aviso_com_uma_intervencao_em_curso()
    {
        Assert.Null(RelogioEmCurso.Unica([]));
        Assert.Null(RelogioEmCurso.Unica([Ficha("A"), Ficha("B")]));
        Assert.Null(RelogioEmCurso.Unica([Ficha("A", estado: GestorManutencao.UI.Modelos.EstadoIntervencao.Planeada)]));
        Assert.Null(RelogioEmCurso.Unica([Ficha("A", podePausar: false)]));
        Assert.Equal("A", RelogioEmCurso.Unica([Ficha("A"), Ficha("B", estado: GestorManutencao.UI.Modelos.EstadoIntervencao.Planeada)])!.Id);
    }

    [Fact]
    public void O_tempo_desconta_as_pausas()
    {
        var f = Ficha("A");
        var (a1, e1) = RelogioEmCurso.Calcular(null, f, Dez);
        Assert.Equal(TimeSpan.FromHours(1).TotalMilliseconds, a1.DecorridoMs);

        var (a2, e2) = RelogioEmCurso.Calcular(e1, f with { EmPausa = true }, Dez.AddMinutes(10));
        Assert.True(a2.EmPausa);
        Assert.Equal(TimeSpan.FromMinutes(70).TotalMilliseconds, a2.DecorridoMs);

        // Pausa parada: o decorrido não anda.
        var (a3, e3) = RelogioEmCurso.Calcular(e2, f with { EmPausa = true }, Dez.AddMinutes(25));
        Assert.Equal(a2.DecorridoMs, a3.DecorridoMs);

        // Retoma 15 minutos depois: o tempo parado (15 min) não conta.
        var (a4, _) = RelogioEmCurso.Calcular(e3, f, Dez.AddMinutes(25));
        Assert.False(a4.EmPausa);
        Assert.Equal(TimeSpan.FromMinutes(70).TotalMilliseconds, a4.DecorridoMs);
    }

    private sealed class EmCursoFalso : IAvisoEmCurso
    {
        public List<AvisoEmCurso> Mostrados { get; } = [];
        public int Escondidos { get; private set; }
        public void Mostrar(AvisoEmCurso aviso) => Mostrados.Add(aviso);
        public void Esconder() => Escondidos++;
    }

    [Fact]
    public async Task Tecnico_com_uma_em_curso_ve_a_notificacao_e_o_botao_pausa()
    {
        var sessao = new SessaoService(new DevAutenticacaoService(), new ArmazemSessaoMemoria());
        Assert.True((await sessao.EntrarAsync("4829")).Sucesso);
        var fichas = new DevIntervencoesService();
        var falso = new EmCursoFalso();
        var servico = new NotificacaoService(sessao, fichas, new ArmazemAvisosMemoria(), new AvisoSistemaNenhum(), new CicloVidaApp(), () => Dez, null, null, falso);

        await servico.VerificarAsync();
        var minhas = await fichas.ListarMinhasAsync();
        var unica = RelogioEmCurso.Unica(minhas);
        if (unica is null) { Assert.Equal(1, falso.Escondidos); return; }

        Assert.Equal(unica.Id, falso.Mostrados.Last().FichaId);
        await servico.AlternarPausaAsync(unica.Id);
        Assert.True(falso.Mostrados.Last().EmPausa);
        await servico.AlternarPausaAsync(unica.Id);
        Assert.False(falso.Mostrados.Last().EmPausa);
    }
}
