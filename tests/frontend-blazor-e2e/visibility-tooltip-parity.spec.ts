import { expect, test } from "@playwright/test";

// Page routing cannot intercept requests fulfilled by a service worker. This
// suite deliberately delays one ES module to exercise the SSR-to-interactive
// hand-off, so keep that request on the browser network path.
test.use({ serviceWorkers: "block" });

test("specified note uses the pinned visibility and users tooltip contract without leaking to visitors", async ({
  page,
}) => {
  const failures: string[] = [];
  page.on("console", (message) => {
    if (message.type() === "error") failures.push(`console:${message.text()}`);
  });
  page.on("pageerror", (error) => failures.push(`page:${error.name}`));

  expect(
    (await page.request.post("/__test/visibility-note/specified")).status(),
  ).toBe(204);
  const visitor = await page.context().newPage();
  visitor.on("console", (message) => {
    if (message.type() === "error")
      failures.push(`visitor-console:${message.text()}`);
  });
  visitor.on("pageerror", (error) =>
    failures.push(`visitor-page:${error.name}`),
  );
  await visitor.goto("/");
  await expect(visitor.locator(".tkcbzcuz")).toHaveCount(0);
  await expect(visitor.locator("body")).not.toContainText("9recipient0");
  await visitor.close();

  await page.goto("/__test/sign-in");
  await expect(page).toHaveURL(/\/$/);
  const trigger = page.locator(".kkwtjztg ._visibility_1rbrq_1");
  await expect(trigger.locator(":scope > i.fas.fa-envelope")).toHaveCount(1);
  await expect(trigger).toHaveAttribute("role", "button");
  await expect(trigger).toHaveAttribute("tabindex", "0");
  await expect(trigger).toHaveAttribute("aria-expanded", "false");
  await expect(trigger).toHaveCSS("margin-left", /.+/);
  await expect(trigger).toHaveAttribute(
    "data-visibility-tooltip-ready",
    "true",
  );

  await trigger.hover();
  const tooltip = page.locator("body .buebdbiu._acrylic._shadow");
  await expect(tooltip).toBeVisible();
  await expect(
    tooltip.locator(':scope > .beaffaef[data-tooltip-load-state="loaded"]'),
  ).toHaveCount(1);
  await expect(tooltip.locator(".beaffaef > .user")).toHaveCount(10);
  await expect(
    tooltip.locator(".beaffaef > .user > .avatar.eiwwqkts > img.inner"),
  ).toHaveCount(10);
  await expect(
    tooltip.locator(".beaffaef > .user > .name.havbbuyv.nowrap"),
  ).toHaveCount(10);
  await expect(tooltip.locator(".beaffaef > .omitted")).toHaveText("+2");
  await expect(trigger).toHaveAttribute("aria-expanded", "true");
  await expect(trigger).toHaveAttribute(
    "aria-describedby",
    (await tooltip.getAttribute("id")) ?? "missing",
  );
  const tooltipId = await tooltip.getAttribute("id");
  const placement = await tooltip.evaluate((element) => ({
    position: getComputedStyle(element).position,
    maxWidth: getComputedStyle(element).maxWidth,
    pointerEvents: getComputedStyle(element).pointerEvents,
    zIndex: Number.parseInt(getComputedStyle(element).zIndex, 10),
    transformOrigin: getComputedStyle(element).transformOrigin,
  }));
  expect(placement.position).toBe("absolute");
  expect(placement.maxWidth).toBe("250px");
  expect(placement.pointerEvents).toBe("none");
  expect(placement.zIndex).toBeGreaterThan(3_000_000);
  expect(placement.transformOrigin.length).toBeGreaterThan(0);

  const state = (await (
    await page.request.get("/__test/visibility-state")
  ).json()) as {
    readCalls: number;
    lastRequestedIds: string[];
  };
  expect(state.readCalls).toBe(1);
  expect(state.lastRequestedIds).toHaveLength(12);

  await page.mouse.move(0, 0);
  await expect(tooltip).toHaveAttribute("data-tooltip-state", "leaving");
  await trigger.focus();
  await page.keyboard.press("Enter");
  await expect(tooltip).toBeVisible();
  await expect(tooltip).toHaveAttribute("id", tooltipId ?? "missing");
  await page.keyboard.press("Escape");
  await expect(tooltip).toHaveCount(0);
  await expect(trigger).toBeFocused();
  await expect(trigger).toHaveAttribute("aria-expanded", "false");

  await trigger.evaluate((element) =>
    element.dispatchEvent(
      new Event("touchstart", { bubbles: true, cancelable: true }),
    ),
  );
  await expect(tooltip).toBeVisible();
  await trigger.evaluate((element) =>
    element.dispatchEvent(
      new Event("touchend", { bubbles: true, cancelable: true }),
    ),
  );
  await expect(tooltip).toHaveCount(0);
  expect(failures).toEqual([]);
});

test("local-only note preserves the independent biohazard span beside home visibility", async ({
  page,
}) => {
  expect(
    (
      await page.request.post("/__test/visibility-note/local-only")
    ).status(),
  ).toBe(204);
  await page.goto("/__test/sign-in");
  await expect(page).toHaveURL(/\/$/);

  const header = page.locator(".kkwtjztg");
  await expect(
    header.locator(".info > ._visibility_1rbrq_1 > i.fas.fa-home"),
  ).toHaveCount(1);
  await expect(
    header.locator(".info > ._localOnly_1rbrq_1 > i.fas.fa-biohazard"),
  ).toHaveCount(1);
  await expect(
    header.locator(".info > ._visibility_1rbrq_1 + ._localOnly_1rbrq_1"),
  ).toHaveCount(1);
  await expect(
    header.locator(".info > ._visibility_1rbrq_1"),
  ).not.toHaveAttribute("tabindex", /.+/);
  await expect(page.locator(".buebdbiu")).toHaveCount(0);
});

test("a pointer already over the specified icon is recovered when interactive hydration finishes", async ({
  page,
}) => {
  const failures: string[] = [];
  page.on("console", (message) => {
    if (message.type() === "error") failures.push(`console:${message.text()}`);
  });
  page.on("pageerror", (error) => failures.push(`page:${error.name}`));

  expect(
    (await page.request.post("/__test/visibility-note/specified")).status(),
  ).toBe(204);

  let releaseModule!: () => void;
  const moduleGate = new Promise<void>((resolve) => {
    releaseModule = resolve;
  });
  let moduleIntercepted = false;
  await page.route("**/*", async (route) => {
    if (
      /\/js\/visibility-tooltip(?:\.[a-z0-9]+)?\.js$/u.test(
        new URL(route.request().url()).pathname,
      )
    ) {
      moduleIntercepted = true;
      await moduleGate;
    }

    await route.continue();
  });

  await page.goto("/__test/sign-in");
  const trigger = page.locator(".kkwtjztg ._visibility_1rbrq_1");
  await expect(trigger.locator(":scope > i.fas.fa-envelope")).toHaveCount(1);
  await trigger.hover();
  await expect.poll(() => moduleIntercepted).toBe(true);
  await expect(trigger).not.toHaveAttribute(
    "data-visibility-tooltip-ready",
    "true",
  );

  releaseModule();
  await expect(trigger).toHaveAttribute(
    "data-visibility-tooltip-ready",
    "true",
  );
  await expect(page.locator("body .buebdbiu._acrylic._shadow")).toBeVisible();
  expect(failures).toEqual([]);
});
