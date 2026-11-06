const fs = require('fs');
const {
  Document, Packer, Paragraph, TextRun, ImageRun, Table, TableRow, TableCell, WidthType, ShadingType,
  AlignmentType, HeadingLevel, BorderStyle, Footer, PageNumber, LevelFormat, PageBreak, TableOfContents, VerticalAlign,
} = require('docx');

const IMG = 'C:/_SigmaRepos/GestorManutencao/Planeamento/Entrega-Cliente/img/';
const REAL = 'C:/_SigmaRepos/GestorManutencao/Planeamento/Entrega-Cliente/img-real/';
const haReal = f => fs.existsSync(REAL + f);
const OUT = 'C:/_SigmaRepos/GestorManutencao/Planeamento/Entrega-Cliente/Novo Circuito de Manutenção - Carnes Landeiro.docx';
const G = '5BB572', B = '3466AF', P = '604696', N = '1A1731', GR = 'CAC9C9';
const W = 9638;
const H = 'Exo 2', T = 'Open Sans';

const run = (t, o = {}) => new TextRun({ text: t, font: T, size: 21, color: N, ...o });
const para = (t, o = {}) => new Paragraph({ spacing: { after: 120, line: 300 }, ...o, children: Array.isArray(t) ? t : [run(t)] });
const b = t => run(t, { bold: true });
const h1 = t => new Paragraph({ heading: HeadingLevel.HEADING_1, children: [new TextRun({ text: t, font: H })] });
const h2 = t => new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun({ text: t, font: H })] });
const bullet = t => new Paragraph({ numbering: { reference: 'pt', level: 0 }, spacing: { after: 60, line: 288 }, children: Array.isArray(t) ? t : [run(t)] });
const num = (t, ref = 'n1') => new Paragraph({ numbering: { reference: ref, level: 0 }, spacing: { after: 60, line: 288 }, children: Array.isArray(t) ? t : [run(t)] });
const caption = t => new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 60, after: 200 }, children: [run(t, { italics: true, size: 18, color: '595766' })] });
const img = (f, w, h, o = {}) => new ImageRun({ type: 'png', data: fs.readFileSync(f.startsWith('REAL:') ? REAL + f.slice(5) : IMG + f), transformation: { width: w, height: h }, altText: { title: f, description: f, name: f }, ...o });
const picture = (f, wpx, ratio, cap) => [
  new Paragraph({ alignment: AlignmentType.CENTER, keepNext: true, spacing: { before: 120, after: 0 }, children: [img(f, wpx, Math.round(wpx * ratio))] }),
  caption(cap),
];
const border = { style: BorderStyle.SINGLE, size: 4, color: GR };
const borders = { top: border, bottom: border, left: border, right: border };
const cell = (kids, w, o = {}) => new TableCell({
  width: { size: w, type: WidthType.DXA }, borders, verticalAlign: VerticalAlign.CENTER,
  margins: { top: 80, bottom: 80, left: 120, right: 120 }, ...o,
  children: Array.isArray(kids) ? kids : [para(kids, { spacing: { after: 0, line: 276 } })],
});
function table(widths, head, rows) {
  const hr = new TableRow({ tableHeader: true, children: head.map((t, i) => cell([para([run(t, { bold: true, color: 'FFFFFF' })], { spacing: { after: 0 } })], widths[i], { shading: { type: ShadingType.CLEAR, fill: N, color: 'auto' } })) });
  const rr = rows.map((r, k) => new TableRow({ cantSplit: true, children: r.map((t, i) => cell(
    [para(Array.isArray(t) ? t : [run(t, i === 0 ? { bold: true } : {})], { spacing: { after: 0, line: 276 } })], widths[i],
    { shading: { type: ShadingType.CLEAR, fill: k % 2 ? 'F6F6F7' : 'FFFFFF', color: 'auto' } })) }));
  return new Table({ width: { size: W, type: WidthType.DXA }, columnWidths: widths, rows: [hr, ...rr] });
}
const spacer = () => new Paragraph({ spacing: { after: 120 }, children: [] });
const callout = (title, text, fill = 'EEF3FA', bar = B) => new Table({
  width: { size: W, type: WidthType.DXA }, columnWidths: [W], rows: [new TableRow({ cantSplit: true, children: [new TableCell({
    width: { size: W, type: WidthType.DXA }, shading: { type: ShadingType.CLEAR, fill, color: 'auto' },
    borders: { top: { style: BorderStyle.NONE, size: 0, color: 'FFFFFF' }, bottom: { style: BorderStyle.NONE, size: 0, color: 'FFFFFF' }, right: { style: BorderStyle.NONE, size: 0, color: 'FFFFFF' }, left: { style: BorderStyle.SINGLE, size: 24, color: bar } },
    margins: { top: 120, bottom: 120, left: 200, right: 160 },
    children: [para([run(title + ' ', { bold: true }), run(text)], { spacing: { after: 0, line: 288 } })],
  })] })],
});
// três telemóveis lado a lado
function phones(items) {
  const w = Math.floor(W / items.length);
  const dims = f => { const b = fs.readFileSync(REAL + f.slice(5)); return [b.readUInt32BE(16), b.readUInt32BE(20)]; };
  return new Table({
    width: { size: W, type: WidthType.DXA }, columnWidths: items.map(() => w),
    rows: [new TableRow({ cantSplit: true, children: items.map(([f]) => new TableCell({
      width: { size: w, type: WidthType.DXA }, borders: { top: { style: BorderStyle.NONE, size: 0, color: 'FFFFFF' }, bottom: { style: BorderStyle.NONE, size: 0, color: 'FFFFFF' }, left: { style: BorderStyle.NONE, size: 0, color: 'FFFFFF' }, right: { style: BorderStyle.NONE, size: 0, color: 'FFFFFF' } },
      children: [new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 0 }, children: [(() => { const [iw, ih] = dims(f); const pw = 180; return img(f, pw, Math.round(pw * ih / iw)); })()] })] })) }),
    new TableRow({ children: items.map(([, c]) => new TableCell({
      width: { size: w, type: WidthType.DXA }, borders: { top: { style: BorderStyle.NONE, size: 0, color: 'FFFFFF' }, bottom: { style: BorderStyle.NONE, size: 0, color: 'FFFFFF' }, left: { style: BorderStyle.NONE, size: 0, color: 'FFFFFF' }, right: { style: BorderStyle.NONE, size: 0, color: 'FFFFFF' } },
      children: [caption(c)] })) })],
  });
}

const children = [];
// ---------- Capa
children.push(new Table({
  width: { size: W, type: WidthType.DXA }, columnWidths: [W],
  rows: [new TableRow({ children: [new TableCell({
    width: { size: W, type: WidthType.DXA }, shading: { type: ShadingType.CLEAR, fill: N, color: 'auto' },
    borders: { top: { style: BorderStyle.NONE, size: 0, color: N }, bottom: { style: BorderStyle.SINGLE, size: 36, color: G }, left: { style: BorderStyle.NONE, size: 0, color: N }, right: { style: BorderStyle.NONE, size: 0, color: N } },
    margins: { top: 900, bottom: 900, left: 500, right: 500 },
    children: [
      new Paragraph({ spacing: { after: 300 }, children: [new TextRun({ text: 'SigmaCode', font: H, bold: true, size: 30, color: G })] }),
      new Paragraph({ spacing: { after: 120 }, children: [new TextRun({ text: 'O NOVO CIRCUITO', font: H, bold: true, size: 64, color: 'FFFFFF' })] }),
      new Paragraph({ spacing: { after: 300 }, children: [new TextRun({ text: 'DE MANUTENÇÃO', font: H, bold: true, size: 64, color: 'FFFFFF' })] }),
      new Paragraph({ spacing: { after: 0 }, children: [new TextRun({ text: 'Como vai funcionar, do backoffice ao telemóvel e ao PHC', font: T, size: 26, color: GR })] }),
    ] })] })],
}));
children.push(spacer(), spacer());
children.push(para([run('Carnes Landeiro, SA', { bold: true, size: 28 })]));
children.push(para('Novembro de 2026 · versão 1.0 para apresentação'));
children.push(spacer());
children.push(callout('Sobre os ecrãs deste documento.', 'São do protótipo navegável, com dados fictícios (nomes de máquinas e números de exemplo). Mostram como o circuito vai funcionar; o aspeto final pode ter pequenos ajustes.'));
children.push(spacer());
children.push(new Paragraph({ children: [new PageBreak()] }));
children.push(new Paragraph({ spacing: { after: 160 }, children: [new TextRun({ text: 'Índice', font: H, bold: true, size: 32, color: N })] }));
children.push(new TableOfContents('Índice', { hyperlink: true, headingStyleRange: '1-2' }));
children.push(para([run('Se o índice aparecer vazio, clique nele com o botão direito e escolha "Atualizar campo".', { italics: true, size: 18, color: '595766' })]));
children.push(new Paragraph({ children: [new PageBreak()] }));

// ---------- 1
children.push(h1('1. Em resumo'));
children.push(para('Hoje a manutenção vive no PHC e a ficha de intervenção é usada todos os dias. No novo circuito, o PHC continua a ser a base: mantêm-se o histórico, as infraestruturas e a ligação das faturas às fichas. O que muda é que a manutenção passa a ser planeada, avisada e validada, com o técnico a trabalhar no telemóvel e o responsável a acompanhar num backoffice.'));
children.push(table([2500, 3569, 3569], ['', 'Hoje', 'No novo circuito'], [
  ['Planeamento', 'Planos escritos sem datas calculadas', 'O sistema calcula a próxima data de cada tarefa e cria a intervenção planeada'],
  ['Avisos', 'Só existem para as viaturas', 'Alerta de cada intervenção planeada, com a antecedência que o backoffice definir'],
  ['Técnico', 'Registo no ecrã do PHC, no posto', 'Lista de trabalho no telemóvel, registo no local, com fotos, mesmo sem rede'],
  ['Fecho', '"Fechada" só quer dizer que a fatura foi ligada', 'O responsável valida (ou devolve para correção) cada intervenção'],
  ['Acompanhamento', 'Consulta nas fichas', 'Dashboard com planeadas, feitas, atrasadas, avarias e custos'],
  ['Peças (2.ª fase)', 'Texto livre', 'Peças por máquina, stock, leitura de QR e alerta de rutura'],
]));
children.push(spacer());

// ---------- 2
children.push(h1('2. As três peças e como falam entre si'));
children.push(para('O circuito tem três peças. O técnico trabalha na app, o responsável trabalha no backoffice e tudo fica gravado no PHC, onde já está o histórico.', { keepNext: true }));
children.push(...picture('fluxo-d1.png', 640, 850 / 2000, 'Figura 1 · As peças do circuito e como comunicam'));
children.push(table([2200, 2800, 4638], ['Peça', 'Quem usa', 'Para quê'], [
  ['App móvel', 'Técnicos', 'Ver as intervenções planeadas, registar o que foi feito, criar uma avaria, tirar fotos'],
  ['Backoffice', 'Responsável de manutenção e supervisão', 'Equipamentos, planos, planeamento, configuração dos alertas, validação e dashboard'],
  ['PHC', 'Toda a empresa (Compras incluídas)', 'Guarda os dados e o histórico; a fatura do fornecedor liga-se à ficha como hoje'],
]));
children.push(new Paragraph({ children: [new PageBreak()] }));

// ---------- 3
children.push(h1('3. O ciclo da manutenção preventiva'));
children.push(para('Este é o caminho de uma tarefa planeada, desde o plano até ficar validada. Cada cor mostra onde acontece: azul no backoffice, roxo no PHC, verde na app.', { keepNext: true }));
children.push(...picture('fluxo-d2.png', 620, 1590 / 2000, 'Figura 2 · Ciclo da manutenção preventiva'));
children.push(num([b('Plano. '), run('O responsável define, para cada equipamento, as tarefas e a periodicidade (mensal, trimestral, anual…).')], 'n1'));
children.push(num([b('Planeamento. '), run('O sistema calcula a próxima data e cria a intervenção planeada.')], 'n1'));
children.push(num([b('Alerta. '), run('O técnico recebe o aviso com a antecedência configurada (no protótipo, 2 dias).')], 'n1'));
children.push(num([b('Execução. '), run('O técnico abre a intervenção, inicia-a e executa o trabalho.')], 'n1'));
children.push(num([b('Conclusão. '), run('Regista tempos, notas, fotos e a limpeza do local.')], 'n1'));
children.push(num([b('Validação. '), run('O supervisor valida ou devolve com uma nota; se devolver, volta ao técnico.')], 'n1'));
children.push(num([b('Fecho. '), run('Fica validada no PHC e a próxima data da tarefa é recalculada.')], 'n1'));
children.push(new Paragraph({ children: [new PageBreak()] }));

// ---------- 4 backoffice
children.push(h1('4. O backoffice, ecrã a ecrã'));
children.push(para('O backoffice é o módulo Manutenção do LandeiroMaui, que a equipa já usa no posto. Trabalha sobre as mesmas infraestruturas e fichas do PHC. Abaixo, os ecrãs como estão hoje, e no fim o que se acrescenta neste circuito.'));
children.push(h2('4.1 Equipamentos'));
children.push(para('Lista dos equipamentos com filtros e indicadores: ativos, com intervenções em aberto, fora de serviço e sem intervenção há mais de um ano. Mostra a última intervenção e quantas estão abertas.', { keepNext: true }));
children.push(...picture('REAL:m-1-equipamentos.png', 580, 1200 / 1920, 'Figura 3 · Equipamentos'));
children.push(h2('4.2 Ficha do equipamento e plano de manutenção'));
children.push(para('Cada equipamento tem os dados, as instruções de manutenção (o plano, com as tarefas e a periodicidade), as intervenções e o histórico de estado. É das tarefas do plano que nascem as intervenções planeadas.', { keepNext: true }));
children.push(...picture('REAL:m-5-equipamento-ficha.png', 580, 1200 / 1920, 'Figura 4 · Ficha do equipamento'));
children.push(h2('4.3 Intervenções planeadas'));
children.push(para('As intervenções criadas a partir dos planos, com o estado (Planeada, Atrasada, Fora de ordem). Os indicadores mostram as que estão em aberto, fora de prazo, a vencer nos próximos dias e em curso. "Gerar planeadas" cria as seguintes; cada uma pode ser iniciada ou cancelada com motivo.', { keepNext: true }));
children.push(...picture('REAL:m-2-planeadas.png', 580, 1200 / 1920, 'Figura 5 · Intervenções planeadas'));
children.push(h2('4.4 Ficha de intervenção'));
children.push(para('A ficha acompanha a intervenção do início ao fim: Planeada, Em curso, Concluída e Validada (ou Cancelada). Guarda o equipamento, o técnico, as horas, o que aconteceu e o que foi feito, a manutenção interna ou externa e os custos.', { keepNext: true }));
children.push(...picture('REAL:m-6-ficha-intervencao.png', 580, 1200 / 1920, 'Figura 6 · Ficha de intervenção'));
children.push(h2('4.5 Validação'));
children.push(para('O supervisor vê as intervenções concluídas e valida (a ficha fecha) ou devolve com o motivo, e a ficha volta a Em curso. O número no menu mostra quantas faltam validar.', { keepNext: true }));
children.push(...picture('REAL:m-4-validacao.png', 580, 1200 / 1920, 'Figura 7 · Intervenções por validar'));
children.push(h2('4.6 Configuração'));
children.push(para('Em Configurações, Manutenção, definem-se as regras: dias de antecedência do aviso, geração automática das planeadas, se se pode avançar fora de ordem, se se pode validar a própria intervenção e se a manutenção externa exige fornecedor. A tabela de periodicidades tem a unidade e o intervalo de cada uma, que permitem calcular as datas.', { keepNext: true }));
children.push(...picture('REAL:m-7-configuracao.png', 580, 1200 / 1920, 'Figura 8 · Configuração da manutenção'));
children.push(h2('4.7 O que este circuito acrescenta'));
children.push(table([3000, 6638], ['Novo', 'O que faz'], [
  ['Dashboard de manutenção', 'Planeadas, feitas, atrasadas e por validar; preventivas contra curativas; equipamentos com mais avarias; custos de mão de obra e peças.'],
  ['Alertas', 'Aviso ao técnico e ao responsável com a antecedência definida na configuração. Canal e destinatários a confirmar.'],
  ['Perfis do módulo', 'Cada pessoa vê e faz o que o seu perfil permite (técnico, supervisor, administrador).'],
  ['Atribuição de técnico', 'A planeada pode ser atribuída a um técnico, que a vê na app.'],
  ['Peças e stock (2.ª fase)', 'Peças por intervenção, stock e alerta de rutura.'],
]));
children.push(new Paragraph({ children: [new PageBreak()] }));

// ---------- 5 app
children.push(h1('5. A app do técnico'));
children.push(para('A app é simples de propósito: poucos botões, letras grandes e nada que obrigue a escrever muito. Cada pessoa vê o que o seu perfil permite: o técnico trabalha as suas intervenções; o supervisor valida e gere a equipa.', { keepNext: true }));
children.push(h2('5.1 Entrar e ver o trabalho'));
children.push(bullet([b('Entrar com PIN. '), run('Teclado numérico no ecrã, sem password. O supervisor cria o técnico e dá-lhe um PIN provisório; no primeiro acesso o técnico escolhe o seu.')]));
children.push(bullet([b('Início. '), run('Cumprimenta pelo nome e mostra os atalhos: As minhas intervenções, Registar avaria e Pedir peça (2.ª fase). Quem é supervisor vê também Planeadas, Validação e Equipa.')]));
children.push(bullet([b('As minhas intervenções. '), run('Agrupadas, com o equipamento, a zona, o tipo (avaria ou preventiva) e a data. As atrasadas ficam destacadas. O botão + regista uma avaria.')]));
children.push(bullet([b('Fazer a intervenção. '), run('O técnico abre a intervenção, toca em Começar, escreve o que fez, marca as horas de início e fim (o botão "Agora" preenche a hora) e toca em Feito. O supervisor confirma depois.')]));
children.push(bullet([b('Registar avaria. '), run('Procura o equipamento pelo nome, escreve o problema e guarda. Cria uma intervenção curativa.')]));
children.push(bullet([b('Supervisor. '), run('Valida as intervenções concluídas (não pode validar a que ele próprio fez) e cria os técnicos, com PIN provisório.')]));
if (haReal('app-1-login.png') && haReal('app-2-inicio.png') && haReal('app-3-intervencoes.png')) {
  children.push(phones([['REAL:app-1-login.png', 'Entrada com PIN'], ['REAL:app-2-inicio.png', 'Início'], ['REAL:app-3-intervencoes.png', 'As minhas intervenções']]));
}
if (haReal('app-4-detalhe.png') && haReal('app-5-avaria.png') && haReal('app-6-validacao.png')) {
  children.push(phones([['REAL:app-4-detalhe.png', 'Fazer a intervenção'], ['REAL:app-5-avaria.png', 'Registar avaria'], ['REAL:app-6-validacao.png', 'Validação (supervisor)']]));
}
children.push(spacer());
children.push(h2('5.2 Sem rede'));
children.push(para('Na fábrica nem sempre há rede. Faz parte da 1.ª fase a app guardar as intervenções no telemóvel e enviá-las sozinha quando a ligação volta, sem duplicar nada, mostrando sempre quantos registos faltam enviar.'));
children.push(...picture('fluxo-d4.png', 640, 530 / 2000, 'Figura 9 · Trabalhar sem rede e sincronizar depois'));
children.push(new Paragraph({ children: [new PageBreak()] }));

// ---------- 6 avaria
children.push(h1('6. Quando há uma avaria'));
children.push(para('A avaria é registada no local, pelo técnico, em poucos toques. O supervisor vê-a no backoffice e, se foi um fornecedor a resolver, a fatura continua a ligar-se à ficha no ecrã de Compras do PHC, como hoje.', { keepNext: true }));
children.push(...picture('fluxo-d3.png', 640, 670 / 2000, 'Figura 10 · Circuito de uma avaria (curativa)'));
children.push(callout('A ligação às faturas não muda.', 'Quem lança as compras continua a usar as mesmas teclas de atalho do PHC (F4 para ligar a compra toda, F8 linha a linha) e os custos continuam a somar-se na ficha.', 'F3F0F8', P));
children.push(spacer());

// ---------- 7 PHC
children.push(h1('7. O que fica no PHC'));
children.push(table([3000, 6638], ['No PHC', 'O que acontece'], [
  ['Infraestruturas', 'Continuam a ser a base de todos os equipamentos, máquinas e veículos. Ganham departamento e zona.'],
  ['Planos de manutenção', 'Os planos existentes passam a ter periodicidade com tipo e intervalo, para poder calcular datas. A lista de periodicidades é limpa em conjunto com a Landeiro.'],
  ['Fichas de intervenção', 'O backoffice e a app gravam nas mesmas fichas. O histórico de cerca de 17 700 fichas mantém-se.'],
  ['Compras', 'A fatura do fornecedor liga-se à ficha como hoje. Não há alterações ao circuito de Compras.'],
  ['Alertas das viaturas', 'Continuam como hoje (inspeção, seguro, tacógrafo…).'],
]));
children.push(spacer());

// ---------- 8 estados
children.push(h1('8. Estados de uma intervenção'));
children.push(para('Cada intervenção passa por estados claros, e o estado de validação fica separado do estado das faturas: "validada" quer dizer que o responsável confirmou o trabalho.', { keepNext: true }));
children.push(...picture('fluxo-d5.png', 620, 510 / 2000, 'Figura 11 · Estados da intervenção'));


// ---------- 9 pecas
children.push(h1('9. Peças e stock (2.ª fase)'));
children.push(para('Na 2.ª fase a app passa a mostrar as peças necessárias de cada intervenção e o armazém passa a ter stock controlado. O técnico dá saída da peça lendo o código QR ou de barras com o telemóvel, e o sistema avisa quando uma peça vai faltar, contando com o prazo de entrega do fornecedor.', { keepNext: true }));
children.push(...picture('fluxo-d6.png', 640, 690 / 2000, 'Figura 12 · Das peças necessárias à saída do armazém'));
children.push(...picture('arm-saida.png', 560, 1800 / 2560, 'Figura 13 · Saída de peças na intervenção (protótipo)'));
children.push(...picture('arm-alt.png', 560, 1800 / 2560, 'Figura 14 · Alertas de rutura de stock (protótipo)'));
children.push(table([2500, 7138], ['Em desenho', 'Como vai funcionar'], [
  ['Peças por máquina', 'Cada peça é registada uma vez, pelo código do fabricante, e ligada às máquinas onde se usa.'],
  ['Receção e etiquetas', 'Registo das peças que chegam e etiquetas para as peças e para os locais de arrumação.'],
  ['Inventário', 'Contagem por leitura (picagem) no telemóvel.'],
  ['Alerta de rutura', 'Cruza as intervenções planeadas, o stock e o prazo de entrega.'],
]));
children.push(new Paragraph({ children: [new PageBreak()] }));

// ---------- 10 dia a dia
children.push(h1('10. Um dia no novo circuito (exemplo)'));
children.push(table([1500, 2300, 5838], ['Quando', 'Quem', 'O que acontece'], [
  ['Segunda, 08:00', 'Técnico', 'Abre a app com o PIN. Vê 3 planeadas nos próximos 2 dias e uma delas atrasada. Começa pela primeira da lista.'],
  ['Segunda, 08:40', 'Técnico', 'Termina a lubrificação, escreve o que fez, marca o fim com "Agora" e toca em Feito. Não há rede na zona: fica guardada no telemóvel.'],
  ['Segunda, 10:15', 'Técnico', 'Regressa à zona com rede. A app envia sozinha e mostra "Sincronizado".'],
  ['Segunda, 10:20', 'Supervisor', 'No backoffice vê "1 por validar", abre a ficha e valida. A próxima data da tarefa é recalculada.'],
  ['Terça, 14:00', 'Técnico', 'A câmara de frio sobe de temperatura. Toca em Registar avaria, escolhe a máquina e escreve o problema.'],
  ['Terça, 16:30', 'Compras', 'Chega a fatura do técnico externo. No PHC liga-a à ficha com F4 e o custo soma-se.'],
  ['Fim do mês', 'Responsável', 'Abre o dashboard: cumprimento do mês, equipamentos com mais avarias e custos.'],
]));
children.push(spacer());

// ---------- 11 fases
children.push(h1('11. Faseamento'));
children.push(table([2200, 7438], ['Fase', 'O que entra'], [
  ['1.ª fase', 'Backoffice (equipamentos, planos, planeadas, validação e configuração já disponíveis; a acrescentar dashboard e alertas) e app móvel (PIN, lista, registo, avaria, validação e funcionamento sem rede).'],
  ['2.ª fase', 'Peças na app, armazém, etiquetas, receção, inventário, saída por leitura e alerta de rutura.'],
]));
children.push(para('As datas acordam-se depois de validado este circuito.', { spacing: { before: 120, after: 120 } }));

// ---------- 12 confirmar
children.push(h1('12. O que precisamos de confirmar convosco'));
children.push(para('O circuito está definido nas suas linhas gerais. Estes pontos afinam o resultado e queremos fechá-los consigo:'));
children.push(table([600, 5000, 4038], ['#', 'A confirmar', 'O que sugerimos'], [
  ['1', 'Quem valida as intervenções e quem recebe os alertas', 'Responsável de manutenção a validar; destinatários a indicar pela Landeiro'],
  ['2', 'Limpeza da lista de periodicidades (por exemplo, "Bianual" quer dizer o quê?)', 'Sessão conjunta antes de arrancar'],
  ['3', 'Os planos atuais continuam válidos?', 'Rever primeiro os dos equipamentos principais'],
  ['4', 'Como avisar: no ecrã, por email ou por notificação no telemóvel', 'Seguir o modelo dos alertas das viaturas'],
  ['5', 'O alcance da permissão para avançar uma intervenção antes de outra', 'Confirmar na reunião'],
  ['6', 'Manutenção por horas de funcionamento ou quilómetros', 'Começar pelas de calendário'],
  ['7', 'Peças: artigos no PHC (a 2.ª fase) e registo de tempos e peças internas', 'Decidir com a equipa de stock'],
]));
children.push(spacer());

// ---------- 13 glossario e contactos
children.push(h1('13. Glossário'));
children.push(table([2600, 7038], ['Termo', 'O que quer dizer'], [
  ['Preventiva / Curativa', 'Preventiva: prevista no plano. Curativa: para resolver uma avaria.'],
  ['Planeada', 'Intervenção criada pelo sistema a partir do plano, ainda por fazer.'],
  ['Validar', 'O responsável confirma que a intervenção está bem registada.'],
  ['Sem rede', 'A app funciona sem internet e envia os dados quando a ligação volta.'],
  ['Sincronizar', 'Trocar dados entre o telemóvel e o backoffice.'],
  ['Picagem', 'Ler o código de barras ou QR de uma peça ou local com o telemóvel.'],
]));
children.push(spacer());
children.push(h1('14. Contactos'));
children.push(para([b('SigmaCode')], { spacing: { after: 40 } }));
children.push(para('Telefone: +351 220 193 309 (chamada para a rede fixa nacional)', { spacing: { after: 40 } }));
children.push(para('Email: suporte@sigmacode.pt', { spacing: { after: 40 } }));
children.push(para('Morada: Rua Nova de Mira, nº 220, 4410-377 Arcozelo, V. N. Gaia', { spacing: { after: 40 } }));
children.push(para('Site: sigmacode.pt'));

const doc = new Document({
  creator: 'SigmaCode', title: 'O Novo Circuito de Manutenção - Carnes Landeiro',
  styles: {
    default: { document: { run: { font: T, size: 21, color: N } } },
    paragraphStyles: [
      { id: 'Heading1', name: 'Heading 1', basedOn: 'Normal', next: 'Normal', quickFormat: true, run: { font: H, size: 36, bold: true, color: B }, paragraph: { spacing: { before: 360, after: 160 }, keepNext: true, outlineLevel: 0 } },
      { id: 'Heading2', name: 'Heading 2', basedOn: 'Normal', next: 'Normal', quickFormat: true, run: { font: H, size: 26, bold: true, color: P }, paragraph: { spacing: { before: 240, after: 100 }, keepNext: true, outlineLevel: 1 } },
    ],
  },
  numbering: { config: [
    { reference: 'pt', levels: [{ level: 0, format: LevelFormat.BULLET, text: '•', alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 540, hanging: 270 } } } }] },
    { reference: 'n1', levels: [{ level: 0, format: LevelFormat.DECIMAL, text: '%1.', alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 540, hanging: 360 } } } }] },
  ] },
  sections: [{
    properties: { page: { size: { width: 11906, height: 16838 }, margin: { top: 1134, right: 1134, bottom: 1134, left: 1134, header: 567, footer: 567 } } },
    footers: { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER, border: { top: { style: BorderStyle.SINGLE, size: 6, color: GR, space: 6 } }, children: [
      new TextRun({ text: 'SigmaCode · Novo circuito de manutenção · Carnes Landeiro, SA · página ', font: T, size: 17, color: '595766' }),
      new TextRun({ children: [PageNumber.CURRENT], font: T, size: 17, color: '595766' }),
    ] })] }) },
    children,
  }],
});
Packer.toBuffer(doc).then(buf => { fs.writeFileSync(OUT, buf); console.log('ok', buf.length); });
