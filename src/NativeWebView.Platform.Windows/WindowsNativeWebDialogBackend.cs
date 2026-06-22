using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NativeWebView.Core;
using NativeWebView.Interop;

namespace NativeWebView.Platform.Windows;

public sealed class WindowsNativeWebDialogBackend : INativeWebDialogBackend, INativeWebDialogPlatformHandleProvider, INativeWebViewInstanceConfigurationTarget
{
    private static readonly NativePlatformHandle PlaceholderPlatformHandle = new((nint)0x1101, "HWND");
    private static readonly NativePlatformHandle PlaceholderDialogHandle = new((nint)0x1102, "ICoreWebView2");
    private static readonly NativePlatformHandle PlaceholderHostWindowHandle = new((nint)0x1103, "HWND");

    private static readonly object WindowClassGate = new();
    private static readonly Win32.WndProc WindowProcDelegate = WindowProc;

    private static ushort _windowClassAtom;

    private readonly WindowsNativeWebViewBackend _backend = new();

    private GCHandle _selfHandle;
    private nint _windowHandle;
    private nint _childHostHandle;

    private bool _isVisible;
    private bool _disposed;
    private bool _suppressDestroyNotification;

    public WindowsNativeWebDialogBackend()
    {
        Platform = NativeWebViewPlatform.Windows;
        Features = WindowsPlatformFeatures.Instance;

        _backend.NavigationStarted += OnNavigationStarted;
        _backend.NavigationCompleted += OnNavigationCompleted;
        _backend.WebMessageReceived += OnWebMessageReceived;
        _backend.NewWindowRequested += OnNewWindowRequested;
        _backend.WebResourceRequested += OnWebResourceRequested;
        _backend.ContextMenuRequested += OnContextMenuRequested;
        _backend.DestroyRequested += OnDestroyRequested;
    }

    public NativeWebViewPlatform Platform { get; }

    public IWebViewPlatformFeatures Features { get; }

    public bool IsVisible => _isVisible;

    public Uri? CurrentUrl => _backend.CurrentUrl;

    public bool CanGoBack => _backend.CanGoBack;

    public bool CanGoForward => _backend.CanGoForward;

    public bool IsDevToolsEnabled
    {
        get => _backend.IsDevToolsEnabled;
        set
        {
            EnsureNotDisposed();
            _backend.IsDevToolsEnabled = value;
        }
    }

    public bool IsContextMenuEnabled
    {
        get => _backend.IsContextMenuEnabled;
        set
        {
            EnsureNotDisposed();
            _backend.IsContextMenuEnabled = value;
        }
    }

    public bool IsStatusBarEnabled
    {
        get => _backend.IsStatusBarEnabled;
        set
        {
            EnsureNotDisposed();
            _backend.IsStatusBarEnabled = value;
        }
    }

    public bool IsZoomControlEnabled
    {
        get => _backend.IsZoomControlEnabled;
        set
        {
            EnsureNotDisposed();
            _backend.IsZoomControlEnabled = value;
        }
    }

    public double ZoomFactor => _backend.ZoomFactor;

    public string? HeaderString => _backend.HeaderString;

    public string? UserAgentString => _backend.UserAgentString;

    public event EventHandler<EventArgs>? Shown;

    public event EventHandler<EventArgs>? Closed;

    public event EventHandler<NativeWebViewNavigationStartedEventArgs>? NavigationStarted;

    public event EventHandler<NativeWebViewNavigationCompletedEventArgs>? NavigationCompleted;

    public event EventHandler<NativeWebViewMessageReceivedEventArgs>? WebMessageReceived;

    public event EventHandler<NativeWebViewNewWindowRequestedEventArgs>? NewWindowRequested;

    public event EventHandler<NativeWebViewResourceRequestedEventArgs>? WebResourceRequested;

    public event EventHandler<NativeWebViewContextMenuRequestedEventArgs>? ContextMenuRequested;

    public void ApplyInstanceConfiguration(NativeWebViewInstanceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        EnsureNotDisposed();

        if (_backend is INativeWebViewInstanceConfigurationTarget configurationTarget)
        {
            configurationTarget.ApplyInstanceConfiguration(configuration);
        }
    }

    public void Show(NativeWebDialogShowOptions? options = null)
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(Show));

        if (OperatingSystem.IsWindows())
        {
            EnsureWindowCreated();
            ApplyWindowOptions(options);
            Win32.ShowWindow(_windowHandle, Win32.ShowWindowCommand.Show);
            Win32.UpdateWindow(_windowHandle);
            UpdateChildHostBounds();
        }

        _isVisible = true;
        Shown?.Invoke(this, EventArgs.Empty);
    }

    public void Close()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(Close));

        if (!_isVisible && _windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            DestroyWindowCore();
            return;
        }

        NotifyClosed();
    }

    public void Move(double left, double top)
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(Move));

        if (!OperatingSystem.IsWindows() || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (!Win32.GetWindowRect(_windowHandle, out var rect))
        {
            return;
        }

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        Win32.MoveWindow(_windowHandle, RoundToInt(left), RoundToInt(top), width, height, true);
    }

    public void Resize(double width, double height)
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(Resize));

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Dialog size must be greater than zero.");
        }

        if (!OperatingSystem.IsWindows() || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        Win32.ResizeWindowToClientSize(_windowHandle, RoundToInt(width), RoundToInt(height));
    }

    public void Navigate(string url)
    {
        EnsureNotDisposed();
        _backend.Navigate(url);
    }

    public void Navigate(Uri uri)
    {
        EnsureNotDisposed();
        _backend.Navigate(uri);
    }

    public void Reload()
    {
        EnsureNotDisposed();
        _backend.Reload();
    }

    public void Stop()
    {
        EnsureNotDisposed();
        _backend.Stop();
    }

    public void GoBack()
    {
        EnsureNotDisposed();
        _backend.GoBack();
    }

    public void GoForward()
    {
        EnsureNotDisposed();
        _backend.GoForward();
    }

    public Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        return _backend.ExecuteScriptAsync(script, cancellationToken);
    }

    public Task PostWebMessageAsJsonAsync(string message, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        return _backend.PostWebMessageAsJsonAsync(message, cancellationToken);
    }

    public Task PostWebMessageAsStringAsync(string message, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        return _backend.PostWebMessageAsStringAsync(message, cancellationToken);
    }

    public void OpenDevToolsWindow()
    {
        EnsureNotDisposed();
        _backend.OpenDevToolsWindow();
    }

    public Task<NativeWebViewPrintResult> PrintAsync(NativeWebViewPrintSettings? settings = null, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        return _backend.PrintAsync(settings, cancellationToken);
    }

    public Task<bool> ShowPrintUiAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        return _backend.ShowPrintUiAsync(cancellationToken);
    }

    public void SetZoomFactor(double zoomFactor)
    {
        EnsureNotDisposed();
        _backend.SetZoomFactor(zoomFactor);
    }

    public void SetUserAgent(string? userAgent)
    {
        EnsureNotDisposed();
        _backend.SetUserAgent(userAgent);
    }

    public void SetHeader(string? header)
    {
        EnsureNotDisposed();
        _backend.SetHeader(header);
    }

    public bool TryGetDownloadManager(out INativeWebViewDownloadManager? downloadManager)
    {
        EnsureNotDisposed();
        return _backend.TryGetDownloadManager(out downloadManager);
    }

    public bool TryGetPlatformHandle(out NativePlatformHandle handle)
    {
        handle = _windowHandle != IntPtr.Zero
            ? new NativePlatformHandle(_windowHandle, "HWND")
            : PlaceholderPlatformHandle;
        return true;
    }

    public bool TryGetDialogHandle(out NativePlatformHandle handle)
    {
        if (_backend is INativeWebViewPlatformHandleProvider provider &&
            provider.TryGetViewHandle(out handle))
        {
            return true;
        }

        handle = PlaceholderDialogHandle;
        return true;
    }

    public bool TryGetHostWindowHandle(out NativePlatformHandle handle)
    {
        handle = _windowHandle != IntPtr.Zero
            ? new NativePlatformHandle(_windowHandle, "HWND")
            : PlaceholderHostWindowHandle;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            DestroyWindowCore();
        }
        catch
        {
            // Best-effort cleanup for native resources.
        }

        _backend.NavigationStarted -= OnNavigationStarted;
        _backend.NavigationCompleted -= OnNavigationCompleted;
        _backend.WebMessageReceived -= OnWebMessageReceived;
        _backend.NewWindowRequested -= OnNewWindowRequested;
        _backend.WebResourceRequested -= OnWebResourceRequested;
        _backend.ContextMenuRequested -= OnContextMenuRequested;
        _backend.DestroyRequested -= OnDestroyRequested;
        _backend.Dispose();
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        var userData = Win32.GetWindowLongPtr(hwnd, Win32.WindowLongIndex.GWLP_USERDATA);
        if (userData != IntPtr.Zero)
        {
            var handle = GCHandle.FromIntPtr(userData);
            if (handle.IsAllocated && handle.Target is WindowsNativeWebDialogBackend backend)
            {
                return backend.ProcessWindowMessage(message, wParam, lParam);
            }
        }

        return Win32.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private IntPtr ProcessWindowMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        _ = wParam;
        _ = lParam;

        switch (message)
        {
            case Win32.WindowMessage.WM_SIZE:
                UpdateChildHostBounds();
                return IntPtr.Zero;

            case Win32.WindowMessage.WM_CLOSE:
                Close();
                return IntPtr.Zero;

            case Win32.WindowMessage.WM_DESTROY:
                if (!_suppressDestroyNotification)
                {
                    HandleUnexpectedWindowDestruction();
                }

                return IntPtr.Zero;
        }

        return Win32.DefWindowProc(_windowHandle, message, wParam, lParam);
    }

    [SupportedOSPlatform("windows")]
    private void EnsureWindowCreated()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            return;
        }

        EnsureWindowClassRegistered();

        var instanceHandle = Win32.GetModuleHandle(null);
        var windowHandle = Win32.CreateWindowEx(
            0,
            Win32.DialogWindowClassName,
            "Authentication",
            Win32.WindowStyles.WS_OVERLAPPEDWINDOW | Win32.WindowStyles.WS_CLIPCHILDREN,
            Win32.WindowPosition.CW_USEDEFAULT,
            Win32.WindowPosition.CW_USEDEFAULT,
            900,
            700,
            IntPtr.Zero,
            IntPtr.Zero,
            instanceHandle,
            IntPtr.Zero);

        if (windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Failed to create the Windows dialog host window. Win32 error: {Marshal.GetLastWin32Error()}.");
        }

        _windowHandle = windowHandle;

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        _selfHandle = GCHandle.Alloc(this);
        Win32.SetWindowLongPtr(_windowHandle, Win32.WindowLongIndex.GWLP_USERDATA, GCHandle.ToIntPtr(_selfHandle));

        _childHostHandle = _backend.AttachToNativeParent(new NativePlatformHandle(_windowHandle, "HWND")).Handle;
        UpdateChildHostBounds();
    }

    [SupportedOSPlatform("windows")]
    private void ApplyWindowOptions(NativeWebDialogShowOptions? options)
    {
        options ??= new NativeWebDialogShowOptions();

        var title = string.IsNullOrWhiteSpace(options.Title) ? "Authentication" : options.Title;
        Win32.SetWindowText(_windowHandle, title);
        Win32.ResizeWindowToClientSize(_windowHandle, RoundToInt(options.Width), RoundToInt(options.Height));

        if (options.CenterOnParent)
        {
            Win32.CenterWindowOnPrimaryScreen(_windowHandle);
        }
        else
        {
            if (Win32.GetWindowRect(_windowHandle, out var rect))
            {
                var width = Math.Max(1, rect.Right - rect.Left);
                var height = Math.Max(1, rect.Bottom - rect.Top);
                Win32.MoveWindow(_windowHandle, RoundToInt(options.Left), RoundToInt(options.Top), width, height, true);
            }
        }
    }

    private void UpdateChildHostBounds()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (_windowHandle == IntPtr.Zero || _childHostHandle == IntPtr.Zero)
        {
            return;
        }

        if (!Win32.GetClientRect(_windowHandle, out var rect))
        {
            return;
        }

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        Win32.MoveWindow(_childHostHandle, 0, 0, width, height, true);
    }

    private void DestroyWindowCore()
    {
        var hadWindow = _windowHandle != IntPtr.Zero;

        if (hadWindow && OperatingSystem.IsWindows())
        {
            _suppressDestroyNotification = true;

            try
            {
                _backend.DetachFromNativeParent();
                _childHostHandle = IntPtr.Zero;

                if (_selfHandle.IsAllocated)
                {
                    Win32.SetWindowLongPtr(_windowHandle, Win32.WindowLongIndex.GWLP_USERDATA, IntPtr.Zero);
                }

                if (Win32.IsWindow(_windowHandle))
                {
                    Win32.DestroyWindow(_windowHandle);
                }
            }
            finally
            {
                _windowHandle = IntPtr.Zero;
                _suppressDestroyNotification = false;
            }
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        NotifyClosed();
    }

    private void HandleUnexpectedWindowDestruction()
    {
        try
        {
            _backend.DetachFromNativeParent();
        }
        catch
        {
            // Native teardown is already in progress.
        }

        _childHostHandle = IntPtr.Zero;
        _windowHandle = IntPtr.Zero;

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        NotifyClosed();
    }

    private void NotifyClosed()
    {
        if (!_isVisible)
        {
            return;
        }

        _isVisible = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnNavigationStarted(object? sender, NativeWebViewNavigationStartedEventArgs e)
    {
        _ = sender;
        NavigationStarted?.Invoke(this, e);
    }

    private void OnNavigationCompleted(object? sender, NativeWebViewNavigationCompletedEventArgs e)
    {
        _ = sender;
        NavigationCompleted?.Invoke(this, e);
    }

    private void OnWebMessageReceived(object? sender, NativeWebViewMessageReceivedEventArgs e)
    {
        _ = sender;
        WebMessageReceived?.Invoke(this, e);
    }

    private void OnNewWindowRequested(object? sender, NativeWebViewNewWindowRequestedEventArgs e)
    {
        _ = sender;
        NewWindowRequested?.Invoke(this, e);
    }

    private void OnWebResourceRequested(object? sender, NativeWebViewResourceRequestedEventArgs e)
    {
        _ = sender;
        WebResourceRequested?.Invoke(this, e);
    }

    private void OnContextMenuRequested(object? sender, NativeWebViewContextMenuRequestedEventArgs e)
    {
        _ = sender;
        ContextMenuRequested?.Invoke(this, e);
    }

    private void OnDestroyRequested(object? sender, NativeWebViewDestroyRequestedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (_isVisible)
        {
            Close();
        }
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureFeature(NativeWebViewFeature feature, string operationName)
    {
        if (!Features.Supports(feature))
        {
            throw new PlatformNotSupportedException(
                $"Operation '{operationName}' is not supported on platform '{Platform}'.");
        }
    }

    private static int RoundToInt(double value)
    {
        return Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));
    }

    [SupportedOSPlatform("windows")]
    private static void EnsureWindowClassRegistered()
    {
        if (_windowClassAtom != 0)
        {
            return;
        }

        lock (WindowClassGate)
        {
            if (_windowClassAtom != 0)
            {
                return;
            }

            var instanceHandle = Win32.GetModuleHandle(null);
            var windowClass = new Win32.WndClassEx
            {
                cbSize = (uint)Marshal.SizeOf<Win32.WndClassEx>(),
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WindowProcDelegate),
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = instanceHandle,
                hIcon = IntPtr.Zero,
                hCursor = Win32.LoadCursor(IntPtr.Zero, Win32.CursorIdcArrow),
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = Win32.DialogWindowClassName,
                hIconSm = IntPtr.Zero,
            };

            _windowClassAtom = Win32.RegisterClassEx(ref windowClass);
            if (_windowClassAtom == 0)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != Win32.ErrorClassAlreadyExists)
                {
                    throw new InvalidOperationException(
                        $"Failed to register the Windows dialog host window class. Win32 error: {error}.");
                }

                _windowClassAtom = ushort.MaxValue;
            }
        }
    }

    private static class Win32
    {
        public const int CursorIdcArrow = 32512;
        public const int ErrorClassAlreadyExists = 1410;
        public const string DialogWindowClassName = "NativeWebView.DialogWindow";

        internal static class WindowLongIndex
        {
            public const int GWLP_USERDATA = -21;
        }

        internal static class WindowStyles
        {
            public const uint WS_OVERLAPPED = 0x00000000;
            public const uint WS_CAPTION = 0x00C00000;
            public const uint WS_SYSMENU = 0x00080000;
            public const uint WS_THICKFRAME = 0x00040000;
            public const uint WS_MINIMIZEBOX = 0x00020000;
            public const uint WS_MAXIMIZEBOX = 0x00010000;
            public const uint WS_CLIPCHILDREN = 0x02000000;
            public const uint WS_OVERLAPPEDWINDOW = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
        }

        internal static class WindowMessage
        {
            public const uint WM_SIZE = 0x0005;
            public const uint WM_CLOSE = 0x0010;
            public const uint WM_DESTROY = 0x0002;
        }

        internal static class ShowWindowCommand
        {
            public const int Show = 5;
        }

        internal static class WindowPosition
        {
            public const int CW_USEDEFAULT = unchecked((int)0x80000000);
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WndClassEx
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpszClassName;
            public IntPtr hIconSm;
        }

        internal delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern ushort RegisterClassEx([In] ref WndClassEx lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateWindowEx(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool repaint);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool AdjustWindowRectEx(ref Rect lpRect, uint dwStyle, bool bMenu, uint dwExStyle);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetWindowText(IntPtr hWnd, string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [DllImport("user32.dll", EntryPoint = "DefWindowProcW", SetLastError = true)]
        internal static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SmCxScreen = 0;
        private const int SmCyScreen = 1;

        public static void ResizeWindowToClientSize(IntPtr windowHandle, int clientWidth, int clientHeight)
        {
            var rect = new Rect
            {
                Left = 0,
                Top = 0,
                Right = Math.Max(1, clientWidth),
                Bottom = Math.Max(1, clientHeight),
            };

            _ = AdjustWindowRectEx(ref rect, WindowStyles.WS_OVERLAPPEDWINDOW | WindowStyles.WS_CLIPCHILDREN, false, 0);
            var width = Math.Max(1, rect.Right - rect.Left);
            var height = Math.Max(1, rect.Bottom - rect.Top);

            if (GetWindowRect(windowHandle, out var currentRect))
            {
                MoveWindow(windowHandle, currentRect.Left, currentRect.Top, width, height, true);
            }
        }

        public static void CenterWindowOnPrimaryScreen(IntPtr windowHandle)
        {
            if (!GetWindowRect(windowHandle, out var rect))
            {
                return;
            }

            var screenWidth = GetSystemMetrics(SmCxScreen);
            var screenHeight = GetSystemMetrics(SmCyScreen);
            var width = Math.Max(1, rect.Right - rect.Left);
            var height = Math.Max(1, rect.Bottom - rect.Top);
            var left = Math.Max(0, (screenWidth - width) / 2);
            var top = Math.Max(0, (screenHeight - height) / 2);
            MoveWindow(windowHandle, left, top, width, height, true);
        }
    }
}
