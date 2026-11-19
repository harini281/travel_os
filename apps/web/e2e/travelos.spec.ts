import { expect, test, type Page, type APIRequestContext } from "@playwright/test";

const API_URL = process.env.E2E_API_URL ?? "http://127.0.0.1:8080";
const TEST_PASSWORD = "TravelOS!E2E2027";

function uniqueEmail(label: string): string {
  return `travelos.e2e.${label}.${Date.now()}.${Math.random().toString(16).slice(2)}@example.test`;
}

async function registerThroughBrowser(page: Page, label: string) {
  const email = uniqueEmail(label);
  await page.goto("/");
  await page.getByRole("button", { name: "Create account", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Create your account" })).toBeVisible();
  await page.getByLabel("Full name").fill(`E2E ${label}`);
  await page.getByLabel("Email address").fill(email);
  await page.getByLabel("Password").fill(TEST_PASSWORD);
  await page.getByRole("button", { name: /Create account/ }).click();
  await expect(page.getByRole("heading", { name: new RegExp(`Good to see you, E2E`) })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Your journeys" })).toBeVisible();
  return email;
}

async function signInThroughBrowser(page: Page, email: string, password = TEST_PASSWORD) {
  await page.goto("/");
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.getByLabel("Email address").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: /Sign in/ }).click();
  await expect(page.getByRole("heading", { name: "Your journeys" })).toBeVisible();
}

async function createTripThroughBrowser(page: Page, name: string) {
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

async function openTrip(page: Page, tripName: string) {
  const tripCard = page.locator(".trip-card").filter({ hasText: tripName });
  await expect(tripCard).toBeVisible();
  await tripCard.getByRole("button", { name: /Open itinerary/ }).click();
  await expect(page.getByRole("heading", { name: tripName, exact: true })).toBeVisible();
}

async function createItineraryAndDay(page: Page, dayDate: string) {
  const createButton = page.getByRole("button", { name: /Create itinerary/ });
  if (await createButton.isVisible().catch(() => false)) {
    await createButton.click();
  }
  await expect(page.locator(".itinerary-toolbar")).toBeVisible();
  const addDay = page.getByRole("button", { name: /Add a day/ });
  if (await addDay.isVisible().catch(() => false)) {
    await addDay.click();
    await page.getByLabel("Choose a date within your trip").fill(dayDate);
    await page.getByRole("button", { name: "Add day", exact: true }).click();
  }
  await expect(page.locator(".day-card").first()).toBeVisible();
}

async function addActivityThroughBrowser(
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

async function apiContextFor(page: Page) {
  const token = await page.evaluate(() => sessionStorage.getItem("travelos.accessToken"));
  expect(token, "The browser session should contain an access token after sign-in").toBeTruthy();
  return { token: token!, headers: { Authorization: `Bearer ${token}` } };
}

test.describe("TravelOS browser end-to-end journeys", () => {
  test("registers, signs in, rejects bad credentials and protects authenticated API access", async ({ page }) => {
    const email = await registerThroughBrowser(page, "auth");
    await expect(page.getByText("Your TravelOS account is ready.")).toBeVisible();

    await page.getByRole("button", { name: "Sign out" }).click();
    await page.getByRole("button", { name: "Sign in", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Welcome back" })).toBeVisible();

    await page.getByLabel("Email address").fill(email);
    await page.getByLabel("Password").fill("DefinitelyWrong!123");
    await page.getByRole("button", { name: /Sign in/ }).click();
    await expect(page.locator(".alert.error")).toBeVisible();
    await expect(page.locator(".alert.error")).not.toBeEmpty();

    const protectedResponse = await page.request.get(`${API_URL}/api/v1/trips`);
    expect(protectedResponse.status()).toBe(401);
    await expect(page.getByRole("heading", { name: "Welcome back" })).toBeVisible();
  });

  test("creates a trip, persists it, edits itinerary activities and enforces approval read-only mode", async ({ page }) => {
    await registerThroughBrowser(page, "itinerary");
    const tripName = `E2E Itinerary ${Date.now()}`;
    const { startDate, tripCard } = await createTripThroughBrowser(page, tripName);

    // Trip details are fetched from the Platform API again after a full browser refresh.
    await page.reload();
    await expect(page.locator(".trip-card").filter({ hasText: tripName })).toBeVisible();
    await openTrip(page, tripName);
    await createItineraryAndDay(page, startDate);

    const museum = await addActivityThroughBrowser(page, "Morning museum", "10:00", "11:00", "60");
    await expect(museum.locator(".activity-time")).toContainText("10:00");
    await addActivityThroughBrowser(page, "Temporary gallery", "12:00", "13:00", "60");

    const temporaryRow = page.locator(".day-card .activity-row").filter({ hasText: "Temporary gallery" });
    await temporaryRow.getByRole("button", { name: "Edit" }).click();
    const editForm = page.locator("form.activity-edit-form");
    await editForm.getByLabel("Activity name").fill("Edited gallery visit");
    await editForm.getByRole("button", { name: "Save changes" }).click();
    await expect(page.locator(".activity-row").filter({ hasText: "Edited gallery visit" })).toBeVisible();

    page.on("dialog", dialog => dialog.accept());
    const editedRow = page.locator(".day-card .activity-row").filter({ hasText: "Edited gallery visit" });
    await editedRow.getByRole("button", { name: "Remove" }).click();
    await expect(page.getByText("Edited gallery visit", { exact: true })).toHaveCount(0);

    await page.getByRole("button", { name: "Approve plan" }).click();
    await expect(page.getByText("This itinerary has been approved and is now read-only.")).toBeVisible();
    await expect(page.locator(".approval-pill")).toContainText("APPROVED");
    await expect(page.getByRole("button", { name: /Add a day/ })).toHaveCount(0);
    await expect(page.locator(".add-activity-link")).toHaveCount(0);

    await page.reload();
    await openTrip(page, tripName);
    await expect(page.locator(".approval-pill")).toContainText("APPROVED");
    await expect(page.locator(".activity-row").filter({ hasText: "Morning museum" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Approve plan" })).toHaveCount(0);
    await expect(page.locator(".add-activity-link")).toHaveCount(0);
  });

  test("reviews deterministic AI suggestions, schedules safely, saves explicitly and prevents duplicate saves", async ({ page }) => {
    await registerThroughBrowser(page, "schedule");
    const tripName = `E2E Scheduling ${Date.now()}`;
    const { startDate } = await createTripThroughBrowser(page, tripName);
    await openTrip(page, tripName);
    await createItineraryAndDay(page, startDate);
    const dayCard = page.locator(".day-card").first();
    await addActivityThroughBrowser(page, "Morning museum", "10:00", "11:00", "60");

    await page.getByRole("button", { name: /Get recommendations/ }).click();
    await expect(page.locator(".recommendation-results")).toBeVisible();

    const garden = page.locator(".recommendation-card").filter({ hasText: "Botanical Garden Walk" });
    const riverside = page.locator(".recommendation-card").filter({ hasText: "Riverside Art Walk" });
    await expect(garden).toBeVisible();
    await expect(garden.getByText("A calm outdoor visit that adds variety to the day's existing museum stop.")).toBeVisible();
    await expect(garden.getByText(/60 min/)).toBeVisible();
    await expect(garden.getByText(/500 LKR/)).toBeVisible();
    await expect(garden.getByText(/Nature lovers and relaxed travellers/)).toBeVisible();
    await expect(garden.getByText(/Playwright fixture only/)).toBeVisible();
    await expect(page.getByText(/not verified venue listings or live prices/)).toBeVisible();
    await expect(dayCard.locator(".activity-row").filter({ hasText: "Botanical Garden Walk" })).toHaveCount(0);

    await page.getByLabel("Planning day starts").fill("08:00");
    await page.getByLabel("Planning day ends").fill("20:00");
    await page.getByLabel("Buffer between activities").selectOption("15");
    await garden.getByRole("button", { name: "Find a suitable time" }).click();
    const schedulePanel = garden.locator(".recommendation-schedule");
    await expect(schedulePanel.getByText(/Suggested schedule/)).toBeVisible();
    const slotPicker = schedulePanel.getByLabel("Choose a time slot");
    const selectedSlotText = (await slotPicker.locator("option:checked").textContent()) ?? "";
    const slotMatch = selectedSlotText.match(/(\d{2}:\d{2})–(\d{2}:\d{2})/);
    expect(slotMatch, `Expected a readable selected time slot, got: ${selectedSlotText}`).not.toBeNull();
    const selectedStart = slotMatch![1];
    const selectedEnd = slotMatch![2];
    expect(selectedEnd <= "10:00" || selectedStart >= "11:00").toBeTruthy();

    await schedulePanel.getByRole("button", { name: "Save with this time" }).click();
    const savedGarden = dayCard.locator(".activity-row").filter({ hasText: "Botanical Garden Walk" });
    await expect(savedGarden).toBeVisible();
    await expect(savedGarden.locator(".activity-time")).toContainText(selectedStart);
    await expect(savedGarden.locator(".activity-time")).toContainText(selectedEnd);

    // A refresh forces the time to be reloaded from the real PostgreSQL-backed API.
    await page.reload();
    await openTrip(page, tripName);
    await expect(page.locator(".day-card .activity-row").filter({ hasText: "Botanical Garden Walk" }).locator(".activity-time"))
      .toContainText(selectedStart);
    await expect(page.locator(".day-card .activity-row").filter({ hasText: "Botanical Garden Walk" }).locator(".activity-time"))
      .toContainText(selectedEnd);

    await page.getByRole("button", { name: /Get recommendations/ }).click();
    const gardenAgain = page.locator(".recommendation-card").filter({ hasText: "Botanical Garden Walk" });
    await expect(gardenAgain.getByText(/Added to itinerary/)).toBeVisible();
    await expect(gardenAgain.getByRole("button", { name: "Save with this time" })).toHaveCount(0);
    await expect(gardenAgain.getByRole("button", { name: "Add without a time" })).toHaveCount(0);

    // The existing museum occupies this one-hour window, so no second suggestion fits.
    await page.getByLabel("Planning day starts").fill("10:00");
    await page.getByLabel("Planning day ends").fill("11:00");
    await page.getByLabel("Buffer between activities").selectOption("0");
    const scheduleButton = riverside.getByRole("button", { name: "Find a suitable time" });
    await scheduleButton.click();
    await expect(riverside.locator(".recommendation-schedule"))
      .toContainText("No suitable slot was found in this planning window.");
    await expect(riverside.getByRole("button", { name: "Save with this time" })).toHaveCount(0);
    await expect(riverside.getByRole("button", { name: "Add without a time" })).toBeVisible();

    await riverside.getByRole("button", { name: "Add without a time" }).click();
    const unscheduled = page.locator(".day-card .activity-row").filter({ hasText: "Riverside Art Walk" });
    await expect(unscheduled).toBeVisible();
    await expect(unscheduled.locator(".activity-time")).toHaveCount(0);

    await page.reload();
    await openTrip(page, tripName);
    const unscheduledAfterRefresh = page.locator(".day-card .activity-row").filter({ hasText: "Riverside Art Walk" });
    await expect(unscheduledAfterRefresh).toBeVisible();
    await expect(unscheduledAfterRefresh.locator(".activity-time")).toHaveCount(0);
  });

  test("shows AI service failures and hides owner-only itinerary and scheduling controls from trip members", async ({ page, request }) => {
    await registerThroughBrowser(page, "owner");
    const tripName = `E2E Member Permissions ${Date.now()}`;
    const { startDate } = await createTripThroughBrowser(page, tripName);
    await openTrip(page, tripName);
    await createItineraryAndDay(page, startDate);
    const dayCard = page.locator(".day-card").first();
    await addActivityThroughBrowser(page, "Member visible museum", "10:00", "11:00", "60");

    // Register the invited traveller via the real auth API, then create and accept
    // an invitation using the real owner/member authorization boundaries.
    const api = request;
    const memberEmail = uniqueEmail("member");
    const memberRegistration = await api.post(`${API_URL}/api/v1/auth/register`, {
        data: { email: memberEmail, displayName: "E2E Member", password: TEST_PASSWORD },
      });
    expect(memberRegistration.status()).toBe(201);
    const memberAuth = await memberRegistration.json() as { accessToken: string; user: { id: string } };

    const owner = await apiContextFor(page);
    const ownerTripsResponse = await api.get(`${API_URL}/api/v1/trips`, { headers: owner.headers });
    expect(ownerTripsResponse.ok()).toBeTruthy();
    const ownerTrips = await ownerTripsResponse.json() as Array<{ id: string; name: string }>;
    const trip = ownerTrips.find(item => item.name === tripName);
    expect(trip).toBeTruthy();

    const invitationResponse = await api.post(`${API_URL}/api/v1/trips/${trip!.id}/invitations`, {
        headers: owner.headers,
        data: { email: memberEmail },
      });
    expect(invitationResponse.status()).toBe(201);
    const invitation = await invitationResponse.json() as { id: string };

    const acceptResponse = await api.post(
        `${API_URL}/api/v1/trips/${trip!.id}/invitations/${invitation.id}/accept`,
        { headers: { Authorization: `Bearer ${memberAuth.accessToken}` } },
      );
    expect(acceptResponse.ok()).toBeTruthy();

    await page.getByLabel("What would you like to improve?").fill("[e2e:fail] simulate AI provider unavailable");
    await page.getByRole("button", { name: /Get recommendations/ }).click();
    await expect(page.locator(".alert.error")).toBeVisible();
    await expect(page.locator(".alert.error")).not.toBeEmpty();

    await page.getByRole("button", { name: "Sign out" }).click();
      await signInThroughBrowser(page, memberEmail);
      await openTrip(page, tripName);
      await expect(page.locator(".day-card .activity-row").filter({ hasText: "Member visible museum" })).toBeVisible();
      await expect(page.getByRole("heading", { name: "Improve a day in your itinerary" })).toHaveCount(0);
      await expect(page.getByRole("button", { name: /Add a day/ })).toHaveCount(0);
      await expect(page.locator(".add-activity-link")).toHaveCount(0);

    const memberHeaders = { Authorization: `Bearer ${memberAuth.accessToken}` };
    const scheduleResponse = await api.post(
        `${API_URL}/api/v1/trips/${trip!.id}/itinerary/recommendations/schedule`,
        {
          headers: memberHeaders,
          data: {
            dayNumber: 1, title: "Forbidden member suggestion", durationMinutes: 60,
            dayStartTime: "08:00", dayEndTime: "20:00", bufferMinutes: 15,
          },
        },
      );
    expect(scheduleResponse.status()).toBe(404);

    const itineraryResponse = await api.get(`${API_URL}/api/v1/trips/${trip!.id}/itinerary`, { headers: memberHeaders });
    expect(itineraryResponse.ok()).toBeTruthy();
    const itinerary = await itineraryResponse.json() as { days: Array<{ id: string }> };
    const addActivityResponse = await api.post(
        `${API_URL}/api/v1/trips/${trip!.id}/itinerary/days/${itinerary.days[0].id}/activities`,
        {
          headers: memberHeaders,
          data: {
            title: "Member forbidden edit", description: null, placeName: null,
            latitude: null, longitude: null, durationMinutes: 60,
            estimatedCost: null, currencyCode: null, sortOrder: 2,
            startTime: null, endTime: null,
          },
        },
      );
    expect(addActivityResponse.status()).toBe(404);
  });
});
