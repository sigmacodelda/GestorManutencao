using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace GestorManutencao.Api;

public sealed record PedidoDispositivo(string? Token);

public static class EndpointsDispositivos
{
    public static void MapDispositivos(this WebApplication app)
    {
        app.MapPost("/dispositivos", ([FromBody] PedidoDispositivo pedido, ClaimsPrincipal eu, IRepositorioDispositivos repo) =>
        {
            var codigo = eu.FindFirstValue("sub") ?? eu.FindFirstValue(ClaimTypes.NameIdentifier);
            if (codigo is null) return Results.Unauthorized();
            if (!RepositorioDispositivosFicheiro.TokenValido(pedido.Token)) return Results.Json(new { erro = "pedido_invalido" }, statusCode: 400);
            repo.Registar(codigo, pedido.Token!);
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapDelete("/dispositivos", ([FromBody] PedidoDispositivo pedido, ClaimsPrincipal eu, IRepositorioDispositivos repo) =>
        {
            var codigo = eu.FindFirstValue("sub") ?? eu.FindFirstValue(ClaimTypes.NameIdentifier);
            if (codigo is null) return Results.Unauthorized();
            if (!RepositorioDispositivosFicheiro.TokenValido(pedido.Token)) return Results.Json(new { erro = "pedido_invalido" }, statusCode: 400);
            repo.Remover(codigo, pedido.Token!);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
