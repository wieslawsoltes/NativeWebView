using NativeWebView.Core;
using NativeWebView.Interop;
using NativeWebView.Platform.Linux;
using NativeWebView.Platform.Windows;
using NativeWebView.Platform.macOS;

namespace NativeWebView.Sample.Desktop;

internal static class DesktopSampleSmokeRunner
{
    public static async Task<int> RunAsync()
    {
        var platform = NativeWebViewRuntime.CurrentPlatform;

        if (platform is not (NativeWebViewPlatform.Windows or NativeWebViewPlatform.MacOS or NativeWebViewPlatform.Linux))
        {
            Console.Error.WriteLine($"Desktop sample supports Windows/macOS/Linux. Current platform: {platform}.");
            return 2;
        }

        var factory = new NativeWebViewBackendFactory();
        RegisterDesktop(factory, platform);
        PrintDiagnostics(factory, platform);

        var checks = new List<(string Name, bool Passed, string? Details)>();

        await RunWebViewChecksAsync(factory, platform, checks);
        await RunDialogChecksAsync(factory, platform, checks);
        await RunAuthChecksAsync(factory, platform, checks);

        var failedCount = checks.Count(static c => !c.Passed);

        Console.WriteLine($"Desktop backend matrix for {platform}:");
        foreach (var check in checks)
        {
            var status = check.Passed ? "PASS" : "FAIL";
            var details = string.IsNullOrWhiteSpace(check.Details) ? string.Empty : $" ({check.Details})";
            Console.WriteLine($"[{status}] {check.Name}{details}");
        }

        Console.WriteLine($"Result: {checks.Count - failedCount}/{checks.Count} checks passed.");
        return failedCount == 0 ? 0 : 1;
    }

    private static void RegisterDesktop(NativeWebViewBackendFactory factory, NativeWebViewPlatform platform)
    {
        switch (platform)
        {
            case NativeWebViewPlatform.Windows:
                factory.UseNativeWebViewWindows();
                break;
            case NativeWebViewPlatform.MacOS:
                factory.UseNativeWebViewMacOS();
                break;
            case NativeWebViewPlatform.Linux:
                factory.UseNativeWebViewLinux();
                break;
        }
    }

    private static void PrintDiagnostics(NativeWebViewBackendFactory factory, NativeWebViewPlatform platform)
    {
        var diagnostics = factory.GetPlatformDiagnosticsOrDefault(platform);

        Console.WriteLine($"Diagnostics for {platform} ({diagnostics.ProviderName}, ready={diagnostics.IsReady}):");
        foreach (var issue in diagnostics.Issues)
        {
            var recommendation = string.IsNullOrWhiteSpace(issue.Recommendation)
                ? string.Empty
                : $" Recommendation: {issue.Recommendation}";
            Console.WriteLine($"- [{issue.Severity}] {issue.Code}: {issue.Message}{recommendation}");
        }

        Console.WriteLine();

        if (GetBooleanEnvironmentVariable("NATIVEWEBVIEW_DIAGNOSTICS_REQUIRE_READY"))
        {
            var warningsAsErrors = GetBooleanEnvironmentVariable("NATIVEWEBVIEW_DIAGNOSTICS_WARNINGS_AS_ERRORS");
            NativeWebViewDiagnosticsValidator.EnsureReady(diagnostics, warningsAsErrors);
        }
    }

    private static bool GetBooleanEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return bool.TryParse(value, out var parsed)
            ? parsed
            : string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task RunWebViewChecksAsync(
        NativeWebViewBackendFactory factory,
        NativeWebViewPlatform platform,
        List<(string Name, bool Passed, string? Details)> checks)
    {
        var runInteractiveRuntimeChecks = ShouldRunDesktopRuntimeInteractionChecks(platform);
        var created = factory.TryCreateNativeWebViewBackend(platform, out var backend);
        checks.Add(("Create webview backend", created, created ? null : "fallback backend used"));

        if (!created)
        {
            return;
        }

        using (backend)
        {
            var environmentRequestedCount = 0;
            var controllerRequestedCount = 0;
            backend.CoreWebView2EnvironmentRequested += (_, _) => environmentRequestedCount++;
            backend.CoreWebView2ControllerOptionsRequested += (_, _) => controllerRequestedCount++;

            await ExecuteCheckAsync("Initialize webview", checks, async () =>
            {
                await backend.InitializeAsync();
            });

            ExecuteCheck("Environment options hook", checks, () =>
            {
                if (environmentRequestedCount == 0)
                {
                    throw new InvalidOperationException("Environment options hook was not raised.");
                }
            });

            ExecuteCheck("Controller options hook", checks, () =>
            {
                if (controllerRequestedCount == 0)
                {
                    throw new InvalidOperationException("Controller options hook was not raised.");
                }
            });

            ExecuteCheck("WebView platform handle", checks, () =>
            {
                var provider = backend as INativeWebViewPlatformHandleProvider
                    ?? throw new InvalidOperationException("Missing INativeWebViewPlatformHandleProvider.");
                RequireHandle(provider.TryGetPlatformHandle(out var handle), handle, "platform");
            });

            ExecuteCheck("WebView view handle", checks, () =>
            {
                var provider = backend as INativeWebViewPlatformHandleProvider
                    ?? throw new InvalidOperationException("Missing INativeWebViewPlatformHandleProvider.");
                RequireHandle(provider.TryGetViewHandle(out var handle), handle, "view");
            });

            ExecuteCheck("WebView controller handle", checks, () =>
            {
                var provider = backend as INativeWebViewPlatformHandleProvider
                    ?? throw new InvalidOperationException("Missing INativeWebViewPlatformHandleProvider.");
                RequireHandle(provider.TryGetControllerHandle(out var handle), handle, "controller");
            });

            ExecuteCheck("Cookie manager", checks, () =>
            {
                if (!backend.TryGetCookieManager(out var cookieManager) || cookieManager is null)
                {
                    throw new InvalidOperationException("Cookie manager is unavailable.");
                }
            });

            ExecuteCheck("Command manager", checks, () =>
            {
                if (!backend.TryGetCommandManager(out var commandManager) || commandManager is null)
                {
                    throw new InvalidOperationException("Command manager is unavailable.");
                }
            });

            ExecuteCheck("Navigate", checks, () =>
            {
                backend.Navigate("https://example.com/");
            });

            await ExecuteOptionalCheckAsync("Execute script", checks, runInteractiveRuntimeChecks, async () =>
            {
                _ = await backend.ExecuteScriptAsync("1 + 1");
            }, "runtime smoke disabled on this host");

            await ExecuteOptionalCheckAsync("Post web message", checks, runInteractiveRuntimeChecks, async () =>
            {
                await backend.PostWebMessageAsStringAsync("phase2-matrix");
            }, "runtime smoke disabled on this host");

            await ExecuteOptionalCheckAsync("Print", checks, runInteractiveRuntimeChecks, async () =>
            {
                _ = await backend.PrintAsync(new NativeWebViewPrintSettings { OutputPath = "matrix.pdf" });
            }, "runtime smoke disabled on this host");

            await ExecuteOptionalCheckAsync("Show print UI", checks, runInteractiveRuntimeChecks, async () =>
            {
                _ = await backend.ShowPrintUiAsync();
            }, "runtime smoke disabled on this host");

            if (backend.Features.Supports(NativeWebViewFeature.DevTools))
            {
                ExecuteOptionalCheck("Open devtools", checks, runInteractiveRuntimeChecks, backend.OpenDevToolsWindow, "runtime smoke disabled on this host");
            }
            else
            {
                checks.Add(("Open devtools", true, "not supported on this backend"));
            }
        }
    }

    private static async Task RunDialogChecksAsync(
        NativeWebViewBackendFactory factory,
        NativeWebViewPlatform platform,
        List<(string Name, bool Passed, string? Details)> checks)
    {
        var runInteractiveRuntimeChecks = ShouldRunDesktopRuntimeInteractionChecks(platform);
        var created = factory.TryCreateNativeWebDialogBackend(platform, out var backend);
        checks.Add(("Create dialog backend", created, created ? null : "dialog backend not registered"));

        if (!created)
        {
            return;
        }

        using (backend)
        {
            ExecuteCheck("Dialog platform handle", checks, () =>
            {
                var provider = backend as INativeWebDialogPlatformHandleProvider
                    ?? throw new InvalidOperationException("Missing INativeWebDialogPlatformHandleProvider.");
                RequireHandle(provider.TryGetPlatformHandle(out var handle), handle, "platform");
            });

            ExecuteCheck("Dialog view handle", checks, () =>
            {
                var provider = backend as INativeWebDialogPlatformHandleProvider
                    ?? throw new InvalidOperationException("Missing INativeWebDialogPlatformHandleProvider.");
                RequireHandle(provider.TryGetDialogHandle(out var handle), handle, "dialog");
            });

            ExecuteCheck("Dialog host handle", checks, () =>
            {
                var provider = backend as INativeWebDialogPlatformHandleProvider
                    ?? throw new InvalidOperationException("Missing INativeWebDialogPlatformHandleProvider.");
                RequireHandle(provider.TryGetHostWindowHandle(out var handle), handle, "host");
            });

            ExecuteOptionalCheck("Show dialog", checks, runInteractiveRuntimeChecks, () => backend.Show(new NativeWebDialogShowOptions { Title = "Matrix" }), "runtime smoke disabled on this host");
            ExecuteOptionalCheck("Dialog navigate", checks, runInteractiveRuntimeChecks, () => backend.Navigate("https://example.com/dialog"), "runtime smoke disabled on this host");

            await ExecuteOptionalCheckAsync("Dialog execute script", checks, runInteractiveRuntimeChecks, async () =>
            {
                _ = await backend.ExecuteScriptAsync("window.location.href");
            }, "runtime smoke disabled on this host");

            await ExecuteOptionalCheckAsync("Dialog post message", checks, runInteractiveRuntimeChecks, async () =>
            {
                await backend.PostWebMessageAsJsonAsync("{\"message\":\"phase2\"}");
            }, "runtime smoke disabled on this host");

            if (backend.Features.Supports(NativeWebViewFeature.DevTools))
            {
                ExecuteOptionalCheck("Dialog open devtools", checks, runInteractiveRuntimeChecks, backend.OpenDevToolsWindow, "runtime smoke disabled on this host");
            }
            else
            {
                checks.Add(("Dialog open devtools", true, "not supported on this backend"));
            }

            ExecuteOptionalCheck("Close dialog", checks, runInteractiveRuntimeChecks, backend.Close, "runtime smoke disabled on this host");
        }
    }

    private static async Task RunAuthChecksAsync(
        NativeWebViewBackendFactory factory,
        NativeWebViewPlatform platform,
        List<(string Name, bool Passed, string? Details)> checks)
    {
        var runInteractiveRuntimeChecks = ShouldRunDesktopRuntimeInteractionChecks(platform);
        var created = factory.TryCreateWebAuthenticationBrokerBackend(platform, out var backend);
        checks.Add(("Create auth backend", created, created ? null : "auth backend not registered"));

        if (!created)
        {
            return;
        }

        var request = runInteractiveRuntimeChecks
            ? new Uri("https://example.com/auth")
            : new Uri("https://example.com/callback?state=desktop#result=success");
        var callback = new Uri("https://example.com/callback");

        await ExecuteCheckAsync("Authenticate", checks, async () =>
        {
            var result = await backend.AuthenticateAsync(request, callback);

            if (runInteractiveRuntimeChecks)
            {
                if (result.ResponseStatus is not (WebAuthenticationStatus.Success or WebAuthenticationStatus.UserCancel))
                {
                    throw new InvalidOperationException($"Unexpected auth status: {result.ResponseStatus}");
                }
            }
            else if (result.ResponseStatus is not WebAuthenticationStatus.Success)
            {
                throw new InvalidOperationException($"Unexpected auth status: {result.ResponseStatus}");
            }
        });

        backend.Dispose();
    }

    private static void ExecuteCheck(string name, List<(string Name, bool Passed, string? Details)> checks, Action action)
    {
        try
        {
            action();
            checks.Add((name, true, null));
        }
        catch (Exception ex)
        {
            checks.Add((name, false, ex.GetType().Name));
        }
    }

    private static void ExecuteOptionalCheck(
        string name,
        List<(string Name, bool Passed, string? Details)> checks,
        bool enabled,
        Action action,
        string skipReason)
    {
        if (!enabled)
        {
            checks.Add((name, true, skipReason));
            return;
        }

        ExecuteCheck(name, checks, action);
    }

    private static async Task ExecuteCheckAsync(string name, List<(string Name, bool Passed, string? Details)> checks, Func<Task> action)
    {
        try
        {
            await action();
            checks.Add((name, true, null));
        }
        catch (Exception ex)
        {
            checks.Add((name, false, ex.GetType().Name));
        }
    }

    private static async Task ExecuteOptionalCheckAsync(
        string name,
        List<(string Name, bool Passed, string? Details)> checks,
        bool enabled,
        Func<Task> action,
        string skipReason)
    {
        if (!enabled)
        {
            checks.Add((name, true, skipReason));
            return;
        }

        await ExecuteCheckAsync(name, checks, action);
    }

    private static void RequireHandle(bool available, NativePlatformHandle handle, string scope)
    {
        if (!available)
        {
            throw new InvalidOperationException($"{scope} handle was not available.");
        }

        if (handle.Handle == nint.Zero || string.IsNullOrWhiteSpace(handle.HandleDescriptor))
        {
            throw new InvalidOperationException($"{scope} handle was invalid.");
        }
    }

    private static bool ShouldRunDesktopRuntimeInteractionChecks(NativeWebViewPlatform platform)
    {
        var overrideValue = Environment.GetEnvironmentVariable("NATIVEWEBVIEW_DESKTOP_RUNTIME_SMOKE");
        if (!string.IsNullOrWhiteSpace(overrideValue))
        {
            return bool.TryParse(overrideValue, out var enabled)
                ? enabled
                : string.Equals(overrideValue, "1", StringComparison.OrdinalIgnoreCase);
        }

        var isCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetEnvironmentVariable("CI"), "1", StringComparison.OrdinalIgnoreCase);

        return !isCi || platform == NativeWebViewPlatform.MacOS;
    }
}
