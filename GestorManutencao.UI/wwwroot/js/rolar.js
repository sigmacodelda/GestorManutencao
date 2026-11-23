// Desce a página até ao elemento (depois de picar). Sem o elemento não faz nada; sem animação para quem a dispensa.
export function para(id) {
    const el = document.getElementById(id);
    if (!el) return;
    const reduzir = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    el.scrollIntoView({ behavior: reduzir ? "auto" : "smooth", block: "center" });
}
