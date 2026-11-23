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

	// Som curto de picagem, como o de um leitor. Falha de áudio nunca impede a leitura.
	private static void Apitar()
	{
		try
		{
			var tom = new Android.Media.ToneGenerator(Android.Media.Stream.Music, 100);
			tom.StartTone(Android.Media.Tone.PropBeep, 150);
			_ = Task.Delay(400).ContinueWith(_ => tom.Release());
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
