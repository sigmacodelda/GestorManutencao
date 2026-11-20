// Mantém o campo escondido do leitor (PDA ou teclado) sempre focado; Enter (ou Tab, se for esse o sufixo do leitor) entrega o código lido.
const TIPOS_TEXTO = ["text", "search", "number", "email", "tel", "url", "password", "date", "time", "datetime-local"];

function eCampoDeTexto(el, meu) {
    if (!el || el === meu) return false;
    if (el.isContentEditable || el.tagName === "TEXTAREA" || el.tagName === "SELECT") return true;
    return el.tagName === "INPUT" && TIPOS_TEXTO.includes((el.type || "text").toLowerCase());
}

export function iniciar(campo, ref) {
    // Se o foco saiu por Tab é a pessoa a navegar com o teclado: não se lhe tira o foco.
    var saiuPorTab = false;

    // Outro campo de texto com o foco por ação da pessoa fica em paz; senão o foco volta ao campo do leitor.
    function refocar() {
        setTimeout(function () {
            var porTab = saiuPorTab;
            saiuPorTab = false;
            if (porTab || !campo.isConnected) return;
            if (eCampoDeTexto(document.activeElement, campo)) return;
            if (document.activeElement !== campo) campo.focus({ preventScroll: true });
        }, 0);
    }
    function aoTeclaGlobal(e) { saiuPorTab = e.key === "Tab"; }
    function aoPonteiro() { saiuPorTab = false; }
    function aoTecla(e) {
        if (e.key !== "Enter" && e.key !== "Tab") return;
        var valor = campo.value;
        var temTexto = valor.trim().length > 0;
        // Tab sem nada lido é navegação normal.
        if (e.key === "Tab" && !temTexto) return;
        e.preventDefault();
        campo.value = "";
        if (e.key === "Tab") saiuPorTab = false;
        if (!temTexto) return;
        ref.invokeMethodAsync("AoLer", valor).catch(function () { /* componente já libertado */ });
    }
    function aoVisivel() { if (!document.hidden) refocar(); }

    campo.addEventListener("keydown", aoTecla);
    // O blur do campo também chega aqui (focusout sobe ao documento).
    document.addEventListener("keydown", aoTeclaGlobal, true);
    document.addEventListener("pointerdown", aoPonteiro, true);
    document.addEventListener("focusout", refocar);
    document.addEventListener("visibilitychange", aoVisivel);
    window.addEventListener("focus", refocar);
    campo.focus({ preventScroll: true });

    return {
        refocar: refocar,
        parar: function () {
            campo.removeEventListener("keydown", aoTecla);
            document.removeEventListener("keydown", aoTeclaGlobal, true);
            document.removeEventListener("pointerdown", aoPonteiro, true);
            document.removeEventListener("focusout", refocar);
            document.removeEventListener("visibilitychange", aoVisivel);
            window.removeEventListener("focus", refocar);
        }
    };
}
