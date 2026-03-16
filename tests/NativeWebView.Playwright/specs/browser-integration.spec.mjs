import { test, expect } from '@playwright/test';

const resultPrefix = 'NATIVEWEBVIEW_INTEGRATION_RESULT:';

test('browser integration app reports success', async ({ page }) => {
  test.setTimeout(120_000);

  const consoleResultPromise = new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      reject(new Error('Timed out waiting for browser integration result.'));
    }, 90_000);

    page.on('console', (message) => {
      const text = message.text();
      if (!text.includes(resultPrefix)) {
        return;
      }

      clearTimeout(timer);
      resolve(text);
    });
  });

  await page.goto('/');

  const rawLine = await Promise.race([
    consoleResultPromise,
    page
      .waitForFunction(
        (prefix) => {
          const value = globalThis.__nativeWebViewIntegrationResult;
          return typeof value === 'string' && value.includes(prefix);
        },
        resultPrefix,
        { timeout: 90_000 }
      )
      .then(async () => page.evaluate(() => globalThis.__nativeWebViewIntegrationResult))
  ]);

  const json = rawLine.slice(rawLine.indexOf(resultPrefix) + resultPrefix.length).trim();
  const result = JSON.parse(json);

  expect(result.platform).toBe('Browser');
  expect(result.passed).toBe(true);

  const scenarioMap = new Map(result.scenarios.map((scenario) => [scenario.name, scenario]));
  expect(scenarioMap.get('webview')?.passed).toBe(true);
  expect(scenarioMap.get('dialog')?.passed).toBe(true);
  expect(scenarioMap.get('auth')?.passed).toBe(true);
});
