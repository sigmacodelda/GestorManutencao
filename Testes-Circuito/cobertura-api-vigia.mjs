// Vigia o backend e imprime só o que mudou (fichas, peças, lugares, inventários, qualidade). Uso: node cobertura-api-vigia.mjs [segundos]
import { http, entrar, itens } from './cobertura-api.mjs';
const dur = Number(process.argv[2] ?? 60) * 1000;
const { t, s, q } = await entrar();
async function estado() {
  const o = {};
  const ps = (await http(t, 'GET', '/pecas')).j?.pecas ?? [];
  for (const p of ps) {
    const l = (await http(t, 'GET', `/pecas/${p.ref}/localizacoes`)).j;
    o[`peca ${p.ref}`] = `stock=${p.stock} emConf=${p.emConferencia} disp=${p.disponivel} porArrumar=${l?.porArrumar} localizado=${l?.localizado} lugares=${(l?.lugares ?? []).map((x) => `${x.texto}:${x.qtt}(res${x.reservado})`).join(',')}`;
  }
  for (const i of (await http(s, 'GET', '/inventarios')).j ?? []) {
    const d = (await http(s, 'GET', `/inventarios/${i.id}`)).j;
    o[`inv ${i.id.slice(0, 6)}`] = `n${i.numero} concl=${i.concluido} lanc=${i.lancado} linhas=${(d?.linhas ?? []).map((x) => `${x.ref}@${x.lugar}=${x.qtt}`).join(';')}`;
  }
  for (const f of itens((await http(s, 'GET', '/intervencoes/todas')).j)) o[`ficha ${f.id.slice(0, 8)}`] = `n${f.numero} ${f.estado} ${f.equipamento} tec=${f.tecnico ?? '-'} fechada=${f.fechada}`;
  o['qualidade'] = ((await http(q, 'GET', '/qualidade')).j ?? []).map((f) => f.id.slice(0, 8)).join(',');
  o['validacao'] = ((await http(s, 'GET', '/validacao')).j ?? []).map((f) => f.id.slice(0, 8)).join(',');
  return o;
}
let ant = await estado();
if (process.argv.includes('--inicial')) for (const [k, v] of Object.entries(ant)) console.log(`= ${k}: ${v}`);
const fim = Date.now() + dur;
while (Date.now() < fim) {
  await new Promise((r) => setTimeout(r, 4000));
  const novo = await estado();
  for (const k of new Set([...Object.keys(ant), ...Object.keys(novo)])) if (ant[k] !== novo[k]) console.log(`${new Date().toLocaleTimeString()} ${k}: ${ant[k] ?? '(novo)'} -> ${novo[k] ?? '(removido)'}`);
  ant = novo;
}
