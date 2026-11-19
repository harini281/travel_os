import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import {
  API_URL, apiContextFor, createTripThroughBrowser, openTrip,
  registerThroughBrowser, signInThroughBrowser, TEST_PASSWORD, uniqueEmail,
} from "./helpers.ts";

async function inviteMemberThroughApi(page: Page, tripName: string, ownerEmail: string) {
  const owner = await apiContextFor(page);
  const memberEmail = uniqueEmail("finance-member");
  const memberRegistration = await page.request.post(`${API_URL}/api/v1/auth/register`, {
    data: { email: memberEmail, displayName: "E2E finance-member", password: TEST_PASSWORD },
  });
  expect(memberRegistration.status()).toBe(201);
  const memberAuth = await memberRegistration.json() as { accessToken: string; user: { id: string } };

  const ownerProfile = await page.request.get(`${API_URL}/api/v1/auth/me`, { headers: owner.headers });
  expect(ownerProfile.ok()).toBeTruthy();
  const ownerUser = await ownerProfile.json() as { id: string };
  const tripsResponse = await page.request.get(`${API_URL}/api/v1/trips`, { headers: owner.headers });
  expect(tripsResponse.ok()).toBeTruthy();
  const trips = await tripsResponse.json() as Array<{ id: string; name: string }>;
  const trip = trips.find(item => item.name === tripName);
  expect(trip, "The newly created trip should be listed for its owner").toBeTruthy();

  const invitationResponse = await page.request.post(
    `${API_URL}/api/v1/trips/${trip!.id}/invitations`,
    { headers: owner.headers, data: { email: memberEmail } },
  );
  expect(invitationResponse.status()).toBe(201);
  const invitation = await invitationResponse.json() as { id: string };

  const acceptResponse = await page.request.post(
    `${API_URL}/api/v1/trips/${trip!.id}/invitations/${invitation.id}/accept`,
    { headers: { Authorization: `Bearer ${memberAuth.accessToken}` } },
  );
  expect(acceptResponse.ok()).toBeTruthy();

  return {
    tripId: trip!.id,
    ownerEmail,
    ownerUserId: ownerUser.id,
    ownerToken: owner.token,
    ownerHeaders: owner.headers,
    memberEmail,
    memberUserId: memberAuth.user.id,
    memberToken: memberAuth.accessToken,
  };
}

async function createEqualExpense(page: Page, title: string, payerName: string, expectedParticipants: number) {
  const form = page.locator("form.expense-create-form");
  await form.getByLabel("What was paid for?").fill(title);
  await form.getByLabel("Category").selectOption("food");
  await form.getByLabel("Amount").fill("1000");
  await form.getByLabel("Currency").selectOption("LKR");
  await form.getByLabel("Date").fill("2027-07-10");
  await form.getByLabel("Who paid?").selectOption({ label: payerName });
  await form.getByLabel("How should this expense be split?").selectOption("equal");
  await expect(form.locator(".expense-participants input[type=checkbox]:checked")).toHaveCount(expectedParticipants);
  await form.getByRole("button", { name: /Add shared expense/ }).click();
  const card = page.locator(".expense-card").filter({ hasText: title });
  await expect(card).toBeVisible();
  return card;
}

test.describe("TravelOS finance browser journeys", () => {
  test("creates a shared expense and completes a recipient-confirmed repayment across two real accounts", async ({ page }) => {
    const ownerName = "E2E finance-owner";
    const ownerEmail = await registerThroughBrowser(page, "finance-owner");
    const tripName = `E2E Finance ${Date.now()}`;
    await createTripThroughBrowser(page, tripName);
    await openTrip(page, tripName);

    const accounts = await inviteMemberThroughApi(page, tripName, ownerEmail);
    await page.reload();
    await openTrip(page, tripName);
    await expect(page.locator(".member-list")).toContainText("E2E finance-member");

    const expense = await createEqualExpense(page, "Shared lunch bill", ownerName, 2);
    await expect(expense).toContainText("1,000 LKR");
    await expect(expense.locator(".expense-shares")).toContainText("500 LKR");

    const settlementResponse = await page.request.get(
      `${API_URL}/api/v1/trips/${accounts.tripId}/settlements`,
      { headers: accounts.ownerHeaders },
    );
    expect(settlementResponse.ok()).toBeTruthy();
    const settlement = await settlementResponse.json() as {
      currencies: Array<{
        currencyCode: string;
        balances: Array<{ userId: string; netAmount: number }>;
        suggestedTransfers: Array<{ fromUserId: string; toUserId: string; amount: number }>;
      }>;
    };
    const lkr = settlement.currencies.find(currency => currency.currencyCode === "LKR");
    expect(lkr).toBeTruthy();
    expect(lkr!.balances.find(item => item.userId === accounts.memberUserId)?.netAmount).toBe(-500);
    expect(lkr!.balances.find(item => item.userId === accounts.ownerUserId)?.netAmount).toBe(500);
    expect(lkr!.suggestedTransfers).toContainEqual({
      fromUserId: accounts.memberUserId,
      fromName: "E2E finance-member",
      toUserId: accounts.ownerUserId,
      toName: ownerName,
      amount: 500,
    });

    // The debtor submits payment. The recipient, not the debtor, must confirm it.
    await page.getByRole("button", { name: "Sign out" }).click();
    await signInThroughBrowser(page, accounts.memberEmail);
    await openTrip(page, tripName);
    const transfer = page.locator(".settlement-transfers").filter({ hasText: "E2E finance-member" });
    await expect(transfer).toContainText(ownerName);
    await transfer.getByRole("button", { name: "Record repayment" }).click();
    const repaymentForm = page.locator("form.repayment-form");
    await repaymentForm.getByLabel("Amount (LKR)").fill("500");
    await repaymentForm.getByLabel("Note (optional)").fill("Playwright repayment confirmation");
    await repaymentForm.getByRole("button", { name: "Submit repayment" }).click();

    const pending = page.locator(".repayment-record").filter({ hasText: "Playwright repayment confirmation" });
    await expect(pending).toBeVisible();
    await expect(pending.locator(".repayment-status")).toHaveText("pending");
    await expect(pending).toContainText("Waiting for recipient confirmation");

    const pendingSettlementResponse = await page.request.get(
      `${API_URL}/api/v1/trips/${accounts.tripId}/settlements`,
      { headers: { Authorization: `Bearer ${accounts.memberToken}` } },
    );
    expect(pendingSettlementResponse.ok()).toBeTruthy();
    const pendingSettlement = await pendingSettlementResponse.json() as {
      currencies: Array<{ balances: Array<{ userId: string; netAmount: number }> }>;
    };
    expect(pendingSettlement.currencies[0].balances.find(item => item.userId === accounts.memberUserId)?.netAmount).toBe(-500);

    const recordsResponse = await page.request.get(
      `${API_URL}/api/v1/trips/${accounts.tripId}/repayments`,
      { headers: { Authorization: `Bearer ${accounts.memberToken}` } },
    );
    expect(recordsResponse.ok()).toBeTruthy();
    const records = await recordsResponse.json() as Array<{ id: string; status: string }>;
    const repayment = records.find(item => item.status === "pending");
    expect(repayment).toBeTruthy();

    const selfConfirmation = await page.request.post(
      `${API_URL}/api/v1/trips/${accounts.tripId}/repayments/${repayment!.id}/respond`,
      { headers: { Authorization: `Bearer ${accounts.memberToken}` }, data: { decision: "confirm" } },
    );
    expect(selfConfirmation.status()).toBe(403);

    await page.getByRole("button", { name: "Sign out" }).click();
    await signInThroughBrowser(page, accounts.ownerEmail);
    await openTrip(page, tripName);
    const ownerPending = page.locator(".repayment-record").filter({ hasText: "Playwright repayment confirmation" });
    await expect(ownerPending.locator(".repayment-status")).toHaveText("pending");
    await ownerPending.getByRole("button", { name: "Confirm received" }).click();
    await expect(ownerPending.locator(".repayment-status")).toHaveText("confirmed");

    const finalSettlementResponse = await page.request.get(
      `${API_URL}/api/v1/trips/${accounts.tripId}/settlements`,
      { headers: accounts.ownerHeaders },
    );
    expect(finalSettlementResponse.ok()).toBeTruthy();
    const finalSettlement = await finalSettlementResponse.json() as {
      currencies: Array<{ balances: Array<{ userId: string; netAmount: number }> }>;
    };
    expect(finalSettlement.currencies[0].balances).toHaveLength(0);
  });

  test("uploads a receipt, rejects invalid content, confirms details and flags a duplicate image", async ({ page }) => {
    const ownerName = "E2E receipt-owner";
    const ownerEmail = await registerThroughBrowser(page, "receipt-owner");
    const tripName = `E2E Receipt ${Date.now()}`;
    await createTripThroughBrowser(page, tripName);
    await openTrip(page, tripName);
    await expect(page.locator(".member-list")).toContainText(ownerName);

    const expense = await createEqualExpense(page, "Receipt-linked lunch", ownerName, 1);
    await expect(expense).toBeVisible();
    const uploadForm = page.locator("form.receipt-upload-form");
    const fileInput = uploadForm.locator('input[type="file"]');

    await fileInput.setInputFiles({
      name: "invalid-receipt.txt",
      mimeType: "text/plain",
      buffer: Buffer.from("not an image"),
    });
    await uploadForm.getByRole("button", { name: "Upload receipt" }).click();
    await expect(page.locator(".alert.error")).toBeVisible();
    await expect(page.locator(".receipt-card")).toHaveCount(0);

    // Small valid PNG fixture; it is only test data and is not committed as a file.
    const png = Buffer.from(
      "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l2sAAAAASUVORK5CYII=",
      "base64",
    );
    await fileInput.setInputFiles({ name: "playwright-receipt.png", mimeType: "image/png", buffer: png });
    await uploadForm.getByLabel("Attach to expense (optional)").selectOption({ index: 1 });
    await uploadForm.getByRole("button", { name: "Upload receipt" }).click();

    const receipt = page.locator(".receipt-card").filter({ hasText: "playwright-receipt.png" });
    await expect(receipt).toBeVisible();
    await expect(receipt).toContainText("Manual review");
    await receipt.getByLabel("Merchant").fill("Playwright Test Cafe");
    await receipt.getByLabel("Receipt date").fill("2027-07-10");
    await receipt.getByLabel("Total amount").fill("1000");
    await receipt.getByLabel("Currency").fill("LKR");
    await receipt.getByLabel("Link expense").selectOption({ label: /Receipt-linked lunch/ });
    await receipt.getByRole("button", { name: "Confirm receipt details" }).click();
    await expect(receipt).toContainText("Confirmed");
    await expect(receipt).toContainText("Playwright Test Cafe");
    await expect(receipt).toContainText("1,000 LKR");

    const auth = await apiContextFor(page);
    const tripsResponse = await page.request.get(`${API_URL}/api/v1/trips`, { headers: auth.headers });
    expect(tripsResponse.ok()).toBeTruthy();
    const trips = await tripsResponse.json() as Array<{ id: string; name: string }>;
    const trip = trips.find(item => item.name === tripName);
    expect(trip).toBeTruthy();
    const receiptsResponse = await page.request.get(
      `${API_URL}/api/v1/trips/${trip!.id}/receipts`,
      { headers: auth.headers },
    );
    expect(receiptsResponse.ok()).toBeTruthy();
    const receipts = await receiptsResponse.json() as Array<{
      id: string; extractionStatus: string; expenseId: string | null;
    }>;
    expect(receipts).toHaveLength(1);
    expect(receipts[0].extractionStatus).toBe("confirmed");
    expect(receipts[0].expenseId).toBeTruthy();

    const originalFile = await page.request.get(
      `${API_URL}/api/v1/trips/${trip!.id}/receipts/${receipts[0].id}/file`,
      { headers: auth.headers },
    );
    expect(originalFile.status()).toBe(200);
    expect(originalFile.headers()["content-type"]).toContain("image/png");
    expect(await originalFile.body()).toEqual(png);

    await fileInput.setInputFiles({ name: "playwright-receipt.png", mimeType: "image/png", buffer: png });
    await uploadForm.getByLabel("Attach to expense (optional)").selectOption({ label: /Receipt-linked lunch/ });
    await uploadForm.getByRole("button", { name: "Upload receipt" }).click();
    const receiptCards = page.locator(".receipt-card").filter({ hasText: "playwright-receipt.png" });
    await expect(receiptCards).toHaveCount(2);
    await expect(receiptCards.filter({ hasText: "Possible duplicate" })).toBeVisible();

    await page.reload();
    await openTrip(page, tripName);
    await expect(page.locator(".receipt-card").filter({ hasText: "playwright-receipt.png" })).toHaveCount(2);
    await expect(page.locator(".receipt-card").filter({ hasText: "Manual review" })).toBeVisible();
  });

  test("runs axe accessibility checks on the unauthenticated registration screen", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("button", { name: "Create account", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Create your account" })).toBeVisible();

    const results = await new AxeBuilder({ page: page as never })
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
      .analyze();
    const serious = results.violations.filter(item => item.impact === "critical" || item.impact === "serious");
    expect(
      serious.map(item => ({
        id: item.id,
        impact: item.impact,
        description: item.description,
        nodes: item.nodes.map(node => node.target),
      })),
      "The registration screen should not introduce critical or serious WCAG 2.1 A/AA violations",
    ).toEqual([]);
  });
});
