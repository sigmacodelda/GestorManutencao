# Resumo da reunião com a Informática Landeiro (04/11/2026)

> Nota (2026-11-06): este resumo regista o que foi dito em 04/11/2026 e não se reescreve. Desde então o planeamento segue a base da LandeiroMaui (`../base-landeiromaui.md`), que prevalece: o circuito de manutenção (estados, validação, configuração, material usado em `u_matinterv`) já existe no PHC; o armazém, o stock e as etiquetas da secção 3 são o que é NOVO face a essa base.

Fonte: gravação "Chamada com Informática Landeiro" (24 min, 14:53), transcrição automática (`transcricao-bruta.txt`) e frames do ecrã partilhado (`../fontes/video-frames/`).
Participantes visíveis: equipa da Informática Landeiro (sala) e Osvaldo Amaral (SigmaCode). Os nomes citados na conversa (Luís, Rui, Vinâncio, Mário) vêm de áudio difícil: **Suposição:** Luís e Rui são da Landeiro (Word do plano e circuito PHC, como está no quadro); Vinâncio faz as compras.

## 1. O que foi mostrado (circuito atual no PHC)

Menu: Opções Internas → Manutenção → ecrã **Infraestruturas** (equipamentos). Detalhe em [`../circuito-phc-atual.md`](../circuito-phc-atual.md).

- Cada equipamento é uma "infraestrutura" com número (exemplo: 79 Depiladora Suínos, 248 Enchedora REX Tecnologie 460) e abas Dados principais, Outros Dados, Instr. Manut., Fichas Interv., Via Verde, Histórico Status.
- As intervenções são "Fichas de Intervenção" (ecrã 3), ligadas ao equipamento: periodicidade (semestral, mensal…), tipo de manutenção (Interna ou Externa), tipo de intervenção (Preventiva ou Curativa), anomalia/problema, intervenção feita, fornecedor, mão de obra e materiais/peças.
- Hoje a ficha é feita **em papel** e depois alguém passa-a para o PHC. Quando o supervisor valida, a ficha fica **Fechada**.
- Para repetir uma intervenção periódica copia-se a ficha anterior (duplicar) e muda-se a data.
- Manutenção **externa**: regista-se o fornecedor e o documento externo / folha de serviço (a ficha que o técnico externo deixa). A fatura é outro assunto.
- O plano de manutenção vive hoje num **Word** (P.05-03 Plano de Manutenção de Equipamentos, v1.4, 2026): nº, designação, zona (Abate, Salsicharia…), interna ou externa, e os meses com "x" (mensal, trimestral…).

## 2. O que foi decidido ou combinado

1. Há dois tipos de intervenção: **preventiva** (planeada) e **curativa** (avaria). O tipo é escolhido na ficha.
2. A app móvel substitui o papel: o operador regista no telemóvel e o supervisor valida no backoffice (a ficha só fecha quando validada).
3. **Prazos:** **11/11/2026** a SigmaCode traz um protótipo ("espelho") para validar a viabilidade; **17/11/2026** (Suposição: foi o dia dito) a Landeiro precisa de algo utilizável para começar a registar as primeiras intervenções nos equipamentos novos (lubrificação, troca de peças). A Landeiro está em auditoria interna e em fase de implementação.
4. O Luís envia por e-mail o Word do plano de manutenção (pedido feito na reunião).

## 3. Peças e stock (2.ª fase)

- A Landeiro **não tem controlo de stock de peças nem referências** das peças de manutenção. Estimativa dita: 3.300 a 4.000 peças, só o equipamento Midas tem cerca de 300.
- Origem das peças: manuais do fabricante (Marel, projeto "Slaughterline Renovation", PDF de 350 MB, em inglês misturado com português). Cada conjunto abre uma lista de peças com código do fabricante (começa por 7 ou por 5…).
- **Referências no PHC:** guardar a referência do fabricante e criar uma referência interna da Landeiro. Proposta discutida: família própria (stock de serviço), separada das mercadorias, com código hierárquico por máquina (exemplo: 1 = Midas; 2 = mesa de sangria; 3 = escaldão; 4 = chamusco; seguintes = lavador…; dentro de cada máquina as peças 001, 001.1, 001.2…). A Landeiro quer manter a designação original em inglês e ter a tradução ao lado.
- **Fluxo proposto:** compra → receção (entrada de stock) → consumo no telemóvel durante a intervenção → alerta de reposição. O consumo só faz sentido se a entrada existir (procedimento: comprar, receber, consumir). Quem dá a entrada (Compras ou manutenção) ficou por fechar.
- Leitura de código: se a peça traz código de barras do fabricante, lê-se esse; se não, etiqueta própria.
- **Alerta de rutura:** a ideia é receber um aviso "é preciso comprar a peça X" cruzando intervenções planeadas, stock e prazo de entrega.
- A SigmaCode (Osvaldo) vai usar IA para separar as peças por máquina a partir do PDF do fabricante e traduzir.
- Foi dito que, sem a parte do stock, a solução fica incompleta. Por isso a 2.ª fase é obrigatória, mas não bloqueia a 1.ª.

## 4. Pendências levantadas na reunião

| # | Pendência | Dono (Suposição) |
|---|---|---|
| 1 | Enviar o Word do plano de manutenção (P.05-03) | Luís (Landeiro) |
| 2 | Quem dá entrada das peças no PHC (Compras ou manutenção) | Landeiro |
| 3 | Criar família e referências das peças no PHC (ou ambiente separado) | Landeiro + SigmaCode |
| 4 | Extrair peças por máquina do PDF Marel (IA) e traduzir | Osvaldo (SigmaCode) |
| 5 | Protótipo para validar em 11/11/2026 | SigmaCode |
| 6 | Conhecer o circuito PHC em detalhe | Rui (Landeiro) |

## 5. Limitações

A transcrição tem erros (por exemplo "midas" pode ser outro equipamento, "Vinâncio" pode ser outro nome). Os factos acima só entram no planeamento marcados como Suposição quando dependem de áudio duvidoso. Os ecrãs do PHC foram confirmados pelas imagens do vídeo.
