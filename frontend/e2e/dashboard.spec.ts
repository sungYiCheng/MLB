import { expect, test } from '@playwright/test';

test('dashboard shell loads without runtime errors', async ({ page }) => {
  const pageErrors: Error[] = [];
  page.on('pageerror', (error) => pageErrors.push(error));

  await page.goto('/');

  await expect(page.getByRole('heading', { name: "Taiwan Today's MLB Games" })).toBeVisible();
  await expect(page.getByText('MLB AI Daily')).toBeVisible();
  await expect(page.getByText('Games', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('Live', { exact: true })).toBeVisible();
  await expect(page.getByText('Final', { exact: true })).toBeVisible();

  expect(pageErrors).toEqual([]);
});

test('daily games section can collapse and expand', async ({ page }) => {
  await page.goto('/');

  const gamesToggle = page.getByRole('button', { name: /Daily Board[\s\S]*Taiwan Today's Games/ });

  await expect(gamesToggle).toBeVisible();
  await expect(gamesToggle).toHaveAttribute('aria-expanded', 'true');

  await gamesToggle.click();
  await expect(gamesToggle).toHaveAttribute('aria-expanded', 'false');

  await gamesToggle.click();
  await expect(gamesToggle).toHaveAttribute('aria-expanded', 'true');
});

test('deployed dashboard finishes the initial data load', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByText("Loading Taiwan today's schedule...")).toBeHidden({ timeout: 60_000 });
  await expect(page.getByText('Could not load MLB data. Check that the backend API is running.')).toBeHidden();
});
