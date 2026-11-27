// Cobertura da API por HTTP (modo memória). Uso: node cobertura-api.mjs snap | node cobertura-api.mjs testes
// Os PINs de teste vêm das variáveis PIN_TECNICO, PIN_SUPERVISOR, PIN_QUALIDADE (por defeito os do modo memória).
import fs from 'node:fs';
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';
const API = (process.env.API_URL ?? 'https://localhost:7257').replace(/\/$/, '');
if (!/^https:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(API)) { console.error('API_URL tem de ser https local.'); process.exit(2); }
export const PINS = { t: process.env.PIN_TECNICO ?? '4829', s: process.env.PIN_SUPERVISOR ?? '7953', q: process.env.PIN_QUALIDADE ?? '3197' };
export async function http(token, metodo, rota, corpo, extra = {}) {
  const r = await fetch(API + rota, { method: metodo, headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(corpo !== undefined ? { 'Content-Type': 'application/json' } : {}), ...extra }, body: corpo !== undefined ? (typeof corpo === 'string' ? corpo : JSON.stringify(corpo)) : undefined });
  const tx = await r.text(); let json = null; try { json = JSON.parse(tx); } catch { json = tx; }
  return { s: r.status, j: json, h: r.headers };
}
export async function entrar() {
  const o = {};
  for (const k of Object.keys(PINS)) { const r = await http(null, 'POST', '/auth/login', { pin: PINS[k] }); if (r.s !== 200) throw new Error(`login ${k} ${r.s}`); o[k] = r.j.token; }
  return o;
}
export const itens = (x) => (Array.isArray(x) ? x : x?.itens ?? []);

async function snap() {
  const tk = await entrar();
  const pecas = (await http(tk.t, 'GET', '/pecas')).j;
  const out = { pecas: [], inventarios: [], fichas: [], qualidade: [], validacao: [] };
  for (const p of pecas.pecas) {
    const l = (await http(tk.t, 'GET', `/pecas/${p.ref}/localizacoes`)).j;
    out.pecas.push({ ref: p.ref, design: p.design, codigo: p.codigo, stock: p.stock, emConf: p.emConferencia, disp: p.disponivel, localPeca: p.local, porArrumar: l.porArrumar, localizado: l.localizado, lugares: (l.lugares ?? []).map((x) => `${x.texto}:${x.qtt}(res ${x.reservado})`) });
  }
  out.inventarios = ((await http(tk.s, 'GET', '/inventarios')).j ?? []).map((i) => `${i.id} n${i.numero} tec${i.tecno} linhas=${i.linhas} concl=${i.concluido} lanc=${i.lancado}`);
  out.fichas = itens((await http(tk.s, 'GET', '/intervencoes/todas')).j).map((f) => `${f.id} n${f.numero} ${f.estado} ${f.tipo} ${f.equipamento} tec=${f.tecnico ?? '-'} fechada=${f.fechada} antiga=${f.antiga}`);
  out.qualidade = ((await http(tk.q, 'GET', '/qualidade')).j ?? []).map((f) => `${f.id} ${f.estado} ${f.equipamento}`);
  out.validacao = ((await http(tk.s, 'GET', '/validacao')).j ?? []).map((f) => `${f.id} ${f.estado} ${f.equipamento}`);
  console.log(JSON.stringify(out, null, 1));
}
if (process.argv[2] === 'snap') await snap();
