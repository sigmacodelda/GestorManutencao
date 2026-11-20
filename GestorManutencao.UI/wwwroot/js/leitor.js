// Mantém o campo escondido do leitor (PDA ou teclado) sempre focado; Enter entrega o código lido.
const TIPOS_TEXTO = ["text", "search", "number", "email", "tel", "url", "password", "date", "time", "datetime-local"];

function eCampoDeTexto(el, meu) {
    if (!el || el === meu) return false;
    if (el.isContentEditable || el.tagName === "TEXTAREA" || el.tagName === "SELECT") return true;
    return el.tagName === "INPUT" && TIPOS_TEXTO.includes((el.type || "text").toLowerCase());
}

export function iniciar(campo, ref) {
    // Outro campo de texto com o foco por ação da pessoa fica em paz; senão o foco volta ao campo do leitor.
    function refocar() {
        setTimeout(function () {
            if (!campo.isConnected) return;
            if (eCampoDeTexto(document.activeElement, campo)) return;
            if (document.activeElement !== campo) campo.focus({ preventScroll: true });
        }, 0);
    }
    function aoTecla(e) {
        if (e.key !== "Enter") return;
        e.preventDefault();
        var valor = campo.value;
        campo.value = "";
        if (valor.trim().length === 0) return;
        ref.invokeMethodAsync("AoLer", valor).catch(function () { /* componente já libertado */ });
    }
    function aoVisivel() { if (!document.hidden) refocar(); }

    campo.addEventListener("keydown", aoTecla);
    campo.addEventListener("blur", refocar);
    document.addEventListener("focusout", refocar);
    document.addEventListener("visibilitychange", aoVisivel);
    window.addEventListener("focus", refocar);
    campo.focus({ preventScroll: true });

    return {
        refocar: refocar,
        parar: function () {
            campo.removeEventListener("keydown", aoTecla);
            campo.removeEventListener("blur", refocar);
            document.removeEventListener("focusout", refocar);
            document.removeEventListener("visibilitychange", aoVisivel);
            window.removeEventListener("focus", refocar);
        }
    };
}
