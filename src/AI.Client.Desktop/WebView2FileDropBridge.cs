namespace AI.Client.Desktop;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Avalonia.Platform;

/// <summary>
/// WebView2 gives a page's File objects their paths only through
/// <c>chrome.webview.postMessageWithAdditionalObjects</c>: the host reads each as ICoreWebView2File.
/// Other engines are left alone; their drops carry file URIs the page already understands.
/// </summary>
internal sealed unsafe partial class WebView2FileDropBridge : IFileDropBridge
{
    /// <summary>The page's message type; chatComposer.js sends it with the dropped FileList.</summary>
    private const string DroppedFilesMessage = "dropped-files";
    private const int MaxFiles = 20;
    // Slots after IUnknown's three, as declared in WebView2.h.
    private const int AddWebMessageReceivedSlot = 34;
    private const int RemoveWebMessageReceivedSlot = 35;
    private const int TryGetWebMessageAsStringSlot = 5;
    private const int GetAdditionalObjectsSlot = 6;
    private const int GetCountSlot = 3;
    private const int GetValueAtIndexSlot = 4;
    private const int GetPathSlot = 3;
    private static readonly Guid MessageArgs2Interface = new("06fc7ab7-c90c-4297-9389-33ca01cf6d5e");
    private static readonly Guid FileInterface = new("f2c19559-6bc1-4583-a757-90021be9afec");

    private IntPtr _coreWebView;
    private void* _handler;
    private long _token;
    private Action<IReadOnlyList<string>>? _dropped;

    public void Attach(IPlatformHandle? webView, Action<IReadOnlyList<string>> dropped)
    {
        if (!OperatingSystem.IsWindows() || _coreWebView != IntPtr.Zero
            || webView is not IWindowsWebView2PlatformHandle { CoreWebView2: var core } || core == IntPtr.Zero) return;
        _dropped = dropped;
        var handler = ComInterfaceMarshaller<IWebMessageReceivedHandler>.ConvertToUnmanaged(new MessageHandler(this));
        long token;
        var result = Call(core, AddWebMessageReceivedSlot, (nint)handler, (nint)(&token));
        if (result < 0)
        {
            ComInterfaceMarshaller<IWebMessageReceivedHandler>.Free(handler);
            Trace.TraceWarning($"Could not watch dropped files in WebView2: 0x{result:X8}");
            return;
        }
        Marshal.AddRef(core);
        _coreWebView = core;
        _handler = handler;
        _token = token;
    }

    public void Detach()
    {
        if (_coreWebView == IntPtr.Zero) return;
        RemoveHandler(_coreWebView, _token);
        ComInterfaceMarshaller<IWebMessageReceivedHandler>.Free(_handler);
        Marshal.Release(_coreWebView);
        _coreWebView = IntPtr.Zero;
        _handler = null;
        _dropped = null;
    }

    private void OnMessage(IntPtr args)
    {
        if (_dropped is not { } dropped || !IsDroppedFilesMessage(args)) return;
        if (Marshal.QueryInterface(args, in MessageArgs2Interface, out var args2) < 0) return;
        try
        {
            dropped(ReadPaths(args2));
        }
        finally
        {
            Marshal.Release(args2);
        }
    }

    private static bool IsDroppedFilesMessage(IntPtr args)
    {
        char* text = null;
        if (Call(args, TryGetWebMessageAsStringSlot, (nint)(&text)) < 0 || text is null) return false;
        try
        {
            using var message = System.Text.Json.JsonDocument.Parse(new string(text));
            return message.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && message.RootElement.TryGetProperty("type", out var type)
                && type.ValueKind == System.Text.Json.JsonValueKind.String
                && type.GetString() == DroppedFilesMessage;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeCoTaskMem((IntPtr)text);
        }
    }

    private static string[] ReadPaths(IntPtr args2)
    {
        IntPtr objects;
        if (Call(args2, GetAdditionalObjectsSlot, (nint)(&objects)) < 0 || objects == IntPtr.Zero) return [];
        try
        {
            uint count;
            if (Call(objects, GetCountSlot, (nint)(&count)) < 0) return [];
            var paths = new List<string>();
            for (uint index = 0; index < count && paths.Count < MaxFiles; index++)
            {
                IntPtr item;
                if (CallAt(objects, GetValueAtIndexSlot, index, (nint)(&item)) < 0 || item == IntPtr.Zero) continue;
                try
                {
                    if (ReadPath(item) is { Length: > 0 } path) paths.Add(path);
                }
                finally
                {
                    Marshal.Release(item);
                }
            }
            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally
        {
            Marshal.Release(objects);
        }
    }

    /// <summary>A dropped directory or anything else that is not a file has no ICoreWebView2File.</summary>
    private static string? ReadPath(IntPtr item)
    {
        if (Marshal.QueryInterface(item, in FileInterface, out var file) < 0) return null;
        try
        {
            char* path = null;
            if (Call(file, GetPathSlot, (nint)(&path)) < 0 || path is null) return null;
            try
            {
                return new string(path);
            }
            finally
            {
                Marshal.FreeCoTaskMem((IntPtr)path);
            }
        }
        finally
        {
            Marshal.Release(file);
        }
    }

    private static int Call(IntPtr self, int slot, nint argument) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, nint, int>)Slot(self, slot))(self, argument);

    private static int Call(IntPtr self, int slot, nint first, nint second) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, nint, nint, int>)Slot(self, slot))(self, first, second);

    private static int CallAt(IntPtr self, int slot, uint index, nint result) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, nint, int>)Slot(self, slot))(self, index, result);

    /// <summary>EventRegistrationToken is a struct holding one INT64, passed by value.</summary>
    private static void RemoveHandler(IntPtr core, long token) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Slot(core, RemoveWebMessageReceivedSlot))(core, token);

    private static void* Slot(IntPtr self, int slot) => (*(void***)self)[slot];

    [GeneratedComInterface]
    [Guid("57213f19-00e6-49fa-8e07-898ea01ecbd2")]
    internal partial interface IWebMessageReceivedHandler
    {
        void Invoke(IntPtr sender, IntPtr args);
    }

    [GeneratedComClass]
    internal sealed partial class MessageHandler(WebView2FileDropBridge owner) : IWebMessageReceivedHandler
    {
        public void Invoke(IntPtr sender, IntPtr args)
        {
            try
            {
                owner.OnMessage(args);
            }
            catch (Exception error)
            {
                // The engine only sees an HRESULT; a failed drop must not surface as a broken event.
                Trace.TraceWarning($"Could not read dropped files from WebView2: {error.Message}");
            }
        }
    }
}
