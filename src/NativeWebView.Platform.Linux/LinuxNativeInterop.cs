using System.Runtime.InteropServices;
using System.Text.Json;

namespace NativeWebView.Platform.Linux;

internal static class LinuxGtkDispatcher
{
    private static readonly object Gate = new();
    private static Task<bool>? _startTask;
    private static int _gtkThreadId;

    public static async Task EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("GTK initialization is only supported on Linux.");
        }

        Task<bool> startTask;
        lock (Gate)
        {
            startTask = _startTask ??= StartAsync();
        }

        if (!await startTask.ConfigureAwait(false))
        {
            throw new InvalidOperationException("Unable to initialize GTK3 on Linux.");
        }
    }

    public static async Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await InvokeAsync(
            () =>
            {
                action();
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        if (Environment.CurrentManagedThreadId == _gtkThreadId)
        {
            return action();
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(static state =>
        {
            ((TaskCompletionSource<T>)state!).TrySetCanceled();
        }, completion);

        LinuxNativeInterop.EnqueueOnGtkThread(() =>
        {
            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return await completion.Task.ConfigureAwait(false);
    }

    private static Task<bool> StartAsync()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            _gtkThreadId = Environment.CurrentManagedThreadId;

            try
            {
                if (!LinuxNativeInterop.InitializeGtk())
                {
                    completion.TrySetResult(false);
                    return;
                }

                completion.TrySetResult(true);
                LinuxNativeInterop.RunGtkLoop();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            Name = "NativeWebView.GTK",
            IsBackground = true,
        };

        thread.Start();
        return completion.Task;
    }
}

internal sealed class LinuxUtf8StringArray : IDisposable
{
    private readonly IntPtr[] _allocatedStrings;

    public LinuxUtf8StringArray(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        _allocatedStrings = new IntPtr[values.Count];
        Pointer = Marshal.AllocHGlobal((values.Count + 1) * IntPtr.Size);

        for (var index = 0; index < values.Count; index++)
        {
            _allocatedStrings[index] = Marshal.StringToCoTaskMemUTF8(values[index]);
            Marshal.WriteIntPtr(Pointer, index * IntPtr.Size, _allocatedStrings[index]);
        }

        Marshal.WriteIntPtr(Pointer, values.Count * IntPtr.Size, IntPtr.Zero);
    }

    public IntPtr Pointer { get; }

    public void Dispose()
    {
        foreach (var stringPointer in _allocatedStrings)
        {
            if (stringPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(stringPointer);
            }
        }

        if (Pointer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(Pointer);
        }
    }
}

internal static class LinuxNativeInterop
{
    private const string GtkName = "libgtk-3.so.0";
    private const string GdkName = "libgdk-3.so.0";
    private const string GObjectName = "libgobject-2.0.so.0";
    private const string GlibName = "libglib-2.0.so.0";
    private const string WebKitName = "libwebkit2gtk-4.1.so.0";
    private const string JavaScriptCoreName = "libjavascriptcoregtk-4.1.so.0";
    private const string X11Name = "libX11.so.6";

    private static readonly object GtkInitializationGate = new();
    private static IntPtr _display;

    private static readonly GSourceFunc IdleSourceCallback = static userData =>
    {
        var handle = GCHandle.FromIntPtr(userData);

        try
        {
            ((Action)handle.Target!).Invoke();
        }
        finally
        {
            handle.Free();
        }

        return 0;
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct GError
    {
        public uint Domain;
        public int Code;
        public IntPtr Message;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GSourceFunc(IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GDestroyNotify(IntPtr data);

    internal enum GtkWindowType
    {
        TopLevel = 0,
        Popup = 1,
    }

    internal enum WebKitLoadEvent
    {
        Started = 0,
        Redirected = 1,
        Committed = 2,
        Finished = 3,
    }

    internal enum WebKitPolicyDecisionType
    {
        NavigationAction = 0,
        NewWindowAction = 1,
        Response = 2,
    }

    internal enum WebKitNetworkProxyMode
    {
        Default = 0,
        NoProxy = 1,
        Custom = 2,
    }

    internal enum WebKitCookiePersistentStorage
    {
        Text = 0,
        Sqlite = 1,
    }

    internal enum WebKitUserContentInjectedFrames
    {
        AllFrames = 0,
        TopFrame = 1,
    }

    internal enum WebKitUserScriptInjectionTime
    {
        DocumentStart = 0,
        DocumentEnd = 1,
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void LoadChangedSignal(IntPtr webView, WebKitLoadEvent loadEvent, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int LoadFailedSignal(IntPtr webView, WebKitLoadEvent loadEvent, IntPtr failingUri, IntPtr error, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int DecidePolicySignal(IntPtr webView, IntPtr decision, WebKitPolicyDecisionType decisionType, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ScriptMessageReceivedSignal(IntPtr manager, IntPtr jsResult, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ContextMenuSignal(IntPtr webView, IntPtr contextMenu, IntPtr eventHandle, IntPtr hitTestResult, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void CloseSignal(IntPtr webView, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int DeleteEventSignal(IntPtr widget, IntPtr eventHandle, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void JavaScriptFinishedCallback(IntPtr webView, IntPtr asyncResult, IntPtr userData);

    private sealed class ConnectedSignal : IDisposable
    {
        private readonly IntPtr _instance;
        private GCHandle _delegateHandle;
        private readonly ulong _signalId;
        private bool _disposed;

        public ConnectedSignal(IntPtr instance, GCHandle delegateHandle, ulong signalId)
        {
            _instance = instance;
            _delegateHandle = delegateHandle;
            _signalId = signalId;
            g_object_ref(instance);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            g_signal_handler_disconnect(_instance, _signalId);
            g_object_unref(_instance);

            if (_delegateHandle.IsAllocated)
            {
                _delegateHandle.Free();
            }
        }
    }

    private sealed class JavaScriptRequest : IDisposable
    {
        private readonly CancellationTokenRegistration _cancellationRegistration;

        public JavaScriptRequest(TaskCompletionSource<string?> completion, CancellationToken cancellationToken)
        {
            Completion = completion;
            if (cancellationToken.CanBeCanceled)
            {
                _cancellationRegistration = cancellationToken.Register(static state =>
                {
                    ((TaskCompletionSource<string?>)state!).TrySetCanceled();
                }, completion);
            }
        }

        public TaskCompletionSource<string?> Completion { get; }

        public void Dispose()
        {
            _cancellationRegistration.Dispose();
        }
    }

    [DllImport(GtkName)]
    private static extern void gtk_main_iteration();

    [DllImport(GtkName)]
    private static extern bool gtk_init_check(int argc, IntPtr argv);

    [DllImport(GtkName)]
    internal static extern IntPtr gtk_window_new(GtkWindowType type);

    [DllImport(GtkName)]
    internal static extern void gtk_window_set_decorated(IntPtr window, bool setting);

    [DllImport(GtkName)]
    internal static extern void gtk_window_set_resizable(IntPtr window, bool resizable);

    [DllImport(GtkName)]
    internal static extern void gtk_window_resize(IntPtr window, int width, int height);

    [DllImport(GtkName)]
    internal static extern void gtk_window_move(IntPtr window, int x, int y);

    [DllImport(GtkName)]
    internal static extern void gtk_window_present(IntPtr window);

    [DllImport(GtkName)]
    internal static extern void gtk_window_set_title(IntPtr window, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

    [DllImport(GtkName)]
    internal static extern void gtk_container_add(IntPtr container, IntPtr widget);

    [DllImport(GtkName)]
    internal static extern void gtk_widget_realize(IntPtr widget);

    [DllImport(GtkName)]
    internal static extern void gtk_widget_show_all(IntPtr widget);

    [DllImport(GtkName)]
    internal static extern void gtk_widget_destroy(IntPtr widget);

    [DllImport(GtkName)]
    internal static extern IntPtr gtk_widget_get_window(IntPtr widget);

    [DllImport(GtkName)]
    internal static extern void gtk_widget_grab_focus(IntPtr widget);

    [DllImport(GdkName)]
    private static extern IntPtr gdk_display_get_default();

    [DllImport(GdkName)]
    private static extern IntPtr gdk_x11_display_get_xdisplay(IntPtr display);

    [DllImport(GdkName)]
    private static extern void gdk_set_allowed_backends([MarshalAs(UnmanagedType.LPUTF8Str)] string backends);

    [DllImport(GdkName)]
    internal static extern IntPtr gdk_x11_window_get_xid(IntPtr window);

    [DllImport(X11Name)]
    private static extern int XReparentWindow(
        IntPtr display,
        IntPtr window,
        IntPtr parent,
        int x,
        int y);

    [DllImport(X11Name)]
    private static extern int XMapWindow(IntPtr display, IntPtr window);

    [DllImport(X11Name)]
    private static extern int XFlush(IntPtr display);

    [DllImport(GlibName)]
    private static extern uint g_idle_add_full(int priority, GSourceFunc function, IntPtr data, GDestroyNotify? notify);

    [DllImport(GlibName)]
    private static extern void g_error_free(IntPtr error);

    [DllImport(GlibName)]
    internal static extern void g_free(IntPtr pointer);

    [DllImport(GObjectName)]
    private static extern ulong g_signal_connect_data(
        IntPtr instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string detailedSignal,
        IntPtr handler,
        IntPtr data,
        IntPtr destroyData,
        int connectFlags);

    [DllImport(GObjectName)]
    private static extern void g_signal_handler_disconnect(IntPtr instance, ulong handlerId);

    [DllImport(GObjectName)]
    internal static extern void g_object_ref(IntPtr instance);

    [DllImport(GObjectName)]
    internal static extern void g_object_unref(IntPtr instance);

    [DllImport(WebKitName)]
    internal static extern IntPtr webkit_web_context_new();

    [DllImport(WebKitName)]
    internal static extern IntPtr webkit_web_context_new_ephemeral();

    [DllImport(WebKitName)]
    internal static extern IntPtr webkit_web_context_get_website_data_manager(IntPtr context);

    [DllImport(WebKitName)]
    internal static extern void webkit_web_context_set_preferred_languages(IntPtr context, IntPtr languages);

    [DllImport(WebKitName)]
    internal static extern IntPtr webkit_web_view_new_with_context(IntPtr context);

    [DllImport(WebKitName)]
    internal static extern IntPtr webkit_web_view_get_user_content_manager(IntPtr webView);

    [DllImport(WebKitName)]
    internal static extern IntPtr webkit_web_view_get_settings(IntPtr webView);

    [DllImport(WebKitName)]
    internal static extern IntPtr webkit_web_view_get_uri(IntPtr webView);

    [DllImport(WebKitName)]
    internal static extern void webkit_web_view_load_uri(IntPtr webView, [MarshalAs(UnmanagedType.LPUTF8Str)] string uri);

    [DllImport(WebKitName)]
    internal static extern void webkit_web_view_reload(IntPtr webView);

    [DllImport(WebKitName)]
    internal static extern void webkit_web_view_stop_loading(IntPtr webView);

    [DllImport(WebKitName)]
    internal static extern bool webkit_web_view_can_go_back(IntPtr webView);

    [DllImport(WebKitName)]
    internal static extern bool webkit_web_view_can_go_forward(IntPtr webView);

    [DllImport(WebKitName)]
    internal static extern void webkit_web_view_go_back(IntPtr webView);

    [DllImport(WebKitName)]
    internal static extern void webkit_web_view_go_forward(IntPtr webView);

    [DllImport(WebKitName)]
    internal static extern void webkit_web_view_set_zoom_level(IntPtr webView, double zoomLevel);

    [DllImport(WebKitName)]
    internal static extern double webkit_web_view_get_zoom_level(IntPtr webView);

    [DllImport(WebKitName)]
    private static extern void webkit_web_view_run_javascript(
        IntPtr webView,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string script,
        IntPtr cancellable,
        JavaScriptFinishedCallback callback,
        IntPtr userData);

    [DllImport(WebKitName)]
    private static extern IntPtr webkit_web_view_run_javascript_finish(IntPtr webView, IntPtr asyncResult, out IntPtr error);

    [DllImport(WebKitName)]
    internal static extern IntPtr webkit_web_view_get_inspector(IntPtr webView);

    [DllImport(WebKitName)]
    internal static extern IntPtr webkit_print_operation_new(IntPtr webView);

    [DllImport(WebKitName)]
    internal static extern void webkit_print_operation_print(IntPtr printOperation);

    [DllImport(WebKitName)]
    private static extern IntPtr webkit_javascript_result_get_js_value(IntPtr jsResult);

    [DllImport(WebKitName)]
    private static extern void webkit_javascript_result_unref(IntPtr jsResult);

    [DllImport(WebKitName)]
    internal static extern void webkit_settings_set_enable_developer_extras(IntPtr settings, bool enabled);

    [DllImport(WebKitName)]
    internal static extern void webkit_settings_set_user_agent(IntPtr settings, [MarshalAs(UnmanagedType.LPUTF8Str)] string? userAgent);

    [DllImport(WebKitName)]
    internal static extern bool webkit_user_content_manager_register_script_message_handler(
        IntPtr manager,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(WebKitName)]
    internal static extern void webkit_user_content_manager_add_script(IntPtr manager, IntPtr script);

    [DllImport(WebKitName)]
    internal static extern IntPtr webkit_user_script_new(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        WebKitUserContentInjectedFrames injectedFrames,
        WebKitUserScriptInjectionTime injectionTime,
        IntPtr allowList,
        IntPtr blockList);

    [DllImport(WebKitName)]
    internal static extern void webkit_user_script_unref(IntPtr userScript);

    [DllImport(WebKitName)]
    internal static extern IntPtr webkit_website_data_manager_get_cookie_manager(IntPtr manager);

    [DllImport(WebKitName)]
    internal static extern void webkit_cookie_manager_set_persistent_storage(
        IntPtr cookieManager,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
        WebKitCookiePersistentStorage storage);

    [DllImport(WebKitName)]
    internal static extern IntPtr webkit_network_proxy_settings_new(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string defaultProxyUri,
        IntPtr ignoreHosts);

    [DllImport(WebKitName)]
    internal static extern void webkit_network_proxy_settings_free(IntPtr proxySettings);

    [DllImport(WebKitName)]
    internal static extern void webkit_website_data_manager_set_network_proxy_settings(
        IntPtr manager,
        WebKitNetworkProxyMode proxyMode,
        IntPtr proxySettings);

    [DllImport(WebKitName)]
    internal static extern IntPtr webkit_navigation_policy_decision_get_request(IntPtr decision);

    [DllImport(WebKitName)]
    internal static extern IntPtr webkit_response_policy_decision_get_request(IntPtr decision);

    [DllImport(WebKitName)]
    internal static extern void webkit_policy_decision_ignore(IntPtr decision);

    [DllImport(WebKitName)]
    internal static extern void webkit_policy_decision_use(IntPtr decision);

    [DllImport(WebKitName)]
    internal static extern IntPtr webkit_uri_request_get_uri(IntPtr request);

    [DllImport(WebKitName)]
    internal static extern IntPtr webkit_uri_request_get_http_method(IntPtr request);

    [DllImport(WebKitName)]
    internal static extern void webkit_web_inspector_show(IntPtr inspector);

    [DllImport(JavaScriptCoreName)]
    private static extern IntPtr jsc_value_to_json(IntPtr value, uint indent);

    [DllImport(JavaScriptCoreName)]
    private static extern IntPtr jsc_value_to_string(IntPtr value);

    public static bool InitializeGtk()
    {
        lock (GtkInitializationGate)
        {
            if (_display != IntPtr.Zero)
            {
                return true;
            }

            try
            {
                var defaultDisplay = gdk_display_get_default();
                if (defaultDisplay != IntPtr.Zero)
                {
                    if (gdk_x11_display_get_xdisplay(defaultDisplay) == IntPtr.Zero)
                    {
                        return false;
                    }

                    _display = defaultDisplay;
                    return true;
                }

                try
                {
                    gdk_set_allowed_backends("x11");
                }
                catch
                {
                    // Best effort. GTK will fail below if X11 is unavailable.
                }

                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "/proc/nativewebview-disable-wayland");
                if (!gtk_init_check(0, IntPtr.Zero))
                {
                    return false;
                }

                _display = gdk_display_get_default();
                return _display != IntPtr.Zero && gdk_x11_display_get_xdisplay(_display) != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void RunGtkLoop()
    {
        while (true)
        {
            gtk_main_iteration();
        }
    }

    public static void EnqueueOnGtkThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var handle = GCHandle.Alloc(action);
        var callbackHandle = GCHandle.ToIntPtr(handle);
        _ = g_idle_add_full(0, IdleSourceCallback, callbackHandle, null);
    }

    public static void AttachX11WindowToParent(IntPtr childWindow, IntPtr parentWindow)
    {
        if (childWindow == IntPtr.Zero)
        {
            throw new InvalidOperationException("Child X11 window handle is invalid.");
        }

        if (parentWindow == IntPtr.Zero)
        {
            throw new InvalidOperationException("Parent X11 window handle is invalid.");
        }

        if (_display == IntPtr.Zero)
        {
            throw new InvalidOperationException("GTK display is not initialized.");
        }

        var xDisplay = gdk_x11_display_get_xdisplay(_display);
        if (xDisplay == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to resolve the X11 display for Linux native attachment.");
        }

        // XReparentWindow reports protocol errors asynchronously; its immediate int return is not a
        // Win32-style success code and must not be treated as a failure signal.
        _ = XReparentWindow(xDisplay, childWindow, parentWindow, 0, 0);
        _ = XMapWindow(xDisplay, childWindow);
        _ = XFlush(xDisplay);
    }

    public static IDisposable ConnectSignal<T>(IntPtr instance, string signalName, T handler)
        where T : Delegate
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalName);
        ArgumentNullException.ThrowIfNull(handler);

        var delegateHandle = GCHandle.Alloc(handler);
        var functionPointer = Marshal.GetFunctionPointerForDelegate(handler);
        var signalId = g_signal_connect_data(instance, signalName, functionPointer, IntPtr.Zero, IntPtr.Zero, 0);

        if (signalId == 0)
        {
            delegateHandle.Free();
            throw new InvalidOperationException($"Unable to connect GTK signal '{signalName}'.");
        }

        return new ConnectedSignal(instance, delegateHandle, signalId);
    }

    public static async Task<string?> RunJavaScriptAsync(IntPtr webView, string script, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new JavaScriptRequest(completion, cancellationToken);
        var handle = GCHandle.Alloc(request);

        try
        {
            webkit_web_view_run_javascript(webView, script, IntPtr.Zero, OnJavaScriptFinished, GCHandle.ToIntPtr(handle));
            return await completion.Task.ConfigureAwait(false);
        }
        catch
        {
            request.Dispose();
            if (handle.IsAllocated)
            {
                handle.Free();
            }

            throw;
        }
    }

    public static string? ConvertJavaScriptResultToJson(IntPtr javascriptResult)
    {
        if (javascriptResult == IntPtr.Zero)
        {
            return "null";
        }

        var value = webkit_javascript_result_get_js_value(javascriptResult);
        return ConvertJavaScriptValueToJson(value);
    }

    public static string? ConvertJavaScriptValueToJson(IntPtr value)
    {
        if (value == IntPtr.Zero)
        {
            return "null";
        }

        var jsonPointer = jsc_value_to_json(value, 0);
        if (jsonPointer != IntPtr.Zero)
        {
            try
            {
                return Marshal.PtrToStringUTF8(jsonPointer) ?? "null";
            }
            finally
            {
                g_free(jsonPointer);
            }
        }

        var stringPointer = jsc_value_to_string(value);
        if (stringPointer != IntPtr.Zero)
        {
            try
            {
                var stringValue = Marshal.PtrToStringUTF8(stringPointer);
                return JsonSerializer.Serialize(stringValue);
            }
            finally
            {
                g_free(stringPointer);
            }
        }

        return "null";
    }

    public static string? ConvertUtf8Pointer(IntPtr pointer)
    {
        return pointer == IntPtr.Zero
            ? null
            : Marshal.PtrToStringUTF8(pointer);
    }

    public static string GetErrorMessageAndFree(IntPtr error)
    {
        if (error == IntPtr.Zero)
        {
            return "Unknown WebKitGTK error.";
        }

        try
        {
            var details = Marshal.PtrToStructure<GError>(error);
            return Marshal.PtrToStringUTF8(details.Message) ?? $"WebKitGTK error {details.Code}.";
        }
        finally
        {
            g_error_free(error);
        }
    }

    private static void OnJavaScriptFinished(IntPtr webView, IntPtr asyncResult, IntPtr userData)
    {
        var handle = GCHandle.FromIntPtr(userData);

        try
        {
            var request = (JavaScriptRequest)handle.Target!;

            try
            {
                var jsResult = webkit_web_view_run_javascript_finish(webView, asyncResult, out var error);
                if (error != IntPtr.Zero)
                {
                    request.Completion.TrySetException(new InvalidOperationException(GetErrorMessageAndFree(error)));
                    return;
                }

                try
                {
                    request.Completion.TrySetResult(ConvertJavaScriptResultToJson(jsResult));
                }
                finally
                {
                    if (jsResult != IntPtr.Zero)
                    {
                        webkit_javascript_result_unref(jsResult);
                    }
                }
            }
            catch (Exception ex)
            {
                request.Completion.TrySetException(ex);
            }
            finally
            {
                request.Dispose();
            }
        }
        finally
        {
            handle.Free();
        }
    }
}
