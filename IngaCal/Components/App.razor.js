const preferenceKey = "ingacal.theme";
const media = matchMedia("(prefers-color-scheme: dark)");
function storedTheme() { try { return localStorage.getItem(preferenceKey) || "system"; } catch { return "system"; } }
function applyTheme() {
    const preference = storedTheme();
    const theme = preference === "system" ? (media.matches ? "dark" : "light") : preference;
    document.documentElement.dataset.bsTheme = theme;
    document.querySelectorAll("[data-theme-picker]").forEach(el => { el.value = preference; });
    window.dispatchEvent(new CustomEvent("ingacal:theme"));
}
applyTheme();
media.addEventListener("change", applyTheme);
document.addEventListener("change", event => {
    if (event.target.matches("[data-theme-picker]")) {
        try { localStorage.setItem(preferenceKey, event.target.value); } catch { }
        applyTheme();
    }
});
document.addEventListener("DOMContentLoaded", applyTheme);
document.addEventListener("enhancedload", applyTheme);
export { applyTheme };
