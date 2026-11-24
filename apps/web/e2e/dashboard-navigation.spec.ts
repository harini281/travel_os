import { expect, test } from "@playwright/test";
import { createTripThroughBrowser, openTrip, registerThroughBrowser } from "./helpers.ts";

test.describe("TravelOS trip dashboard navigation", () => {
  test("keeps each trip dashboard focused on its own tools", async ({ page }) => {
    await registerThroughBrowser(page, "dashboards");
    const tripName = `E2E Dashboards ${Date.now()}`;
    await createTripThroughBrowser(page, tripName);
    await openTrip(page, tripName);

    const content = page.locator(".content-wrap");
    const heading = page.locator(".trip-page-heading h2");
    const selectDashboard = async (name: RegExp, pageId: string, title: string) => {
      await page.getByRole("button", { name }).click();
      await expect(content).toHaveAttribute("data-trip-page", pageId);
      await expect(heading).toHaveText(title);
      await expect(page.getByRole("button", { name }).filter({ has: page.locator("[aria-current='page']") })).toHaveCount(0);
      await expect(page.locator(`.trip-nav button[aria-current="page"]`)).toContainText(name);
    };

    await expect(content).toHaveAttribute("data-trip-page", "overview");
    await expect(page.locator(".trip-overview-panel")).toBeVisible();
    await expect(page.locator(".trip-assistant-panel")).toBeVisible();
    await expect(page.locator(".travel-context-panel")).toBeHidden();
    await expect(page.locator(".flight-operations-panel")).toBeHidden();
    await expect(page.locator(".group-workspace")).toBeHidden();
    await expect(page.locator(".budget-workspace")).toBeHidden();

    await selectDashboard(/Itinerary/, "itinerary", "Itinerary planner");
    await expect(page.locator(".itinerary-empty")).toBeVisible();
    await expect(page.locator(".opening-hours-panel")).toBeVisible();
    await expect(page.locator(".trip-assistant-panel")).toBeHidden();
    await expect(page.locator(".group-workspace")).toBeHidden();

    await selectDashboard(/Flights & transport/, "flights", "Flights & transport");
    await expect(page.locator(".flight-operations-panel")).toBeVisible();
    await expect(page.locator(".journey-timeline-panel")).toBeVisible();
    await expect(page.locator(".trip-assistant-panel")).toBeHidden();
    await expect(page.locator(".budget-workspace")).toBeHidden();

    await selectDashboard(/Weather & places/, "weather", "Weather & places");
    await expect(page.locator(".weather-disruption-panel")).toBeVisible();
    await expect(page.locator(".opening-hours-panel")).toBeVisible();
    await expect(page.locator(".flight-operations-panel")).toBeHidden();
    await expect(page.locator(".group-workspace")).toBeHidden();

    await selectDashboard(/Travel together/, "group", "Travel together");
    await expect(page.locator(".group-workspace")).toBeVisible();
    await expect(page.locator(".preference-workspace")).toBeVisible();
    await expect(page.locator(".poll-workspace")).toBeVisible();
    await expect(page.locator(".expense-workspace")).toBeHidden();
    await expect(page.locator(".budget-workspace")).toBeHidden();

    await selectDashboard(/Budget & expenses/, "budget", "Budget & expenses");
    await expect(page.locator(".budget-workspace")).toBeVisible();
    await expect(page.locator(".expense-workspace")).toBeVisible();
    await expect(page.locator(".group-workspace")).toBeHidden();
    await expect(page.locator(".flight-operations-panel")).toBeHidden();

    await selectDashboard(/Trip settings/, "settings", "Trip settings");
    await expect(page.locator(".travel-context-panel")).toBeVisible();
    await expect(page.locator(".trip-assistant-panel")).toBeHidden();
    await expect(page.locator(".budget-workspace")).toBeHidden();
    await expect(page.locator(".group-workspace")).toBeHidden();

    // Returning to Overview should restore the concise summary without changing trip data.
    await selectDashboard(/Overview/, "overview", "Trip overview");
    await expect(page.locator(".trip-overview-panel")).toBeVisible();
    await expect(page.locator(".travel-context-panel")).toBeHidden();
  });
});
