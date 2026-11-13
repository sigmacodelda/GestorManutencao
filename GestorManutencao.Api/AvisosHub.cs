using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GestorManutencao.Api;

// Canal em tempo real: só diz "as fichas mudaram" (sem dados). Cada aparelho volta a perguntar à API com o seu próprio token,
// por isso nada do que é privado passa por aqui.
[Authorize]
public sealed class AvisosHub : Hub
{
    public const string Rota = "/hub/avisos";
    public const string Evento = "fichasMudaram";
}
