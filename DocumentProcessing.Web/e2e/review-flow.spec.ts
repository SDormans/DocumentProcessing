import { expect, test } from '@playwright/test';
import { createPdf } from './pdf';

const username = process.env.E2E_USERNAME ?? 'demo-admin';
const password = process.env.E2E_PASSWORD ?? '';

test('upload → review → correct → approve', async ({ page }) => {
  test.skip(!password, 'Set E2E_PASSWORD (and E2E_USERNAME) to the demo admin credentials.');
  const fileName = `e2e-${Date.now()}.pdf`;

  await page.goto('/');
  await page.getByLabel('Username').fill(username);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Documents' })).toBeVisible();

  // Over 1 MB, so this also proves the web proxy forwards uploads up to the API limit.
  await page.getByLabel('Upload invoice PDFs').setInputFiles({
    name: fileName,
    mimeType: 'application/pdf',
    buffer: createPdf('Invoice E2E-001 from Acme B.V. Total EUR 121.00', 2 * 1024 * 1024),
  });
  await page.getByRole('button', { name: 'Upload PDF' }).click();
  const item = page.getByRole('link', { name: new RegExp(fileName) });
  await expect(item).toBeVisible();

  await expect(async () => {
    await page.reload();
    await expect(page.getByRole('link', { name: new RegExp(fileName) })).toContainText('NeedsReview', { timeout: 2_000 });
  }).toPass({ timeout: 90_000 });
  await page.getByRole('link', { name: new RegExp(fileName) }).click();

  await expect(page.getByTitle(`Preview of ${fileName}`)).toBeVisible();
  for (const label of ['Supplier', 'Invoice number', 'Invoice date', 'Due date', 'Currency', 'Subtotal', 'Tax', 'Total', 'Invoice lines']) {
    await expect(page.getByLabel(label, { exact: true })).toBeVisible();
  }

  const values: Record<string, string> = {
    Supplier: 'Acme B.V.',
    'Invoice number': 'E2E-001',
    'Invoice date': '2026-10-01',
    Currency: 'EUR',
    Total: '121.00',
  };
  for (const [label, value] of Object.entries(values)) {
    await page.getByLabel(label, { exact: true }).fill(value);
  }
  await page.getByRole('button', { name: 'Save corrections' }).click();
  await expect(page.getByRole('status')).toHaveText('Corrections saved.');

  await page.reload();
  await expect(page.getByLabel('Invoice number', { exact: true })).toHaveValue('E2E-001');

  await page.getByRole('button', { name: 'Approve' }).click();
  await expect(page.getByRole('heading', { name: 'Documents' })).toBeVisible();
  await expect(page.getByRole('link', { name: new RegExp(fileName) })).toContainText('Completed');
});
