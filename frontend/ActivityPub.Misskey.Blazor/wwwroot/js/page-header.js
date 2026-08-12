const tooltipSelector = '[data-tooltip]';

export function attach(element, receiver) {
    let disposed = false;
    let frame = 0;
    let tooltip = null;
    let tooltipFrame = 0;
    let tooltipCloseTimer = 0;
    let configuredRefreshPending = false;
    let configuredBackground = element.dataset.headerBackground?.trim() || null;
    const observed = element.parentElement ?? element;

    const refreshHighlight = () => {
        if (disposed || !element.isConnected) return;
        const tab = element.querySelector(':scope > .tabs > .tab.active');
        const highlight = element.querySelector(':scope > .tabs > .highlight');
        if (!(tab instanceof HTMLElement) || !(highlight instanceof HTMLElement) || tab.parentElement === null) {
            return;
        }
        const parentRect = tab.parentElement.getBoundingClientRect();
        const rect = tab.getBoundingClientRect();
        highlight.style.width = `${rect.width}px`;
        highlight.style.left = `${rect.left - parentRect.left}px`;
    };

    const applyBackground = (preferConfigured = false) => {
        if (disposed || !element.isConnected) return;
        const requested = preferConfigured
            ? configuredBackground || 'var(--bg)'
            : element.dataset.headerBackground?.trim() || 'var(--bg)';
        const color = resolveColor(requested) ?? resolveColor('var(--bg)');
        if (color === null) return;
        element.style.backgroundColor = `rgba(${color[0]}, ${color[1]}, ${color[2]}, 0.85)`;
    };

    const publish = () => {
        frame = 0;
        if (disposed || !element.isConnected) return;
        receiver.invokeMethodAsync('UpdatePageHeaderNarrow', observed.offsetWidth < 500)
            .catch(error => {
                if (!disposed && element.isConnected) {
                    console.error('Page header resize callback failed.', error);
                }
            });
        const preferConfigured = configuredRefreshPending;
        configuredRefreshPending = false;
        applyBackground(preferConfigured);
        refreshHighlight();
    };

    const schedule = () => {
        if (frame === 0) frame = window.requestAnimationFrame(publish);
    };

    const positionTooltip = () => {
        tooltipFrame = 0;
        if (tooltip === null || !tooltip.target.isConnected || !tooltip.element.isConnected) return;
        const targetRect = tooltip.target.getBoundingClientRect();
        const tooltipRect = tooltip.element.getBoundingClientRect();
        const viewportMargin = 8;
        const centered = window.scrollX + targetRect.left + ((targetRect.width - tooltipRect.width) / 2);
        const left = Math.min(
            window.scrollX + window.innerWidth - tooltipRect.width - viewportMargin,
            Math.max(window.scrollX + viewportMargin, centered));
        const preferredTop = window.scrollY + targetRect.top - tooltipRect.height - viewportMargin;
        const top = preferredTop >= window.scrollY + viewportMargin
            ? preferredTop
            : window.scrollY + targetRect.bottom + viewportMargin;
        tooltip.element.style.left = `${left}px`;
        tooltip.element.style.top = `${top}px`;
        tooltip.element.style.transformOrigin = preferredTop >= window.scrollY + viewportMargin
            ? 'center bottom'
            : 'center top';
        tooltipFrame = window.requestAnimationFrame(positionTooltip);
    };

    const closeTooltip = () => {
        if (tooltip === null) return;
        const closing = tooltip.element;
        tooltip = null;
        if (tooltipFrame !== 0) {
            window.cancelAnimationFrame(tooltipFrame);
            tooltipFrame = 0;
        }
        closing.classList.add('tooltip-leave-active');
        tooltipCloseTimer = window.setTimeout(() => {
            tooltipCloseTimer = 0;
            closing.remove();
        }, 200);
    };

    const showTooltip = target => {
        const text = target.dataset.tooltip;
        if (disposed || !target.isConnected || typeof text !== 'string' || text.length === 0) return;
        if (tooltip?.target === target) return;
        closeTooltip();
        const tooltipElement = document.createElement('div');
        tooltipElement.className = 'buebdbiu _acrylic _shadow tooltip-enter-active tooltip-enter-from';
        tooltipElement.dataset.pageHeaderTooltip = '';
        tooltipElement.setAttribute('role', 'tooltip');
        tooltipElement.style.zIndex = '1100000';
        tooltipElement.style.maxWidth = '250px';
        const content = document.createElement('span');
        content.textContent = text;
        tooltipElement.append(content);
        document.body.append(tooltipElement);
        tooltip = { target, element: tooltipElement };
        positionTooltip();
        window.requestAnimationFrame(() => {
            if (tooltip?.element === tooltipElement) tooltipElement.classList.remove('tooltip-enter-from');
        });
    };

    const tooltipTarget = event => {
        const target = event.target instanceof Element ? event.target.closest(tooltipSelector) : null;
        return target instanceof HTMLElement && element.contains(target) ? target : null;
    };
    const onPointerOver = event => {
        const target = tooltipTarget(event);
        if (target !== null && !(event.relatedTarget instanceof Node && target.contains(event.relatedTarget))) {
            showTooltip(target);
        }
    };
    const onPointerOut = event => {
        const target = tooltipTarget(event);
        if (target !== null && !(event.relatedTarget instanceof Node && target.contains(event.relatedTarget))) {
            closeTooltip();
        }
    };
    const onFocusIn = event => {
        const target = tooltipTarget(event);
        if (target !== null) showTooltip(target);
    };
    const onFocusOut = event => {
        const target = tooltipTarget(event);
        if (target !== null) closeTooltip();
    };
    const onTouchStart = event => {
        const target = tooltipTarget(event);
        if (target !== null) {
            if (target.matches('.buttons.right > button')) {
                event.stopPropagation();
            }
            showTooltip(target);
        }
    };
    const onTouchEnd = event => {
        if (tooltipTarget(event) !== null) closeTooltip();
    };
    const onClick = event => {
        if (tooltipTarget(event) !== null) closeTooltip();
    };
    const onSelectStart = event => {
        if (tooltipTarget(event) !== null) event.preventDefault();
    };
    const onKeyDown = event => {
        const trigger = event.target instanceof Element
            ? event.target.closest('[data-tabs-popup-trigger="true"]')
            : null;
        if (trigger !== null && ['Enter', ' ', 'Spacebar', 'ArrowDown'].includes(event.key)) {
            event.preventDefault();
        }
    };

    const resizeObserver = new ResizeObserver(schedule);
    resizeObserver.observe(observed);
    const mutationObserver = new MutationObserver(schedule);
    mutationObserver.observe(element, {
        attributes: true,
        childList: true,
        subtree: true,
        attributeFilter: ['class', 'data-header-background'],
    });
    const themeObserver = new MutationObserver(applyBackground);
    themeObserver.observe(document.documentElement, {
        attributes: true,
        attributeFilter: ['class', 'style', 'data-theme', 'data-theme-id'],
    });
    window.addEventListener('resize', schedule, { passive: true });
    element.addEventListener('pointerover', onPointerOver, { passive: true });
    element.addEventListener('pointerout', onPointerOut, { passive: true });
    element.addEventListener('focusin', onFocusIn, { passive: true });
    element.addEventListener('focusout', onFocusOut, { passive: true });
    element.addEventListener('touchstart', onTouchStart, { passive: true });
    element.addEventListener('touchend', onTouchEnd, { passive: true });
    element.addEventListener('click', onClick, { passive: true });
    element.addEventListener('selectstart', onSelectStart);
    element.addEventListener('keydown', onKeyDown);
    publish();

    return {
        refresh(background) {
            configuredBackground = typeof background === 'string' && background.trim().length > 0
                ? background.trim()
                : null;
            configuredRefreshPending = true;
            schedule();
        },
        refreshHighlight,
        scrollToTop() {
            let container = element.parentElement;
            while (container !== null && container.tagName !== 'HTML') {
                const overflow = getComputedStyle(container).overflowY;
                if (overflow === 'auto' || overflow === 'scroll') {
                    container.scroll({ top: 0, behavior: 'smooth' });
                    return;
                }
                container = container.parentElement;
            }
            window.scroll({ top: 0, behavior: 'smooth' });
        },
        dispose() {
            if (disposed) return;
            disposed = true;
            resizeObserver.disconnect();
            mutationObserver.disconnect();
            themeObserver.disconnect();
            window.removeEventListener('resize', schedule);
            element.removeEventListener('pointerover', onPointerOver);
            element.removeEventListener('pointerout', onPointerOut);
            element.removeEventListener('focusin', onFocusIn);
            element.removeEventListener('focusout', onFocusOut);
            element.removeEventListener('touchstart', onTouchStart);
            element.removeEventListener('touchend', onTouchEnd);
            element.removeEventListener('click', onClick);
            element.removeEventListener('selectstart', onSelectStart);
            element.removeEventListener('keydown', onKeyDown);
            closeTooltip();
            if (tooltipCloseTimer !== 0) {
                window.clearTimeout(tooltipCloseTimer);
                tooltipCloseTimer = 0;
            }
            document.querySelectorAll('body > .buebdbiu[data-page-header-tooltip]').forEach(node => node.remove());
            if (frame !== 0) window.cancelAnimationFrame(frame);
        },
    };
}

function resolveColor(requested) {
    let value = requested;
    const variable = /^var\((--[A-Za-z0-9_-]+)\)$/.exec(requested);
    if (variable !== null) {
        value = getComputedStyle(document.documentElement).getPropertyValue(variable[1]).trim();
    }
    if (value.length === 0 || value.length > 128) return null;
    const canvas = document.createElement('canvas');
    canvas.width = 1;
    canvas.height = 1;
    const context = canvas.getContext('2d', { willReadFrequently: true });
    if (context === null) return null;
    context.fillStyle = '#010203';
    const sentinel = context.fillStyle;
    context.fillStyle = value;
    if (context.fillStyle === sentinel && !['#010203', 'rgb(1, 2, 3)'].includes(value.toLowerCase())) {
        return null;
    }
    context.clearRect(0, 0, 1, 1);
    context.fillRect(0, 0, 1, 1);
    const color = context.getImageData(0, 0, 1, 1).data;
    return [color[0], color[1], color[2]];
}
