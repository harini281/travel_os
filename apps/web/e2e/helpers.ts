import { expect, type Page } from "@playwright/test";

export const API_URL = process.env.E2E_API_URL ?? "http://127.0.0.1:8080";
export const TEST_PASSWORD = "TravelOS!E2E2027";

export function uniqueEmail(label: string): string {
  return `travelos.e2e.${label}.${Date.now()}.${Math.random().toString(16).slice(2)}@example.test`;
}

export async function registerThroughBrowser(page: Page, label: string) {
  const email = uniqueEmail(label);
  await page.goto("/");
  await page.getByRole("button", { name: "Create account", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Create your account" })).toBeVisible();
  await page.getByLabel("Full name").fill(`E2E ${label}`);
  await page.getByLabel("Email address").fill(email);
  await page.getByLabel("Password").fill(TEST_PASSWORD);
  await page.getByRole("button", { name: "Create account →", exact: true }).click();
  await expect(page.getByRole("heading", { name: /Good to see you, E2E/ })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Your journeys" })).toBeVisible();
  return email;
}

export async function signInThroughBrowser(page: Page, email: string, password = TEST_PASSWORD) {
  await page.goto("/");
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.getByLabel("Email address").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in →", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Your journeys" })).toBeVisible();
}

export async function createTripThroughBrowser(page: Page, name: string) {
  const startDate = "2027-07-10";
  const endDate = "2027-07-12";
  await page.getByRole("button", { name: /Plan a trip/ }).click();
  await page.getByLabel("Trip name").fill(name);
  await page.getByLabel("Destination").fill("Colombo, Sri Lanka");
  await page.getByLabel("Start date").fill(startDate);
  await page.getByLabel("End date").fill(endDate);
  await page.getByLabel("Currency").selectOption("LKR");
  await page.getByRole("button", { name: /Create trip/ }).click();
  const tripCard = page.locator(".trip-card").filter({ hasText: name });
  await expect(tripCard).toBeVisible();
  await expect(tripCard.getByText("Colombo, Sri Lanka")).toBeVisible();
  await expect(tripCard.getByText("LKR", { exact: false })).toBeVisible();
  return { startDate, endDate, tripCard };
}

export async function openTrip(page: Page, tripName: string) {
  const tripCard = page.locator(".trip-card").filter({ hasText: tripName });
  await expect(tripCard).toBeVisible();
  await tripCard.getByRole("button", { name: /Open itinerary/ }).click();
  await expect(page.getByRole("heading", { name: tripName, exact: true })).toBeVisible();
  await expect(page.locator(".content-wrap")).toHaveAttribute("data-trip-page", "overview");
}

export async function apiContextFor(page: Page) {
  const token = await page.evaluate(() => sessionStorage.getItem("travelos.accessToken"));
  expect(token, "The browser session should contain an access token after sign-in").toBeTruthy();
  return { token: token!, headers: { Authorization: `Bearer ${token}` } };
}


export async function navigateTripDashboard(page: Page, dashboard: "Overview" | "Itinerary" | "Flights & transport" | "Weather & places" | "Travel together" | "Budget & expenses" | "Trip settings") {
  const navigation = page.getByRole("navigation", { name: "Trip dashboards" });
  await navigation.locator("button").filter({ hasText: dashboard }).click();
  const pageIds: Record<typeof dashboard, string> = {
    "Overview": "overview", "Itinerary": "itinerary", "Flights & transport": "flights",
    "Weather & places": "weather", "Travel together": "group", "Budget & expenses": "budget", "Trip settings": "settings"
  };
  await expect(page.locator(".content-wrap")).toHaveAttribute("data-trip-page", pageIds[dashboard]);
}

export async function createItineraryAndDay(page: Page, dayDate: string) {
  await navigateTripDashboard(page, "Itinerary");
  const toolbar = page.locator(".itinerary-toolbar");
  const emptyState = page.locator(".itinerary-empty");

  // Wait for the selected trip's itinerary request to resolve in slower engines
  // before deciding whether the workspace must be created.
  await expect(page.locator(".itinerary-toolbar, .itinerary-empty")).toBeVisible();

  const createButton = emptyState.getByRole("button", { name: /Create itinerary/ });
  if (await createButton.isVisible().catch(() => false)) {
    const createResponsePromise = page.waitForResponse(response => {
      const url = new URL(response.url());
      return /^\/api\/v1\/trips\/[^/]+\/itinerary$/.test(url.pathname)
        && response.request().method() === "POST";
    }, { timeout: 30_000 });
    await createButton.click();
    const response = await createResponsePromise;
    if (!response.ok()) {
      throw new Error(`Create itinerary failed (${response.status()}): ${await response.text()}`);
    }
  }

  // Allow slower browser engines to finish loading the new itinerary after the POST.
  await expect(toolbar).toBeVisible({ timeout: 30_000 });
  const addDay = page.getByRole("button", { name: /Add a day/ });
  if (await addDay.isVisible().catch(() => false)) {
    await addDay.click();
    await page.getByLabel("Choose a date within your trip").fill(dayDate);
    await page.getByRole("button", { name: "Add day", exact: true }).click();
  }
  await expect(page.locator(".day-card").first()).toBeVisible();
}

export async function addActivityThroughBrowser(
  page: Page,
  title: string,
  startTime: string,
  endTime: string,
  duration: string,
) {
  const dayCard = page.locator(".day-card").first();
  await dayCard.getByRole("button", { name: /Add activity/ }).click();
  const form = dayCard.locator("form.activity-form");
  await form.getByLabel("Activity name").fill(title);
  await form.getByLabel("Place").fill("Colombo");
  await form.getByLabel("Notes").fill("Created by the TravelOS Playwright browser test.");
  await form.getByLabel("Start time").fill(startTime);
  await form.getByLabel("End time").fill(endTime);
  await form.getByLabel("Duration (minutes)").fill(duration);
  await form.getByRole("button", { name: "Save activity" }).click();
  const row = dayCard.locator(".activity-row").filter({ hasText: title });
  await expect(row).toBeVisible();
  return row;
}
