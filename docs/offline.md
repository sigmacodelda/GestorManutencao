# Modo offline (técnico)

O técnico trabalha sem rede e o que fez segue para o servidor quando a rede volta. Vale para o telemóvel (Android) e para o Windows.

## O que fica no telemóvel e por quanto tempo

| Dados | Quanto tempo |
|---|---|
| Intervenções por fazer (planeadas e em curso, todas) | Enquanto estiverem por fazer |
| Intervenções fechadas (concluídas, validadas, canceladas) | 30 dias |
| Material e fotos das intervenções por fazer | Enquanto estiverem por fazer |
| Lista de equipamentos (com obsoletos) e fichas dos equipamentos com intervenções por fazer | Atualizados de 15 em 15 minutos; as fichas de equipamento abertas há mais de 30 dias saem |
| Registos por enviar (fila) e fotos por enviar | Até serem enviados ou o técnico os descartar. Nunca são apagados pela limpeza da cache |
| Avisos "por rever" (com o texto escrito pelo técnico) | Até o técnico carregar em "Percebi" |

Ficheiros: a base `manutencao-offline.db3` e a pasta `fotos-pendentes`, ambas na pasta privada da app (`FileSystem.AppDataDirectory`). O backup do Android está desligado (`allowBackup=false`).

Não ficam na base: token, PIN ou qualquer palavra-passe (a sessão continua no armazenamento seguro do sistema).

## Quando se envia e se atualiza

- Ao abrir a app com sessão iniciada: envia a fila e depois atualiza a cache.
- Quando a rede volta: envia a fila e depois atualiza a cache.
- De 60 em 60 segundos, se houver registos por enviar.
- De 15 em 15 minutos, com a app aberta: atualiza a cache.
- Depois de cada escrita feita online e aceite, se ainda houver registos por enviar.
- Depois de entrar ou de trocar de utilizador.

Falhas passam em silêncio: nada se perde e a próxima tentativa corrige. Sem sessão não há pedidos.

## O que acontece num conflito

O servidor ganha sempre. Se recusar um registo feito sem rede (por exemplo, a ficha já foi concluída por outra pessoa, a ficha deixou de existir ou falta material):

1. O registo e os que dependem dele na mesma ficha deixam de ser enviados.
2. A ficha passa a mostrar o estado que está no servidor.
3. A faixa de cima mostra "N por rever". Em "Por rever" o técnico vê a mensagem e o texto que escreveu (para o copiar) e carrega em "Percebi" quando já não precisar. Só então o texto é apagado.

## O que não funciona offline

- Tudo o que é do supervisor: validar, devolver, atribuir, gerar planeadas, configuração e "todas as fichas".
- A regra "exige material" só se sabe quando o registo chega ao servidor. Se o técnico concluir sem material e a regra estiver ativa, a conclusão vira conflito ("por rever").
- Equipamentos que nunca foram guardados no telemóvel não aparecem na pesquisa de avaria sem rede. O ecrã avisa e pede para tentar quando houver rede.
- A avaria registada sem rede fica com um identificador local até ser criada no servidor; o texto tem no máximo 162 letras (o servidor junta-lhe um identificador da operação).

## Ao sair da conta

Se houver registos por enviar, a app pede confirmação. Se o técnico sair, a fila não se apaga: fica guardada até voltar a entrar com a mesma conta. Outro utilizador no mesmo telemóvel não vê nem envia esses registos; a cache do anterior é apagada ao entrar o novo.

## Riscos e pressupostos

- **Base não cifrada (decisão pendente do utilizador).** A base SQLite e as fotos pendentes ficam em claro na pasta privada da app. Num telemóvel com root, ou sem bloqueio de ecrã, podem ser lidos por quem tiver acesso ao aparelho. O que lá está: fichas, descrições de avarias, material, nomes de técnicos e equipamentos (sem PIN nem token). Opção para fechar o risco: SQLCipher, com a chave no armazenamento seguro do sistema (traz dependência e custo de migração).
- **Suposição: Dono = Nome do utilizador.** O dono dos dados guardados é o `Nome` do utilizador da sessão (o utilizador da app não tem o número de técnico do PHC). Dois utilizadores com o mesmo nome partilhariam a fila e a cache neste telemóvel. A app pode registar outro `IDonoDados` (por exemplo, com o número de técnico) sem mexer no resto.
- Sem teste em dispositivo real: SQLite, sincronização e câmara foram testados com ficheiros temporários em Windows; falta uma passagem no telemóvel.

## Como testar sem rede (passos curtos)

1. Entre com um técnico, com rede, e abra "As minhas intervenções" (isto enche a cache).
2. Ative o modo de avião. A faixa de cima passa a dizer "Sem ligação. A mostrar dados de …".
3. Registe uma avaria (pesquise o equipamento, escreva o problema): aparece "Guardado. Será enviado quando houver rede." e, na lista, a etiqueta "Por enviar".
4. Abra a avaria, junte material e uma foto, e carregue em "Feito". A faixa mostra "N por enviar".
5. Desative o modo de avião. A faixa passa por "A enviar…" e desaparece; a etiqueta "Por enviar" sai.
6. Para ver um conflito: sem rede, conclua uma ficha; com a rede desligada, peça a outra pessoa para a cancelar no servidor; volte a ligar. A faixa mostra "1 por rever" e a página "Por rever" mostra o texto escrito.
