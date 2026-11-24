#if ANDROID
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace GestorManutencao.App;

// Página nativa com a câmara: fecha à primeira leitura ou ao cancelar (botão ou tecla Voltar).
public sealed class PaginaLeituraCamara : ContentPage
{
	private readonly TaskCompletionSource<string?> _resultado = new();
	private readonly CameraBarcodeReaderView _camara;
	private int _fechada, _apitou;

	public Task<string?> Resultado => _resultado.Task;

	public PaginaLeituraCamara()
	{
		Title = "Ler etiqueta";
		_camara = new CameraBarcodeReaderView
		{
			IsDetecting = true,
			VerticalOptions = LayoutOptions.Fill,
			Options = new BarcodeReaderOptions
			{
				Formats = BarcodeFormat.QrCode | BarcodeFormat.Ean13 | BarcodeFormat.Ean8
					| BarcodeFormat.Code128 | BarcodeFormat.Code39 | BarcodeFormat.DataMatrix,
				AutoRotate = true,
				Multiple = false
			}
		};
		_camara.BarcodesDetected += AoDetetar;

		var cancelar = new Button { Text = "Cancelar", Margin = new Thickness(16) };
		cancelar.Clicked += (_, _) => Fechar(null);

		var grelha = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
		grelha.Add(_camara, 0, 0);
		grelha.Add(cancelar, 0, 1);
		Content = grelha;
	}

	private void AoDetetar(object? remetente, BarcodeDetectionEventArgs e)
	{
		var valor = e.Results?.Select(r => r.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
		if (valor is null) return;
		// Só a primeira leitura apita (a câmara pode detetar várias vezes seguidas antes de a página fechar).
		if (Interlocked.Exchange(ref _apitou, 1) == 0) Apitar();
		MainThread.BeginInvokeOnMainThread(() => Fechar(valor));
	}

	// Bip de caixa de supermercado: um tom agudo e seco (~2,4 kHz, 110 ms), com um harmónico para soar a leitor.
	// Gera-se uma vez em memória (sem ficheiro de som). Falha de áudio nunca impede a leitura.
	private const int TaxaAmostragem = 44100;
	private static short[]? _bip;

	private static short[] Bip()
	{
		if (_bip is not null) return _bip;
		var n = TaxaAmostragem * 110 / 1000;
		var amostras = new short[n];
		for (var i = 0; i < n; i++)
		{
			var t = (double)i / TaxaAmostragem;
			var envelope = Math.Min(1.0, i / (TaxaAmostragem * 0.003)) * Math.Min(1.0, (n - i) / (TaxaAmostragem * 0.015));
			var onda = Math.Sin(2 * Math.PI * 2400 * t) + 0.35 * Math.Sin(2 * Math.PI * 4800 * t);
			amostras[i] = (short)(onda / 1.35 * envelope * short.MaxValue * 0.85);
		}
		return _bip = amostras;
	}

	private static void Apitar()
	{
		try
		{
			var amostras = Bip();
			var atributos = new Android.Media.AudioAttributes.Builder()!
				.SetUsage(Android.Media.AudioUsageKind.Media)!
				.SetContentType(Android.Media.AudioContentType.Sonification)!
				.Build()!;
			var formato = new Android.Media.AudioFormat.Builder()!
				.SetEncoding(Android.Media.Encoding.Pcm16bit)!
				.SetSampleRate(TaxaAmostragem)!
				.SetChannelMask(Android.Media.ChannelOut.Mono)!
				.Build()!;
			var pista = new Android.Media.AudioTrack(atributos, formato, amostras.Length * 2, Android.Media.AudioTrackMode.Static, 0);
			pista.Write(amostras, 0, amostras.Length);
			pista.Play();
			_ = Task.Delay(400).ContinueWith(_ => { try { pista.Release(); } catch (Exception) { } });
		}
		catch (Exception) { /* sem áudio: lê-se na mesma */ }
	}

	public void Fechar(string? valor)
	{
		if (Interlocked.Exchange(ref _fechada, 1) == 1) return;
		_camara.IsDetecting = false;
		_camara.BarcodesDetected -= AoDetetar;
		_resultado.TrySetResult(valor);
		_ = FecharPaginaAsync();
	}

	private async Task FecharPaginaAsync()
	{
		try { await Navigation.PopModalAsync(); }
		catch (InvalidOperationException) { /* já não estava na pilha */ }
	}

	protected override bool OnBackButtonPressed()
	{
		Fechar(null);
		return true;
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		Fechar(null);
	}
}
#endif
