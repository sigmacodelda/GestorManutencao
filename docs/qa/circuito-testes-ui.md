# Circuito de testes de utilizador (Playwright, 375×812 e 1280×800)

Base: `testes_land2` (só ela; confirmar `SELECT DB_NAME()` antes de qualquer escrita). Dados criados levam o prefixo `TESTE-UI-` para limpeza posterior. Cada passo regista: resultado (OK/FALHA), captura nas falhas e a verificação UX ao lado.

Verificações UX transversais (ui-ux-pro-max), aplicadas em **todos** os passos:
- U1 Alvos de toque ≥ 44 px e espaço entre botões ≥ 8 px.
- U2 Nada se mexe ao escrever, picar ou ao aparecer um erro/aviso (erros e avisos flutuam no topo; medir deslocamento de botões = 0).
- U3 Aviso/erro flutuante: aparece, lê-se, some sozinho (6–8 s) ou ao tocar; máx. 3 em simultâneo; confirmações com botões ficam no sítio.
- U4 Foco e teclado: foco visível, ordem lógica, `Enter` submete, sem foco perdido após gravar.
- U5 Contraste (≥ 4,5:1) em claro e escuro; sem texto cortado a 375 px; sem scroll horizontal.
- U6 Estado de carregamento e de vazio claros; botão desativado enquanto grava (sem duplo envio).
- U7 Marca SigmaCode (verde #5BB572, azul #3466AF, roxo #604696, navy #1A1731; Exo 2 / Open Sans).

## A. Supervisor
| # | Passo | Esperado |
|---|---|---|
| A1 | Login com PIN errado ×2, depois certo | Erro flutuante, teclado não sobe; entra à terceira |
| A2 | Início: contadores e mosaicos | Contadores sem saltos; cada mosaico abre a sua página |
| A3 | Equipa: criar utilizador `TESTE-UI-Tec`, "Novo PIN" com lista grande | Ecrã vai ao PIN; erro de PIN fraco flutuante |
| A4 | Equipamentos: pesquisa, ficha, gestão (criar `TESTE-UI-Eq`) | Lista, ficha e gravação com toast |
| A5 | Tarefas / Instruções / Periodicidades: criar, editar, apagar (confirmação inline) | Confirmação com botões não flutua |
| A6 | Planeadas e Todas as fichas: filtros e "ver mais" | Aviso de limite flutua ou inline sem saltos |
| A7 | Configuração: guardar | Toast de sucesso |
| A8 | Inventário: ver atribuído, consultar concluído | Só leitura quando concluído |

## B. Técnico (criado em A3)
| # | Passo | Esperado |
|---|---|---|
| B1 | Login PIN certo, mudar PIN | Regras de PIN, erro flutuante |
| B2 | Registar avaria `TESTE-UI-Avaria` num equipamento | Entra em Minhas intervenções |
| B3 | Detalhe: iniciar; sem stock (campo `iniciarsemstock`) | Bloqueia com mensagem flutuante por defeito |
| B4 | Material: pesquisar peça, escolher lugar, quantidade acima do stock, repetida (soma) | Avisos flutuantes; soma na linha existente |
| B5 | Concluir com assinatura | Fica por validar |
| B6 | Arrumação: peça → lugar **por seleção** (corredor→estante→nível→posição) e lugar → peça | Quantidade ≤ por arrumar; lista atualizada |
| B7 | **Rearrumar**: peça → origem → destino por seleção → quantidade → "Mudar" | Soma ao destino, tira da origem, linha vazia desaparece |
| B8 | Inventário: contar, alterar, apagar, concluir | Linhas em `bi.lobs3`; depois só leitura |
| B9 | Offline: cortar a API, gravar, repor | "Alterações por guardar" e sincroniza |

## C. Circuito cruzado
| # | Passo | Esperado |
|---|---|---|
| C1 | Supervisor valida B5 | Técnico vê validada |
| C2 | Supervisor devolve outra; técnico volta a concluir | Estado correto nos dois lados |
| C3 | Qualidade: perfil só leitura | Sem ações de operador |

## D. Responsivo e tema
| # | Passo | Esperado |
|---|---|---|
| D1 | Repetir A2, B2, B6, B7 a 375×812 e a 1280×800 | U1–U5 |
| D2 | Modo escuro nas mesmas páginas | Contraste e avisos legíveis |

## Limpeza
`TESTE-UI-*`: fichas, utilizadores e equipamentos criados; contagens de inventário de teste. Só na `testes_land2`.
