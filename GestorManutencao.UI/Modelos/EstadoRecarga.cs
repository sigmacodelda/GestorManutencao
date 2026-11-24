namespace GestorManutencao.UI.Modelos;

// Recargas de listas: o "A carregar…" só na primeira carga; depois a lista fica visível e uma falha vira aviso, sem a esvaziar.
public static class EstadoRecarga
{
    public static bool MostrarSpinner(bool jaTemDados) => !jaTemDados;

    public static bool FalhaSubstituiLista(bool jaTemDados) => !jaTemDados;
}
