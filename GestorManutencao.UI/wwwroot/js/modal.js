// Prende o Tab dentro do modal de escolha enquanto está aberto: do último volta ao primeiro (e ao contrário com Shift),
// e se o foco já estiver fora do modal, o Tab traz-o de volta.
export function prender(caixa) {
    const SELETOR = 'button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

    function aoTecla(e) {
        if (e.key !== "Tab" || !caixa.isConnected) return;
        const alvos = Array.from(caixa.querySelectorAll(SELETOR)).filter(function (x) { return x.offsetParent !== null; });
        if (alvos.length === 0) { e.preventDefault(); caixa.focus(); return; }
        const primeiro = alvos[0];
        const ultimo = alvos[alvos.length - 1];
        const ativo = document.activeElement;
        if (!caixa.contains(ativo)) { e.preventDefault(); primeiro.focus(); return; }
        if (e.shiftKey && (ativo === primeiro || ativo === caixa)) { e.preventDefault(); ultimo.focus(); }
        else if (!e.shiftKey && ativo === ultimo) { e.preventDefault(); primeiro.focus(); }
    }

    document.addEventListener("keydown", aoTecla, true);
    return { parar: function () { document.removeEventListener("keydown", aoTecla, true); } };
}
