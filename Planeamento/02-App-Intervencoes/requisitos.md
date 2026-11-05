# Parte 2: requisitos da app de intervenções (APP-01 a APP-08)

Fonte: `00-Comum/requisitos-gerais.md` (secção 3, Parte 2), quadro 3 da reunião de 04/11/2026 e os PATs. Utilizadores: **operador** (técnico de manutenção, usa o telemóvel), **supervisor** (valida no backoffice, Parte 1) e **administrador de manutenção**. Os nomes dos perfis (roles) são a pendência 10; aqui usam-se os termos genéricos.

Convenção: "Suposição:" marca o que não vem do `00-global.md` nem dos PATs. Os nomes de campos e tabelas do PHC são provisórios até P1-T01 e P1-T02.

## Interpretação do esboço do quadro 3 (confirmada olhando para a imagem)

| Elemento desenhado | Leitura | Estado |
|---|---|---|
| "Login (Pin ou utilizador e pass)", "Deve manter o máx. tempo possível" | Duas formas de entrar; sessão longa | Lido na imagem |
| "1.ª fase" e ecrã de lista com linhas e um botão "+" no canto superior direito | Lista de intervenções com "+" para criar | Lido na imagem |
| Seta do "+" para o 2.º ecrã, cabeçalho "Intr." | Ecrã de criar intervenção | Lido na imagem |
| Linha por baixo do cabeçalho com três rótulos curtos e ícones (relógio, pessoa, quadrado) | Hora de início, técnico e hora de fim (ou conclusão) | Suposição: os rótulos manuscritos são pouco legíveis ("HI", "Pessoa", "Término") |
| Caixa grande com linhas de texto | Anomalia e intervenção feita (texto livre) | Suposição |
| Quadrado pequeno dentro da caixa | Foto da intervenção | Suposição |
| Oval à direita do ecrã | Botão de ação (guardar e concluir) | Suposição |
| "Configuração no BO que defina se pode avançar ordem intervenções" | Perfil com ou sem permissão de avançar fora de ordem | Lido na imagem |
| "Obrigatório trabalhar" (corte na imagem) | Obrigatório trabalhar offline | Suposição: a frase está cortada; coincide com o requisito APP-02 e com a reunião |

## APP-01 Login por PIN ou por utilizador e password

- **Utilizador:** operador, supervisor.
- **Regra de negócio:** 1.º acesso no dispositivo: utilizador e password (login SQL do próprio utilizador, como no LandeiroMaui; exige rede). A app pede para definir um PIN de 4 a 6 dígitos. Acessos seguintes: PIN desbloqueia a sessão guardada, mesmo sem rede. A password nunca se guarda; o PIN só existe como hash com sal no armazenamento seguro (D-06). A sessão não expira por tempo. Termina por logout, utilizador desativado ou sem o perfil (verificado quando há rede), troca de dispositivo, ou PIN bloqueado (volta a pedir utilizador e password).
- **Critérios de aceitação:**
  1. Com rede e credenciais certas, entra e guarda nome, iniciais e perfis; credenciais erradas mostram "Utilizador ou password incorretos" e sem rede mostram "Sem ligação ao servidor", nunca o mesmo texto para as duas causas.
  2. Definir PIN guarda só hash, sal e iterações; um teste confirma que o PIN em claro não existe em armazenamento, log nem SQLite.
  3. Fechar e abrir a app pede o PIN e, certo, entra sem rede; errado conta falha, e a contagem sobrevive ao fecho da app.
  4. À 5.ª falha (valor `PIN_MAX_TENTATIVAS`, defeito 5) o PIN é anulado, pede utilizador e password e a fila de saída mantém-se.
  5. Pede PIN também após X minutos em segundo plano (Suposição: 5; valor local).
  6. Logout com registos por enviar está bloqueado, com aviso e número de pendentes.
  7. Utilizador desativado ou sem o perfil termina a sessão na 1.ª sincronização com rede.
- **Casos limite:** primeiro acesso sem rede (sem sessão não há login falso); armazenamento seguro corrompido ou apagado (limpar e pedir login, avisar); relógio do telemóvel errado; PIN igual a 0000 ou 1234 (Suposição: aceitar e avisar, rejeitar sequências óbvias fica para decisão); telemóvel partilhado por vários técnicos (um utilizador por sessão; trocar de utilizador exige logout).
- **Perguntas abertas:** (1) como se deteta um utilizador desativado sem guardar a password (a cadeia da app lê `us`? campo a confirmar em P2-T01); (2) telemóveis pessoais ou da empresa; (3) políticas de ecrã bloqueado; (4) credencial de sincronização (decisão D-10, risco R-03).

## APP-02 Trabalhar offline (obrigatório)

- **Utilizador:** operador.
- **Regra de negócio:** tudo o que o operador faz no telemóvel funciona sem rede: ver equipamentos, planeadas e configuração em cache, criar e editar intervenções, fotografar, receber alertas. As gravações vão para uma fila de saída local e seguem quando há rede. Os ecrãs leem sempre do SQLite local; o servidor só o atualiza em segundo plano. Fora do âmbito do M2: sincronizar com a app fechada.
- **Critérios de aceitação:**
  1. Em modo avião a app abre (com PIN), mostra a lista e permite criar uma curativa com foto; o registo fica "Pendente".
  2. A lista abre em menos de 2 s com cache (mediana de 5 medições).
  3. Cortar a rede 5 e 20 minutos a meio de uma intervenção não perde nada nem trava o operador (nunca fica à espera de timeout).
  4. Fechar ou matar a app com pendentes não perde a fila; ao reabrir continua a enviar.
  5. A faixa de estado mostra "Sem ligação, N por enviar" e a hora dos dados ("dados de HH:mm").
- **Casos limite:** base SQLite corrompida (recria sem impedir o arranque; os pendentes perdidos são avisados); telemóvel sem espaço; cache vazia sem rede (mensagem a dizer que é preciso ligar uma vez); mudança de hora; muitos pendentes (centenas) sem bloquear a interface.
- **Perguntas abertas:** janela de dados em cache (Suposição: planeadas dos próximos 30 dias, em curso, atrasadas e concluídas dos últimos 2 dias); se a cache cifrada (SQLCipher) é exigida (dados não sensíveis, Suposição: não).

## APP-03 Alerta de intervenções planeadas

- **Utilizador:** operador.
- **Regra de negócio:** notificação local agendada a partir da cache, com a antecedência definida no backoffice (BO-04: global ou por plano ou equipamento). Suposição: aviso às 08:00 do dia (data planeada menos a antecedência), outro no próprio dia, e um por dia enquanto atrasada. Reagenda após cada sincronização, ao reiniciar o telemóvel e quando a configuração muda. A faixa dentro da app mostra "N planeadas nos próximos D dias".
- **Critérios de aceitação:**
  1. Com antecedência de 2 dias, uma planeada para dia 10 gera aviso no dia 8 (teste do planeador com a hora injetada).
  2. Sem rede, os avisos já agendados disparam.
  3. Planeada iniciada, concluída ou cancelada retira os avisos pendentes.
  4. Depois de reiniciar o telemóvel os avisos continuam agendados (teste manual).
  5. Permissão de notificações negada: a faixa dentro da app continua e há um aviso para ativar.
- **Casos limite:** poupança de bateria dos fabricantes a matar a app; planeada criada no servidor depois da última sincronização (só avisa depois de sincronizar; limite inevitável do offline); mudança de fuso ou de hora; muitas planeadas no mesmo dia (agrupar numa só notificação).
- **Perguntas abertas:** hora do aviso (fixa ou configurável no backoffice, pedido a P1-T02); quem recebe (todos os operadores ou só os da zona).

## APP-04 Listagem com botão "+"

- **Utilizador:** operador.
- **Regra de negócio:** lista de intervenções do utilizador e das planeadas visíveis (filtro a confirmar), ordenada por data planeada e ordem; secções ou filtros: atrasadas, hoje, próximas, em curso, devolvidas, por enviar. Cada linha mostra equipamento, zona, tipo (preventiva ou curativa), data, estado e marca de sincronização. Botão "+" no canto superior direito (como no esboço) cria uma intervenção.
- **Critérios de aceitação:**
  1. A lista mostra os estados Planeada, EmCurso e Devolvida e os registos locais por enviar.
  2. Filtro por estado e pesquisa por equipamento ou número, sem rede.
  3. "+" abre o ecrã de criar com tipo "Curativa" por defeito (Suposição).
  4. Cada linha tem a marca de sincronização da tabela de estados (secção abaixo).
  5. Alvo de toque de pelo menos 48 dp; sem scroll horizontal; `Virtualize` em listas longas.
- **Casos limite:** lista vazia (mensagem e botão "+"); equipamento tornado obsoleto depois de planeado; muitas intervenções (desempenho); intervenção de outro técnico visível ou não.
- **Perguntas abertas:** o "+" no canto superior direito é difícil de alcançar com uma mão (Suposição: manter como no esboço e avaliar um botão flutuante no protótipo); filtro das planeadas por zona, departamento ou técnico.

## APP-05 Criar intervenção (ecrã simples e prático)

- **Utilizador:** operador.
- **Regra de negócio:** equipamento (pesquisa na cache), tipo (preventiva ou curativa), hora de início e de fim (com atalho "agora"), técnico (por defeito o utilizador), anomalia ou problema, intervenção feita, limpeza do local (como na ficha atual do PHC), foto. Guardar rascunho e concluir. O stamp da ficha gera-se no telemóvel. Interna ou externa: a app regista internas; a externa (fornecedor, folha de serviço) fica para o backoffice (BO-10) até decisão contrária (Suposição).
- **Critérios de aceitação:**
  1. Uma curativa completa grava-se em menos de 1 minuto e 6 toques principais, sem rede (medido no roteiro).
  2. Hora de fim anterior à de início é recusada com mensagem junto ao campo.
  3. Curativa exige equipamento e anomalia; preventiva exige equipamento e intervenção.
  4. O que o operador escreveu nunca se perde (rascunho local automático ao sair ou se a app for morta).
  5. Foto da câmara, comprimida, na pasta privada; câmara negada não impede o registo.
  6. Guardar offline cria 1 registo "Pendente" e 1 operação na fila; reenviar duas vezes não cria duas fichas.
- **Casos limite:** equipamento inexistente (criar equipamento é da Parte 1); foto muito grande; dois toques rápidos em "Concluir"; criar sem sessão (impossível); data de fim no futuro.
- **Perguntas abertas:** vários técnicos na mesma intervenção (Suposição: um técnico por ficha; mais pessoas no texto); limite de fotos (Suposição: 5); onde ficam as fotos (anexos do PHC ou pasta de rede, P1-T01).

## APP-06 Avançar ordem conforme a configuração do backoffice

- **Utilizador:** operador; configura o administrador (BO-05, chave por perfil, ex.: `AVANCAR_ORDEM_<ROLE>`).
- **Regra de negócio:** Suposição: as planeadas têm uma ordem (data planeada e `u_ordem`). Se o perfil não pode avançar, só inicia a primeira por executar; as outras ficam com cadeado e a razão ("Faça primeiro: <equipamento>"). Se pode, inicia qualquer. A curativa nunca é bloqueada. A configuração vem da cache.
- **Critérios de aceitação:**
  1. Sem permissão, tocar numa planeada que não é a primeira mostra o motivo e não inicia.
  2. Com permissão, inicia qualquer planeada.
  3. Concluir a primeira liberta a seguinte (também offline).
  4. Configuração alterada no backoffice aplica-se após a sincronização seguinte; a app usa o último valor conhecido sem rede.
- **Casos limite:** configuração em falta na cache (assumir o mais restritivo: sem permissão); empate de datas; planeada cancelada pela supervisão a meio da fila; ordem alterada no servidor.
- **Perguntas abertas:** a "ordem" é por equipamento, por zona ou global; o servidor deve recusar ou só sinalizar "fora de ordem" quando a configuração mudou (pedido a P1-T02).

## APP-07 Sincronização sem duplicados e com conflitos

- **Utilizador:** operador (vê o resultado), supervisor (herda o resultado).
- **Regra de negócio:** cada gravação é uma operação idempotente por stamp (`INSERT` só se não existir; `UPDATE` com condição de estado). Ordem FIFO por utilizador; foto depende da intervenção. Conflitos: **ganha a validação do backoffice**. Ficha Validada ou Cancelada no servidor: a alteração do telemóvel vira proposta guardada e avisa o técnico. Ficha devolvida: reaparece como "Devolvida". Duas pessoas a iniciar a mesma planeada: a primeira ganha e a segunda é avisada. Erros de negócio ficam em "Erro" sem travar as independentes.
- **Critérios de aceitação:**
  1. Reenviar a mesma operação duas vezes (teste) deixa uma só ficha.
  2. Matar a app durante o envio e reabrir não duplica nem perde.
  3. Cada cenário da tabela de conflitos do desenho tem um teste com o nome do cenário.
  4. Erro de ligação não conta como erro de negócio (classificação testada).
  5. Depois de enviar, a app volta a ler o que mudou; os totais batem com os do servidor.
- **Casos limite:** relógio do telemóvel errado; mesmo equipamento com duas curativas no mesmo minuto (são duas fichas, stamps diferentes); servidor lento (timeout curto, sem bloquear); fila com centenas de operações; utilizador trocou durante pendentes (a fila é dele, envia com a sua identidade).
- **Perguntas abertas:** o servidor recusa ou aceita escritas de um estado antigo (P1-T02); o que acontece a uma proposta descartada pelo técnico.

## APP-08 Peças usadas na intervenção (2.ª fase, liga à Parte 3)

- **Utilizador:** operador.
- **Regra de negócio:** na 2.ª fase, a intervenção lista as peças do plano (PEC-03) e o operador regista as usadas, por leitura de código ou escolha; a saída de stock é da Parte 3 (PEC-09). Na 1.ª fase a app só deixa o ponto de ligação (`IPecasIntervencao`, secção escondida).
- **Critérios de aceitação:**
  1. O ponto de ligação existe, com implementação vazia, sem alterar o fluxo da 1.ª fase.
  2. Ao ligar a Parte 3 (P3-T05), a secção usa a cache (peças por equipamento e plano) e a fila (operação `RegistarPeca`) sem mudar o esquema existente.
- **Casos limite:** peça sem stock; leitura de código sem rede (usa a cache); peça registada e intervenção descartada.
- **Perguntas abertas:** peças consumidas ficam na ficha (Movimentos da Ficha de Intervenção) ou em tabela própria (hipótese de P1-T01 e P3-T02).

## Estados de sincronização e offline (referência para o desenho, o protótipo e os testes)

### Estado da ligação (faixa no topo da app)

| Estado | Quando | Texto na faixa (curto) | Comportamento |
|---|---|---|---|
| Online | Ligação ao servidor boa | (sem faixa; ícone verde discreto) | Sincroniza ao abrir, ao voltar a rede, de 5 em 5 min em primeiro plano e por "Enviar agora" |
| Offline | Sem rede ou servidor inacessível (erro de ligação) | "Sem ligação. N por enviar. Dados de HH:mm" | Tudo funciona com a cache; sonda leve até voltar |
| A sincronizar | A enviar a fila ou a atualizar a cache | "A sincronizar…" | Interface livre; sem bloqueios |
| Dados antigos | Online, mas última atualização com mais de X horas (Suposição: 24) | "Dados de ontem 18:40. Atualize" | Aviso; não bloqueia |
| Bloqueado | PIN pedido (abertura ou segundo plano) | Ecrã de PIN | Nada visível sem PIN |

### Estado de cada registo (marca em cada linha e no ecrã)

| Estado | Significado | Marca | Transições | Ação do operador |
|---|---|---|---|---|
| Sincronizado | Igual ao servidor | visto verde | edição para Pendente | nenhuma |
| Pendente | Guardado no telemóvel, na fila, à espera de rede ou de vez | relógio | para A enviar; para Erro; para Conflito | "Enviar agora" opcional |
| A enviar | Operação em curso | roda | para Sincronizado, Erro ou Conflito | esperar |
| Erro de ligação (transitório) | Falhou por rede; tenta de novo com intervalo crescente | relógio com aviso | volta a A enviar | nenhuma |
| Erro de regra | O servidor recusou por regra de negócio (por exemplo, equipamento obsoleto) | ponto de exclamação vermelho | fica em Erro; as independentes seguem | Ver motivo, Repetir ou Descartar (com confirmação e role) |
| Conflito (proposta) | Ficha já Validada ou Cancelada, ou iniciada por outro | triângulo laranja | fica guardado como proposta | Ver, Descartar proposta; a supervisão decide |
| Devolvida | A supervisão devolveu a ficha para correção | seta de retorno | para EmCurso | Corrigir e concluir de novo |
| Bloqueada por ordem | Planeada fora da ordem sem permissão | cadeado | liberta quando a anterior conclui | Ver razão |

## Requisitos não funcionais desta parte

Offline em tudo; PIN só hash com sal; sem credenciais no código, nos logs nem no APK em claro (risco R-03 documentado); dados pessoais só iniciais e nome do técnico (RGPD) e fotos sem pessoas quando possível; lista em menos de 2 s com cache; erros nunca em silêncio (`CrashLogService.Registar` e aviso); SQL nível 100 com `NOLOCK` nas leituras; acessibilidade móvel (alvos de 48 dp, texto de 16 px, contraste AA, teclado nativo); sem tabelas nem campos criados pelo Claude Code.
