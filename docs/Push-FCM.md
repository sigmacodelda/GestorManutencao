# Push FCM (Android)

O servidor envia, a cada escrita nas fichas, um push FCM só de dados `{ "t": "fichasMudaram" }` com prioridade alta. A app não mostra o push: acorda, verifica as fichas na API e mostra os avisos locais de sempre (sem duplicados).

## Ficheiro google-services.json

- Caminho: `GestorManutencao.App/Platforms/Android/google-services.json` (projeto Firebase `gestormanutencaosigmacode`, pacote `pt.sigmacode.gestormanutencao`).
- Obter: consola Firebase, Definições do projeto, Os seus apps, app Android, "google-services.json". Guardar no caminho acima.
- Sem o ficheiro a app compila na mesma, mas não recebe push (o csproj só o inclui se existir).
- Mudar de projeto Firebase ou de pacote obriga a descarregar outro ficheiro.

## Como funciona

1. Depois do pedido de permissão de notificações, a app obtém o token FCM e faz `POST /dispositivos` com o JWT da sessão.
2. Ao terminar a sessão faz `DELETE /dispositivos` com o mesmo token (e o JWT dessa sessão).
3. `ServicoPushFirebase` recebe o push (app aberta, em segundo plano ou morta), restaura a sessão do armazém seguro se for preciso e corre `NotificacaoService.VerificarAsync` com limite de 9 s.
4. Token novo (`OnNewToken`): volta a registar se houver sessão.

## Testar à mão num telemóvel Android

- Instalar o APK, entrar, aceitar as notificações. Confirmar na API que o token foi registado.
- App em primeiro plano, em segundo plano, fechada (deslizar nos recentes) e após reiniciar o telemóvel sem abrir a app: gravar uma ficha noutro aparelho e ver o aviso chegar. Com a app morta pode haver atraso se a poupança de bateria do fabricante restringir a app.
- Tocar na notificação: abre a app na ficha.
- Recusar a permissão de notificações: o push não pode rebentar nem mostrar nada; ao dar a permissão depois, os avisos seguintes aparecem.
- Terminar a sessão: a API deixa de ter o token; gravar uma ficha não acorda o aparelho.
- Sem rede: nada rebenta; a verificação seguinte (ciclo ou SignalR) apanha o que faltou.
