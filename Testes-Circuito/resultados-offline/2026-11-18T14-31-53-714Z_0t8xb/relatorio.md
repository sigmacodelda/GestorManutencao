# Teste offline/online (app Windows real)

32/32 passos ok · 0 falha(s)

| Modo | Passo | Resultado | Detalhe |
|---|---|---|---|
| computador | Técnico: entra online e o sinal de ligação mostra "ligado" | ok | {"visivel":true,"estado":"ok","texto":"Ligado"} |
| computador | Técnico: online, regista a avaria 1 (vai direta à API) | ok |  |
| computador | Técnico: SEM REDE, a pesquisa de equipamentos devolve resultados da cópia guardada | ok | 1 opção(ões) |
| computador | Técnico: SEM REDE, regista a avaria 2 e fica "guardado, será enviado" | ok | sinal: {"visivel":true,"estado":"sem","texto":"Sem ligação. Pode continuar a trabalhar. 1 por enviar."} |
| computador | Técnico: SEM REDE, "As minhas intervenções" mostra a ficha guardada (por enviar) | ok |  |
| computador | Técnico: a rede volta e a avaria 2 segue para a API (até 100 s) | ok | chegou em 43 s; sinal: {"visivel":true,"estado":"ok","texto":"Ligado"} |
| computador | Técnico: escreve a assinatura e fica guardada no utilizador (API) | ok |  |
| computador | Supervisor: entra online e vê as fichas por validar | ok |  |
| computador | Supervisor: SEM REDE, a lista por validar continua visível (cópia guardada) | ok | 3 fichas |
| computador | Supervisor: SEM REDE, valida uma ficha ("Guardado. Será enviado quando houver rede.") | ok |  |
| computador | Supervisor: SEM REDE, devolve uma ficha com motivo | ok |  |
| computador | Supervisor: SEM REDE, o que exige rede avisa em vez de falhar calado (planeadas/equipa) | ok | Não foi possível carregar a equipa. |
| computador | Supervisor: a rede volta e validar/devolver chegam à API (até 100 s) | ok | chegou em 34 s |
| computador | Qualidade: entra online, vê o Início e a lista por concluir | ok |  |
| computador | Qualidade: SEM REDE, a lista continua visível e conclui com o nome de quem higienizou | ok |  |
| computador | Qualidade: a rede volta e a conclusão chega à API (até 100 s) | ok | chegou em 44 s |
| telemovel | Técnico: entra online e o sinal de ligação mostra "ligado" | ok | {"visivel":true,"estado":"ok","texto":"Ligado"} |
| telemovel | Técnico: online, regista a avaria 1 (vai direta à API) | ok |  |
| telemovel | Técnico: SEM REDE, a pesquisa de equipamentos devolve resultados da cópia guardada | ok | 1 opção(ões) |
| telemovel | Técnico: SEM REDE, regista a avaria 2 e fica "guardado, será enviado" | ok | sinal: {"visivel":true,"estado":"sem","texto":"Sem ligação. Pode continuar a trabalhar. 1 por enviar."} |
| telemovel | Técnico: SEM REDE, "As minhas intervenções" mostra a ficha guardada (por enviar) | ok |  |
| telemovel | Técnico: a rede volta e a avaria 2 segue para a API (até 100 s) | ok | chegou em 47 s; sinal: {"visivel":true,"estado":"ok","texto":"Ligado"} |
| telemovel | Técnico: escreve a assinatura e fica guardada no utilizador (API) | ok |  |
| telemovel | Supervisor: entra online e vê as fichas por validar | ok |  |
| telemovel | Supervisor: SEM REDE, a lista por validar continua visível (cópia guardada) | ok | 3 fichas |
| telemovel | Supervisor: SEM REDE, valida uma ficha ("Guardado. Será enviado quando houver rede.") | ok |  |
| telemovel | Supervisor: SEM REDE, devolve uma ficha com motivo | ok |  |
| telemovel | Supervisor: SEM REDE, o que exige rede avisa em vez de falhar calado (planeadas/equipa) | ok | Não foi possível carregar a equipa. |
| telemovel | Supervisor: a rede volta e validar/devolver chegam à API (até 100 s) | ok | chegou em 36 s |
| telemovel | Qualidade: entra online, vê o Início e a lista por concluir | ok |  |
| telemovel | Qualidade: SEM REDE, a lista continua visível e conclui com o nome de quem higienizou | ok |  |
| telemovel | Qualidade: a rede volta e a conclusão chega à API (até 100 s) | ok | chegou em 46 s |
