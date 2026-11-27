import { expect, test } from "@playwright/test";
import {
  addActivityThroughBrowser,
  createItineraryAndDay,
  createTripThroughBrowser,
  navigateTripDashboard,
  openTrip,
  registerThroughBrowser,
} from "./helpers.ts";

async function expectNoHorizontalOverflow(page: import("@playwright/test").Page) {
  const dimensions = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    document: document.documentElement.scrollWidth,
  }));
  expect(
    dimensions.document,
    `Horizontal overflow: document ${dimensions.document}px, viewport ${dimensions.viewport}px`,
  ).toBeLessThanOrEqual(dimensions.viewport + 1);
}

test.describe("TravelOS desktop browser-engine compatibility", () => {
  test("registers, creates a trip, edits the itinerary and persists in the active browser engine", async ({ page, browserName }) => {
    const pageErrors: string[] = [];
    page.on("pageerror", error => pageErrors.push(error.message));

    await registerThroughBrowser(page, `engine-${browserName}`);
    const tripName = `E2E ${browserName} journey ${Date.now()}`;
    const { startDate } = await createTripThroughBrowser(page, tripName);
    await expectNoHorizontalOverflow(page);

    await openTrip(page, tripName);
    await createItineraryAndDay(page, startDate);
    const activity = await addActivityThroughBrowser(
      page,
      `Browser check ${browserName}`,
      "09:15",
      "10:00",
      "45",
    );
    await expect(activity.locator(".activity-time")).toContainText("09:15");
    await expectNoHorizontalOverflow(page);

    await page.reload();
    await openTrip(page, tripName);
    await navigateTripDashboard(page, "Itinerary");
    await expect(
      page.locator(".activity-row").filter({ hasText: `Browser check ${browserName}` }),
    ).toBeVisible();
    await expectNoHorizontalOverflow(page);
    expect(pageErrors, "No uncaught browser page errors should occur").toEqual([]);
  });
});
