export function scrollToActive(containerSelector, activeSelector) {
    const container = document.querySelector(containerSelector);
    const active = container && container.querySelector(activeSelector);
    if (active) {
        active.scrollIntoView({ block: 'center', behavior: 'instant' });
    }
}
