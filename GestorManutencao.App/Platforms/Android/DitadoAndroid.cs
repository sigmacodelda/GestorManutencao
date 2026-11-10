using Android.Content;
using Android.OS;
using Android.Speech;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.App;

// O SpeechRecognizer do Android pára sozinho ao fim de uma pausa; aqui volta a arrancar até a pessoa carregar em Parar.
public sealed class DitadoAndroid : IDitado
{
	private SpeechRecognizer? _reconhecedor;
	private Intent? _pedido;
	private bool _ativo, _preferirOffline;
	private int _sessao;
	private TaskCompletionSource? _fim;

	public bool Disponivel => SpeechRecognizer.IsRecognitionAvailable(Android.App.Application.Context);

	public event Action<string>? Parcial;
	public event Action<string>? Frase;
	public event Action<string?>? Terminou;

	public async Task<string?> IniciarAsync()
	{
		if (!Disponivel) return "Este telemóvel não tem reconhecimento de voz.";

		var estado = await Permissions.CheckStatusAsync<Permissions.Microphone>();
		if (estado != PermissionStatus.Granted)
			estado = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.Microphone>());
		if (estado != PermissionStatus.Granted)
			return "Sem acesso ao microfone. Permita-o nas definições da app.";

		if (_ativo) await PararAsync();
		await MainThread.InvokeOnMainThreadAsync(Arrancar);
		return null;
	}

	public async Task PararAsync()
	{
		if (!_ativo) return;
		_ativo = false;
		var fim = _fim = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var sessao = _sessao;
		await MainThread.InvokeOnMainThreadAsync(() => _reconhecedor?.StopListening());
		// Se o Android não devolver o resultado final, não deixar a app presa.
		if (await Task.WhenAny(fim.Task, Task.Delay(2500)) != fim.Task && sessao == _sessao)
			await MainThread.InvokeOnMainThreadAsync(() => Terminar(sessao, null));
	}

	private void Arrancar()
	{
		var contexto = Android.App.Application.Context;
		Libertar();
		_sessao++;
		_ativo = true;
		_preferirOffline = true;
		_reconhecedor = SpeechRecognizer.CreateSpeechRecognizer(contexto);
		_reconhecedor!.SetRecognitionListener(new Ouvinte(this, _sessao));

		_pedido = new Intent(RecognizerIntent.ActionRecognizeSpeech);
		_pedido.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
		_pedido.PutExtra(RecognizerIntent.ExtraLanguage, "pt-PT");
		_pedido.PutExtra(RecognizerIntent.ExtraPartialResults, true);
		_pedido.PutExtra(RecognizerIntent.ExtraMaxResults, 1);
		// Usa o pacote de voz do telemóvel, se existir, para funcionar sem internet.
		_pedido.PutExtra(RecognizerIntent.ExtraPreferOffline, _preferirOffline);
		_pedido.PutExtra(RecognizerIntent.ExtraCallingPackage, contexto.PackageName);
		_reconhecedor.StartListening(_pedido);
	}

	private void Libertar()
	{
		var r = _reconhecedor;
		_reconhecedor = null;
		if (r is null) return;
		try { r.Cancel(); r.Destroy(); } catch (Exception) { }
	}

	// Sempre na thread principal.
	private void Terminar(int sessao, string? motivo)
	{
		if (sessao != _sessao) return;
		_ativo = false;
		Libertar();
		_sessao++;
		_fim?.TrySetResult();
		Terminou?.Invoke(motivo);
	}

	// O pacote offline não existe para esta língua: volta ao reconhecimento online.
	private void SemPacoteOffline(int sessao)
	{
		_preferirOffline = false;
		_pedido?.PutExtra(RecognizerIntent.ExtraPreferOffline, false);
		Recomecar(sessao);
	}

	private void Recomecar(int sessao)
	{
		if (sessao != _sessao || !_ativo) return;
		// Pequena pausa: evita um ciclo sem fim se o Android falhar logo.
		new Handler(Looper.MainLooper!).PostDelayed(() =>
		{
			if (sessao == _sessao && _ativo) _reconhecedor?.StartListening(_pedido);
		}, 250);
	}

	private sealed class Ouvinte(DitadoAndroid dono, int sessao) : Java.Lang.Object, IRecognitionListener
	{
		public void OnPartialResults(Bundle? parcial)
		{
			if (sessao == dono._sessao && Primeiro(parcial) is { Length: > 0 } texto) dono.Parcial?.Invoke(texto);
		}

		public void OnResults(Bundle? resultados)
		{
			if (sessao != dono._sessao) return;
			if (Primeiro(resultados) is { Length: > 0 } texto) dono.Frase?.Invoke(texto);
			if (dono._ativo) dono.Recomecar(sessao); else dono.Terminar(sessao, null);
		}

		public void OnError(SpeechRecognizerError erro)
		{
			if (sessao != dono._sessao) return;
			// Falta de fala ou de correspondência é normal numa pausa: se a pessoa não parou, continua.
			var normal = erro is SpeechRecognizerError.NoMatch or SpeechRecognizerError.SpeechTimeout or SpeechRecognizerError.RecognizerBusy;
			if (!dono._ativo) { dono.Terminar(sessao, null); return; }
			if (dono._preferirOffline && (int)erro is 12 or 13) { dono.SemPacoteOffline(sessao); return; }
			if (normal) { dono.Recomecar(sessao); return; }
			dono.Terminar(sessao, erro switch
			{
				SpeechRecognizerError.InsufficientPermissions => "Sem acesso ao microfone. Permita-o nas definições da app.",
				SpeechRecognizerError.Network or SpeechRecognizerError.NetworkTimeout => "Sem internet. Para ditar offline, instale o português nas definições de voz do Google (reconhecimento offline).",
				_ => "O ditado parou. Tente outra vez."
			});
		}

		public void OnReadyForSpeech(Bundle? parametros) { }
		public void OnBeginningOfSpeech() { }
		public void OnRmsChanged(float rmsdB) { }
		public void OnBufferReceived(byte[]? buffer) { }
		public void OnEndOfSpeech() { }
		public void OnEvent(int tipoEvento, Bundle? parametros) { }

		private static string? Primeiro(Bundle? pacote) =>
			pacote?.GetStringArrayList(SpeechRecognizer.ResultsRecognition)?.FirstOrDefault();
	}
}
