const matterUrl = new URL(
    '_content/ActivityPub.Misskey.Blazor/vendor/matter-0.18.0.min.js',
    document.baseURI).href;
let matterPromise;

export async function prepare(container) {
    if (!(container instanceof HTMLElement) || !container.isConnected) return false;
    const icon = container.querySelector(':scope > img.icon');
    if (icon instanceof HTMLImageElement && !icon.complete) {
        await new Promise(resolve => {
            const complete = () => resolve();
            icon.addEventListener('load', complete, { once: true });
            icon.addEventListener('error', complete, { once: true });
        });
    }
    if (!container.isConnected) return false;

    const width = container.offsetWidth;
    if (width <= 0) return false;
    const emojis = container.querySelectorAll(':scope > span.emoji');
    for (const emoji of emojis) {
        emoji.dataset.physicsX = `${Math.random() * width}`;
        emoji.dataset.physicsY = `${-(128 + Math.random() * 256)}`;
    }
    container.dataset.physicsPrepared = 'true';
    return emojis.length === 32;
}

export async function start(container) {
    if (!(container instanceof HTMLElement) || container.dataset.physicsPrepared !== 'true') {
        throw new Error('The Misskey physics container is not prepared.');
    }

    const Matter = await loadMatter();
    const containerWidth = container.offsetWidth;
    const containerHeight = container.offsetHeight;
    const containerCenterX = containerWidth / 2;

    container.style.position = 'relative';
    container.style.boxSizing = 'border-box';
    container.style.width = `${containerWidth}px`;
    container.style.height = `${containerHeight}px`;

    const engine = Matter.Engine.create({
        constraintIterations: 4,
        positionIterations: 8,
        velocityIterations: 8,
    });
    const world = engine.world;
    const runner = Matter.Runner.create();
    Matter.Runner.run(runner, engine);

    const groundThickness = 1024;
    const ground = Matter.Bodies.rectangle(
        containerCenterX,
        containerHeight + groundThickness / 2,
        containerWidth,
        groundThickness,
        { isStatic: true, restitution: 0.1, friction: 2 });
    Matter.World.add(world, [ground]);

    const elements = Array.from(container.children).filter(value => value instanceof HTMLElement);
    const bodies = [];
    for (const element of elements) {
        const left = element.dataset.physicsX === undefined
            ? element.offsetLeft
            : Number.parseInt(element.dataset.physicsX, 10);
        const top = element.dataset.physicsY === undefined
            ? element.offsetTop
            : Number.parseInt(element.dataset.physicsY, 10);
        const width = element.offsetWidth;
        const height = element.offsetHeight;
        const body = element.classList.contains('_physics_circle_')
            ? Matter.Bodies.circle(left + width / 2, top + height / 2, Math.max(width, height) / 2, {
                restitution: 0.5,
            })
            : Matter.Bodies.rectangle(left + width / 2, top + height / 2, width, height, {
                chamfer: { radius: Number.parseInt(getComputedStyle(element).borderRadius || '0', 10) || 0 },
                restitution: 0.5,
            });
        element.id = `${body.id}`;
        bodies.push(body);
    }
    Matter.World.add(world, bodies);

    const mouse = Matter.Mouse.create(container);
    const mouseConstraint = Matter.MouseConstraint.create(engine, {
        mouse,
        constraint: { stiffness: 0.1, render: { visible: false } },
    });
    Matter.World.add(world, mouseConstraint);

    for (const element of elements) {
        element.style.position = 'absolute';
        element.style.top = '0';
        element.style.left = '0';
        element.style.margin = '0';
    }

    let stopped = false;
    let frame = 0;
    const update = () => {
        frame = 0;
        if (stopped) return;
        for (let index = 0; index < elements.length; index += 1) {
            const element = elements[index];
            const body = bodies[index];
            if (body === undefined) continue;
            const x = body.position.x - element.offsetWidth / 2;
            const y = body.position.y - element.offsetHeight / 2;
            element.style.transform = `translate(${x}px, ${y}px) rotate(${body.angle}rad)`;
        }
        frame = window.requestAnimationFrame(update);
    };
    frame = window.requestAnimationFrame(update);

    const interval = window.setInterval(() => {
        for (const body of bodies) {
            if (body.position.y > containerHeight + 1024) Matter.World.remove(world, body);
        }
    }, 10_000);
    container.dataset.physicsActive = 'true';

    const stop = () => {
        if (stopped) return;
        stopped = true;
        delete container.dataset.physicsActive;
        if (frame !== 0) window.cancelAnimationFrame(frame);
        window.clearInterval(interval);
        Matter.Runner.stop(runner);
        Matter.Mouse.clearSourceEvents(mouse);
        Matter.World.clear(world, false);
        Matter.Engine.clear(engine);
    };

    return { stop, dispose: stop };
}

function loadMatter() {
    if (globalThis.Matter !== undefined) return Promise.resolve(globalThis.Matter);
    if (matterPromise !== undefined) return matterPromise;
    matterPromise = new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = matterUrl;
        script.async = true;
        script.addEventListener('load', () => {
            if (globalThis.Matter === undefined) {
                reject(new Error('Matter.js loaded without exporting its browser API.'));
                return;
            }
            resolve(globalThis.Matter);
        }, { once: true });
        script.addEventListener('error', () => {
            script.remove();
            matterPromise = undefined;
            reject(new Error('Matter.js could not be loaded.'));
        }, { once: true });
        document.head.append(script);
    });
    return matterPromise;
}
