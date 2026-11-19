import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import {
  addActivityThroughBrowser,
  createItineraryAndDay,
  createTripThroughBrowser,
  openTrip,
  registerThroughBrowser,
} from "./helpers.ts";

async function expectNoHorizontalOverflow(page: Page, checkpoint: string) {
  const dimensions = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    document: document.documentElement.scrollWidth,
  }));
  expect(
    dimensions.document,
    `${checkpoint}: horizontal overflow (${dimensions.document}px document vs ${dimensions.viewport}px viewport)`,
  ).toBeLessThanOrEqual(dimensions.viewport + 1);
}

async function expectNoSeriousAccessibilityViolations(page: Page, checkpoint: string) {
  const results = await new AxeBuilder({ page: page as never })
    .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
    .analyze();
  const serious = results.violations.filter(
    violation => violation.impact === "critical" || violation.impact === "serious",
  );
  expect(
    serious.map(violation => ({
      id: violation.id,
      impact: violation.impact,
      nodes: violation.nodes.map(node => node.target),
    })),
    `${checkpoint}: critical or serious WCAG 2.1 A/AA issues detected`,
  ).toEqual([]);
}

test.describe("TravelOS responsive and accessibility journeys", () => {
  test("keeps registration, dashboard and itinerary usable without horizontal scrolling", async ({ page }, testInfo) => {
    await page.goto("/");
    await page.getByRole("button", { name: "Create account", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Create your account" })).toBeVisible();
    await expectNoHorizontalOverflow(page, "Registration");
    await expectNoSeriousAccessibilityViolations(page, "Registration");

    await registerThroughBrowser(page, `responsive-${testInfo.project.name}`);
    await expect(page.getByRole("heading", { name: "Your journeys" })).toBeVisible();
    await expectNoHorizontalOverflow(page, "Dashboard");
    await expectNoSeriousAccessibilityViolations(page, "Dashboard");

    const tripName = `E2E responsive ${testInfo.project.name} ${Date.now()}`;
    const { startDate, tripCard } = await createTripThroughBrowser(page, tripName);
    await expect(tripCard).toBeVisible();
    await expectNoHorizontalOverflow(page, "Trip card");

    await openTrip(page, tripName);
    await createItineraryAndDay(page, startDate);
    const activity = await addActivityThroughBrowser(
      page,
      "Mobile layout activity",
      "11:00",
      "12:00",
      "60",
    );
    await expect(activity).toBeVisible();
    await expectNoHorizontalOverflow(page, "Itinerary");
    await expectNoSeriousAccessibilityViolations(page, "Itinerary");

    const dayCard = page.locator(".day-card").first();
    const bounds = await dayCard.boundingBox();
    expect(bounds).not.toBeNull();
    expect(bounds!.width).toBeLessThanOrEqual(await page.evaluate(() => window.innerWidth) + 1);
  });

  test("supports keyboard activation and retains a visible focus target", async ({ page }, testInfo) => {
    await registerThroughBrowser(page, `keyboard-${testInfo.project.name}`);
    const planTrip = page.getByRole("button", { name: /Plan a trip/ });
    await planTrip.focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("heading", { name: "Let's plan somewhere." })).toBeVisible();

    const focus = await page.evaluate(() => {
      const active = document.activeElement;
      if (!(active instanceof HTMLElement)) return null;
      const rect = active.getBoundingClientRect();
      const style = window.getComputedStyle(active);
      return {
        width: rect.width,
        height: rect.height,
        outlineStyle: style.outlineStyle,
        outlineWidth: style.outlineWidth,
        boxShadow: style.boxShadow,
      };
    });
    expect(focus).not.toBeNull();
    expect(focus!.width).toBeGreaterThan(0);
    expect(focus!.height).toBeGreaterThan(0);
    expect(
      focus!.outlineStyle !== "none" && focus!.outlineWidth !== "0px" || focus!.boxShadow !== "none",
      "Keyboard focus should have a visible outline or shadow",
    ).toBeTruthy();
  });
});
