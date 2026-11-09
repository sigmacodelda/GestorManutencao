using GestorManutencao.UI.Servicos;
using Windows.Media.SpeechRecognition;

namespace GestorManutencao.App;

// Ditado contínuo do Windows (precisa do microfone e do reconhecimento de voz online ativos nas definições).
public sealed class DitadoWindows : IDitado
{
	private const int PrivacidadeNaoAceite = unchecked((int)0x80045509);

	private SpeechRecognizer? _reconhecedor;
	private bool _ativo;

	public bool Disponivel => true;

	public event Action<string>? Parcial;
	public event Action<string>? Frase;
	public event Action<string?>? Terminou;

	public async Task<string?> IniciarAsync()
	{
		if (_ativo) await PararAsync();
		try
		{
			var reconhecedor = Criar();
			reconhecedor.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "ditado"));
			var compilacao = await reconhecedor.CompileConstraintsAsync();
			if (compilacao.Status != SpeechRecognitionResultStatus.Success)
			{
				reconhecedor.Dispose();
				return "Não foi possível preparar o reconhecimento de voz.";
			}

			reconhecedor.HypothesisGenerated += (s, e) => Parcial?.Invoke(e.Hypothesis.Text);
			var sessao = reconhecedor.ContinuousRecognitionSession;
			sessao.ResultGenerated += (s, e) =>
			{
				if (e.Result.Status == SpeechRecognitionResultStatus.Success && e.Result.Text.Length > 0)
					Frase?.Invoke(e.Result.Text);
			};
			sessao.Completed += (s, e) => _ = AoCompletarAsync(reconhecedor, e.Status);

			_reconhecedor = reconhecedor;
			_ativo = true;
			await sessao.StartAsync();
			return null;
		}
		catch (Exception ex)
		{
			_ativo = false;
			Libertar();
			return ex switch
			{
				UnauthorizedAccessException => "Sem acesso ao microfone. Permita-o nas definições do Windows (Privacidade > Microfone).",
				_ when ex.HResult == PrivacidadeNaoAceite => "Ative o reconhecimento de voz online nas definições do Windows (Privacidade > Voz).",
				_ => "Não foi possível iniciar o ditado neste computador."
			};
		}
	}

	public async Task PararAsync()
	{
		if (!_ativo || _reconhecedor is null) return;
		_ativo = false;
		var reconhecedor = _reconhecedor;
		try { await reconhecedor.ContinuousRecognitionSession.StopAsync(); }
		catch (Exception) { }
		if (_reconhecedor == reconhecedor)
		{
			Libertar();
			Terminou?.Invoke(null);
		}
	}

	// Língua portuguesa se estiver instalada; senão, a do sistema.
	private static SpeechRecognizer Criar()
	{
		try { return new SpeechRecognizer(new Windows.Globalization.Language("pt-PT")); }
		catch (Exception) { return new SpeechRecognizer(); }
	}

	private async Task AoCompletarAsync(SpeechRecognizer reconhecedor, SpeechRecognitionResultStatus estado)
	{
		if (_reconhecedor != reconhecedor || !_ativo) return;
		// Pausa longa: o Windows fecha a sessão; enquanto a pessoa não parar, volta a abrir.
		if (estado is SpeechRecognitionResultStatus.TimeoutExceeded or SpeechRecognitionResultStatus.Success)
		{
			try { await reconhecedor.ContinuousRecognitionSession.StartAsync(); return; }
			catch (Exception) { }
		}
		_ativo = false;
		Libertar();
		Terminou?.Invoke(estado == SpeechRecognitionResultStatus.UserCanceled ? null : "O ditado parou. Tente outra vez.");
	}

	private void Libertar()
	{
		var r = _reconhecedor;
		_reconhecedor = null;
		try { r?.Dispose(); } catch (Exception) { }
	}
}
