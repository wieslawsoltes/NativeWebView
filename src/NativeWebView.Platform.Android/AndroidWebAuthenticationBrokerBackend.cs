#if NATIVEWEBVIEW_ANDROID_RUNTIME
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using Java.Interop;
using Object = Java.Lang.Object;
#endif
using NativeWebView.Core;

namespace NativeWebView.Platform.Android;

public sealed class AndroidWebAuthenticationBrokerBackend : IWebAuthenticationBrokerBackend
{
    public AndroidWebAuthenticationBrokerBackend()
    {
        Platform = NativeWebViewPlatform.Android;
        Features = AndroidPlatformFeatures.Instance;
    }

    public NativeWebViewPlatform Platform { get; }

    public IWebViewPlatformFeatures Features { get; }

    public Task<WebAuthenticationResult> AuthenticateAsync(
        Uri requestUri,
        Uri callbackUri,
        WebAuthenticationOptions options = WebAuthenticationOptions.None,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(callbackUri);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Features.Supports(NativeWebViewFeature.AuthenticationBroker))
        {
            return Task.FromResult(WebAuthenticationResult.Error(WebAuthenticationBrokerBackendSupport.NotImplementedError));
        }

        if (WebAuthenticationBrokerBackendSupport.TryCreateImmediateSuccess(requestUri, callbackUri, out var immediateResult))
        {
            return Task.FromResult(immediateResult);
        }

        if ((options & WebAuthenticationOptions.SilentMode) != 0)
        {
            return Task.FromResult(WebAuthenticationResult.UserCancel());
        }

        if ((options & WebAuthenticationOptions.UseHttpPost) != 0)
        {
            return Task.FromResult(WebAuthenticationBrokerBackendSupport.UnsupportedHttpPost());
        }

#if NATIVEWEBVIEW_ANDROID_RUNTIME
        if (OperatingSystem.IsAndroid())
        {
            return AuthenticateRuntimeAsync(requestUri, callbackUri, options, cancellationToken);
        }
#endif

        return Task.FromResult(WebAuthenticationBrokerBackendSupport.RuntimeUnavailable());
    }

    public void Dispose()
    {
    }

#if NATIVEWEBVIEW_ANDROID_RUNTIME
    private static Task<WebAuthenticationResult> AuthenticateRuntimeAsync(
        Uri requestUri,
        Uri callbackUri,
        WebAuthenticationOptions options,
        CancellationToken cancellationToken)
    {
        return AndroidAuthenticationBrokerCoordinator.StartAsync(requestUri, callbackUri, options, cancellationToken);
    }
#endif
}

#if NATIVEWEBVIEW_ANDROID_RUNTIME
[Activity(Exported = false, NoHistory = true)]
public sealed class AndroidAuthenticationBrokerActivity : Activity
{
    private const string SessionIdExtra = "NativeWebView.Auth.SessionId";

    private AndroidAuthenticationSessionState? _session;
    private WebView? _webView;
    private AuthNavigationClient? _navigationClient;
    private AuthChromeClient? _chromeClient;

    public static Intent CreateIntent(Context context, string sessionId)
    {
        var intent = new Intent(context, typeof(AndroidAuthenticationBrokerActivity));
        intent.AddFlags(ActivityFlags.NewTask);
        intent.PutExtra(SessionIdExtra, sessionId);
        return intent;
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var sessionId = Intent?.GetStringExtra(SessionIdExtra);
        if (string.IsNullOrWhiteSpace(sessionId) ||
            !AndroidAuthenticationBrokerCoordinator.TryGetSession(sessionId, out _session))
        {
            Finish();
            return;
        }

        var session = _session
            ?? throw new InvalidOperationException("Android authentication session is unavailable.");

        session.Attach(this);

        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
        };
        root.SetBackgroundColor(Color.White);

        var header = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
        };
        header.SetPadding(24, 24, 24, 24);
        header.SetBackgroundColor(Color.Rgb(245, 245, 245));

        var titleView = new TextView(this)
        {
            Text = WebAuthenticationBrokerBackendSupport.CreateInteractiveTitle(session.RequestUri, session.Options),
            TextSize = 18,
        };

        var titleLayout = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
        header.AddView(titleView, titleLayout);

        var cancelButton = new Button(this)
        {
            Text = "Cancel",
        };
        cancelButton.Click += (_, _) => Complete(WebAuthenticationResult.UserCancel());
        header.AddView(cancelButton, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));

        _webView = new WebView(this);
        _webView.LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            0,
            1f);

        var settings = _webView.Settings;
        if (settings is not null)
        {
            settings.JavaScriptEnabled = true;
            settings.DomStorageEnabled = true;
            settings.JavaScriptCanOpenWindowsAutomatically = true;
            settings.SetSupportMultipleWindows(true);
        }

        _navigationClient = new AuthNavigationClient(this);
        _chromeClient = new AuthChromeClient(this);
        _webView.SetWebViewClient(_navigationClient);
        _webView.SetWebChromeClient(_chromeClient);

        root.AddView(header, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        root.AddView(_webView);

        SetContentView(root);
        _webView.LoadUrl(session.RequestUri.AbsoluteUri);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (_webView is not null)
        {
            _webView.StopLoading();
            _webView.SetWebViewClient(null);
            _webView.SetWebChromeClient(null);
            _webView.Destroy();
            _webView.Dispose();
            _webView = null;
        }

        _navigationClient = null;
        _chromeClient = null;

        if (_session is not null)
        {
            _session.Detach(this);
            _session.TryComplete(WebAuthenticationResult.UserCancel());
            _session = null;
        }
    }

#pragma warning disable CS0672
    public override void OnBackPressed()
#pragma warning restore CS0672
    {
        Complete(WebAuthenticationResult.UserCancel());
    }

    internal bool HandleNavigation(string? url, bool loadInCurrentView)
    {
        if (_session is null || string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var navigationUri) &&
            WebAuthenticationBrokerBackendSupport.IsCallbackUri(navigationUri, _session.CallbackUri))
        {
            Complete(WebAuthenticationResult.Success(
                WebAuthenticationBrokerBackendSupport.ToResponseData(navigationUri)));
            return true;
        }

        if (loadInCurrentView && _webView is not null)
        {
            _webView.LoadUrl(url);
        }

        return false;
    }

    private void Complete(WebAuthenticationResult result)
    {
        _session?.TryComplete(result);
        Finish();
    }

    private sealed class AuthNavigationClient : WebViewClient
    {
        private readonly WeakReference<AndroidAuthenticationBrokerActivity> _owner;

        public AuthNavigationClient(AndroidAuthenticationBrokerActivity owner)
        {
            _owner = new WeakReference<AndroidAuthenticationBrokerActivity>(owner);
        }

        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
        {
            if (!_owner.TryGetTarget(out var owner) || request is null || !request.IsForMainFrame)
            {
                return false;
            }

            return owner.HandleNavigation(request.Url?.ToString(), loadInCurrentView: false);
        }

        public override bool ShouldOverrideUrlLoading(WebView? view, string? url)
        {
            if (!_owner.TryGetTarget(out var owner))
            {
                return false;
            }

            return owner.HandleNavigation(url, loadInCurrentView: false);
        }

        public override void OnPageStarted(WebView? view, string? url, Bitmap? favicon)
        {
            base.OnPageStarted(view, url, favicon);

            if (_owner.TryGetTarget(out var owner))
            {
                owner.HandleNavigation(url, loadInCurrentView: false);
            }
        }
    }

    private sealed class AuthChromeClient : WebChromeClient
    {
        private readonly WeakReference<AndroidAuthenticationBrokerActivity> _owner;

        public AuthChromeClient(AndroidAuthenticationBrokerActivity owner)
        {
            _owner = new WeakReference<AndroidAuthenticationBrokerActivity>(owner);
        }

        public override bool OnCreateWindow(WebView? view, bool isDialog, bool isUserGesture, Message? resultMsg)
        {
            _ = isDialog;
            _ = isUserGesture;

            if (!_owner.TryGetTarget(out var owner) || view is null || resultMsg?.Obj is null)
            {
                return false;
            }

            var transport = Object.GetObject<WebView.WebViewTransport>(resultMsg.Obj.Handle, JniHandleOwnership.DoNotTransfer);
            if (transport is null)
            {
                return false;
            }

            var popupContext = view.Context;
            if (popupContext is null)
            {
                return false;
            }

            var popupWebView = new WebView(popupContext);
            popupWebView.SetWebViewClient(new PopupNavigationClient(owner, popupWebView));
            transport.WebView = popupWebView;
            resultMsg.SendToTarget();
            return true;
        }
    }

    private sealed class PopupNavigationClient : WebViewClient
    {
        private readonly WeakReference<AndroidAuthenticationBrokerActivity> _owner;
        private readonly WeakReference<WebView> _popupWebView;
        private bool _handled;

        public PopupNavigationClient(AndroidAuthenticationBrokerActivity owner, WebView popupWebView)
        {
            _owner = new WeakReference<AndroidAuthenticationBrokerActivity>(owner);
            _popupWebView = new WeakReference<WebView>(popupWebView);
        }

        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
        {
            if (request is null || !request.IsForMainFrame)
            {
                return false;
            }

            HandlePopupNavigation(request.Url?.ToString());
            return true;
        }

        public override bool ShouldOverrideUrlLoading(WebView? view, string? url)
        {
            HandlePopupNavigation(url);
            return true;
        }

        public override void OnPageStarted(WebView? view, string? url, Bitmap? favicon)
        {
            base.OnPageStarted(view, url, favicon);
            HandlePopupNavigation(url);
        }

        private void HandlePopupNavigation(string? url)
        {
            if (_handled)
            {
                return;
            }

            _handled = true;

            if (_owner.TryGetTarget(out var owner))
            {
                owner.HandleNavigation(url, loadInCurrentView: true);
            }

            if (_popupWebView.TryGetTarget(out var popupWebView))
            {
                popupWebView.StopLoading();
                popupWebView.SetWebViewClient(null);
                popupWebView.SetWebChromeClient(null);
                popupWebView.Destroy();
                popupWebView.Dispose();
            }
        }
    }
}

internal static class AndroidAuthenticationBrokerCoordinator
{
    private static readonly Dictionary<string, AndroidAuthenticationSessionState> Sessions = [];
    private static readonly object Gate = new();

    public static Task<WebAuthenticationResult> StartAsync(
        Uri requestUri,
        Uri callbackUri,
        WebAuthenticationOptions options,
        CancellationToken cancellationToken)
    {
        var session = new AndroidAuthenticationSessionState(
            Guid.NewGuid().ToString("N"),
            requestUri,
            callbackUri,
            options,
            cancellationToken,
            RemoveSession);

        lock (Gate)
        {
            Sessions[session.Id] = session;
        }

        var intent = AndroidAuthenticationBrokerActivity.CreateIntent(Application.Context, session.Id);

        try
        {
            Application.Context.StartActivity(intent);
            return session.Task;
        }
        catch
        {
            RemoveSession(session.Id);
            session.TryComplete(WebAuthenticationResult.UserCancel());
            return session.Task;
        }
    }

    public static bool TryGetSession(string id, out AndroidAuthenticationSessionState? session)
    {
        lock (Gate)
        {
            return Sessions.TryGetValue(id, out session);
        }
    }

    private static void RemoveSession(string id)
    {
        lock (Gate)
        {
            Sessions.Remove(id);
        }
    }
}

internal sealed class AndroidAuthenticationSessionState
{
    private readonly CancellationToken _cancellationToken;
    private readonly Action<string> _removeSession;
    private readonly TaskCompletionSource<WebAuthenticationResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CancellationTokenRegistration _cancellationRegistration;
    private WeakReference<Activity>? _activity;
    private int _completionState;

    public AndroidAuthenticationSessionState(
        string id,
        Uri requestUri,
        Uri callbackUri,
        WebAuthenticationOptions options,
        CancellationToken cancellationToken,
        Action<string> removeSession)
    {
        Id = id;
        RequestUri = requestUri;
        CallbackUri = callbackUri;
        Options = options;
        _cancellationToken = cancellationToken;
        _removeSession = removeSession;

        _cancellationRegistration = cancellationToken.Register(static state =>
        {
            ((AndroidAuthenticationSessionState)state!).Cancel();
        }, this);
    }

    public string Id { get; }

    public Uri RequestUri { get; }

    public Uri CallbackUri { get; }

    public WebAuthenticationOptions Options { get; }

    public Task<WebAuthenticationResult> Task => _completion.Task;

    public void Attach(Activity activity)
    {
        _activity = new WeakReference<Activity>(activity);
    }

    public void Detach(Activity activity)
    {
        if (_activity is not null &&
            _activity.TryGetTarget(out var current) &&
            ReferenceEquals(current, activity))
        {
            _activity = null;
        }
    }

    public void TryComplete(WebAuthenticationResult result)
    {
        if (Interlocked.CompareExchange(ref _completionState, 1, 0) != 0)
        {
            return;
        }

        _cancellationRegistration.Dispose();
        _removeSession(Id);
        _completion.TrySetResult(result);
    }

    private void Cancel()
    {
        if (Interlocked.CompareExchange(ref _completionState, 1, 0) != 0)
        {
            return;
        }

        _removeSession(Id);
        _completion.TrySetCanceled(_cancellationToken);

        if (_activity is not null && _activity.TryGetTarget(out var activity))
        {
            activity.RunOnUiThread(activity.Finish);
        }
    }
}
#endif
