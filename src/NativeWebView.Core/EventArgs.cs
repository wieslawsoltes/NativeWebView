namespace NativeWebView.Core;

public sealed class CoreWebViewInitializedEventArgs : EventArgs
{
    public CoreWebViewInitializedEventArgs(bool isSuccess, Exception? initializationException = null, object? nativeObject = null)
    {
        IsSuccess = isSuccess;
        InitializationException = initializationException;
        NativeObject = nativeObject;
    }

    public bool IsSuccess { get; }

    public Exception? InitializationException { get; }

    public object? NativeObject { get; }
}

public sealed class NativeWebViewNavigationStartedEventArgs : EventArgs
{
    public NativeWebViewNavigationStartedEventArgs(Uri? uri, bool isRedirected)
    {
        Uri = uri;
        IsRedirected = isRedirected;
    }

    public Uri? Uri { get; }

    public bool IsRedirected { get; }

    public bool Cancel { get; set; }
}

public sealed class NativeWebViewNavigationCompletedEventArgs : EventArgs
{
    public NativeWebViewNavigationCompletedEventArgs(Uri? uri, bool isSuccess, int? httpStatusCode = null, string? error = null)
    {
        Uri = uri;
        IsSuccess = isSuccess;
        HttpStatusCode = httpStatusCode;
        Error = error;
    }

    public Uri? Uri { get; }

    public bool IsSuccess { get; }

    public int? HttpStatusCode { get; }

    public string? Error { get; }
}

public sealed class NativeWebViewMessageReceivedEventArgs : EventArgs
{
    public NativeWebViewMessageReceivedEventArgs(string? message, string? json)
    {
        Message = message;
        Json = json;
    }

    public string? Message { get; }

    public string? Json { get; }
}

public sealed class NativeWebViewOpenDevToolsRequestedEventArgs : EventArgs
{
}

public sealed class NativeWebViewDestroyRequestedEventArgs : EventArgs
{
    public NativeWebViewDestroyRequestedEventArgs(string? reason = null)
    {
        Reason = reason;
    }

    public string? Reason { get; }
}

public sealed class NativeWebViewRequestCustomChromeEventArgs : EventArgs
{
    public bool UseCustomChrome { get; set; }
}

public sealed class NativeWebViewRequestParentWindowPositionEventArgs : EventArgs
{
    public int Left { get; set; }

    public int Top { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}

public sealed class NativeWebViewBeginMoveDragEventArgs : EventArgs
{
}

public sealed class NativeWebViewBeginResizeDragEventArgs : EventArgs
{
    public NativeWebViewBeginResizeDragEventArgs(NativeWindowResizeEdge edge)
    {
        Edge = edge;
    }

    public NativeWindowResizeEdge Edge { get; }
}

public sealed class NativeWebViewNewWindowRequestedEventArgs : EventArgs
{
    public NativeWebViewNewWindowRequestedEventArgs(Uri? uri)
    {
        Uri = uri;
    }

    public Uri? Uri { get; }

    public bool Handled { get; set; }
}

public sealed class NativeWebViewResourceRequestedEventArgs : EventArgs
{
    public NativeWebViewResourceRequestedEventArgs(Uri? uri, string method, IReadOnlyDictionary<string, string>? headers = null)
    {
        Uri = uri;
        Method = method;
        Headers = headers ?? EmptyReadOnlyDictionary.Instance;
    }

    public Uri? Uri { get; }

    public string Method { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public bool Handled { get; set; }

    public int StatusCode { get; set; } = 200;

    public string? ContentType { get; set; }

    public string? ResponseBody { get; set; }
}

public sealed class NativeWebViewContextMenuRequestedEventArgs : EventArgs
{
    public NativeWebViewContextMenuRequestedEventArgs(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }

    public bool Handled { get; set; }
}

public sealed class NativeWebViewNavigationHistoryChangedEventArgs : EventArgs
{
    public NativeWebViewNavigationHistoryChangedEventArgs(bool canGoBack, bool canGoForward)
    {
        CanGoBack = canGoBack;
        CanGoForward = canGoForward;
    }

    public bool CanGoBack { get; }

    public bool CanGoForward { get; }
}

public sealed class NativeWebViewRenderFrameCapturedEventArgs : EventArgs
{
    public NativeWebViewRenderFrameCapturedEventArgs(NativeWebViewRenderFrame frame)
    {
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
    }

    public NativeWebViewRenderFrame Frame { get; }
}

public sealed class CoreWebViewEnvironmentRequestedEventArgs : EventArgs
{
    public CoreWebViewEnvironmentRequestedEventArgs(NativeWebViewEnvironmentOptions options)
    {
        Options = options;
    }

    public NativeWebViewEnvironmentOptions Options { get; }
}

public sealed class CoreWebViewControllerOptionsRequestedEventArgs : EventArgs
{
    public CoreWebViewControllerOptionsRequestedEventArgs(NativeWebViewControllerOptions options)
    {
        Options = options;
    }

    public NativeWebViewControllerOptions Options { get; }
}
