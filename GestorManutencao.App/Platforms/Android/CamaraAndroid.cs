using Android.Graphics;
using Android.Media;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.App;

public sealed class CamaraAndroid : ICamara
{
	public bool Disponivel => MediaPicker.Default.IsCaptureSupported;

	public async Task<byte[]?> TirarFotoAsync(int ladoMaximo, int bytesMaximos)
	{
		FileResult? foto;
		try { foto = await MediaPicker.Default.CapturePhotoAsync(); }
		catch (PermissionException) { throw new InvalidOperationException("Sem permissão para usar a câmara. Ative-a nas definições da app."); }
		catch (FeatureNotSupportedException) { throw new InvalidOperationException("A câmara não está disponível neste aparelho."); }
		if (foto is null) return null;

		return await Task.Run(() => Reduzir(foto.FullPath, ladoMaximo, bytesMaximos));
	}

	// Lê só o necessário (inSampleSize), corrige a rotação do EXIF e comprime até caber.
	private static byte[] Reduzir(string caminho, int lado, int bytesMaximos)
	{
		var medidas = new BitmapFactory.Options { InJustDecodeBounds = true };
		BitmapFactory.DecodeFile(caminho, medidas);
		var amostra = 1;
		while (Math.Max(medidas.OutWidth, medidas.OutHeight) / (amostra * 2) >= lado) amostra *= 2;

		using var lido = BitmapFactory.DecodeFile(caminho, new BitmapFactory.Options { InSampleSize = amostra })
			?? throw new InvalidOperationException("Não foi possível ler a foto. Tente outra vez.");

		var escala = Math.Min(1f, (float)lado / Math.Max(lido.Width, lido.Height));
		var matriz = new Matrix();
		matriz.PostScale(escala, escala);
		var rotacao = new ExifInterface(caminho).GetAttributeInt(ExifInterface.TagOrientation, (int)Orientation.Normal) switch
		{
			(int)Orientation.Rotate90 => 90f,
			(int)Orientation.Rotate180 => 180f,
			(int)Orientation.Rotate270 => 270f,
			_ => 0f
		};
		if (rotacao != 0f) matriz.PostRotate(rotacao);

		using var final = Bitmap.CreateBitmap(lido, 0, 0, lido.Width, lido.Height, matriz, true);
		for (var qualidade = 85; ; qualidade -= 15)
		{
			using var memoria = new MemoryStream();
			final.Compress(Bitmap.CompressFormat.Jpeg!, qualidade, memoria);
			if (memoria.Length <= bytesMaximos || qualidade <= 40) return memoria.ToArray();
		}
	}
}
