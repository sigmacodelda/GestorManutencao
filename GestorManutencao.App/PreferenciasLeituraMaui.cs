using GestorManutencao.UI.Servicos;

namespace GestorManutencao.App;

public sealed class PreferenciasLeituraMaui : IPreferenciasLeitura
{
	private const string Chave = "modo_leitura";

	public ModoLeitura Modo
	{
		get
		{
			var guardado = Preferences.Get(Chave, (int)ModoLeitura.Automatico);
			return Enum.IsDefined(typeof(ModoLeitura), guardado) ? (ModoLeitura)guardado : ModoLeitura.Automatico;
		}
		set => Preferences.Set(Chave, (int)value);
	}
}
