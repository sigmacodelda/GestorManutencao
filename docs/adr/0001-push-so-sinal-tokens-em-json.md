# ADR 0001: Push FCM só-sinal e tokens dos aparelhos num ficheiro JSON

- Data: 2026-11-13
- Estado: aceite

## Contexto
Com a app fechada, o canal em tempo real (SignalR) não chega aos aparelhos. Os avisos são calculados na app, por isso o servidor não decide o que avisar. Há no máximo cerca de 15 utilizadores.

## Decisão
Depois de qualquer escrita nas fichas, a API envia por FCM uma mensagem só de dados (`{"t":"fichasMudaram"}`, prioridade alta, TTL 60 s, collapse key `fichas`), sem título, texto nem identificadores. A app acorda e verifica sozinha. Escritas seguidas juntam-se (cerca de 3 s) num só envio e o aparelho de quem escreveu fica de fora. Os tokens ficam num ficheiro JSON no servidor (`Push:FicheiroDispositivos`), com escrita atómica e no máximo 5 por utilizador. A credencial Firebase lê-se de um ficheiro fora do repositório (`Push:CaminhoChaveFirebase`); sem caminho o envio fica desligado.

## Alternativas consideradas
- Push com o texto do aviso calculado no servidor: duplica a lógica de avisos e põe dados da operação no Google.
- Tabela de dispositivos na base: exige tabela nova no PHC, desproporcionado para ~15 utilizadores.
- Só SignalR e verificação periódica: não acorda a app fechada.

## Consequências
Nenhum dado pessoal sai para o FCM, mas os tokens ficam em claro no ficheiro do servidor (proteger a pasta). A dependência FirebaseAdmin entra na API. Um segundo servidor exigiria partilhar o ficheiro ou passar a base de dados. O ficheiro tem de sobreviver às republicações.
