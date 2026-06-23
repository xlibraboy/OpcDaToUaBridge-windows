using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Principal;
using OpcBridge.Core;

namespace OpcBridge.Da;

public sealed class OpcDaClient : IDaClient
{
    private const int OpcDataSourceDevice = 2;
    private static readonly int ItemStateSize = Marshal.SizeOf<OpcItemState>();
    private static readonly int ItemValueOffset = (int)Marshal.OffsetOf<OpcItemState>(nameof(OpcItemState.Value));

    private readonly DaClientOptions options_;
    private object? server_com_object_;
    private object? group_com_object_;
    private IOPCServer? server_;
    private IOPCItemMgt? item_management_;
    private IOPCSyncIO? sync_io_;
    private ItemBinding[] item_bindings_ = [];
    private int server_group_handle_;

    public OpcDaClient(DaClientOptions options)
    {
        options_ = options;
    }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (server_ is not null)
        {
            return Task.CompletedTask;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("OPC DA mode requires Windows because it uses COM/DCOM.");
        }

        string progId = options_.ProgId.Trim();
        if (progId.Length == 0)
        {
            throw new InvalidOperationException("Da:ProgId must be configured when Da:Mode is OpcDa.");
        }

        string? host = NormalizeHost(options_.Host);

        // Use impersonation for remote connections with explicit credentials
        bool hasCredentials = host is not null
            && !string.IsNullOrWhiteSpace(options_.RemoteUsername);

        if (hasCredentials)
        {
            ConnectWithImpersonation(progId, host!);
        }
        else
        {
            ConnectDirect(progId, host);
        }

        return Task.CompletedTask;
    }

    [SupportedOSPlatform("windows")]
    private void ConnectWithImpersonation(string progId, string host)
    {
        string username = options_.RemoteUsername!.Trim();
        string password = options_.RemotePassword ?? string.Empty;
        string domain   = string.IsNullOrWhiteSpace(options_.RemoteDomain)
            ? host  // use host as domain for local accounts on remote machine
            : options_.RemoteDomain.Trim();

        // LOGON32_LOGON_NEW_CREDENTIALS (9) — best for network/DCOM impersonation
        // LOGON32_PROVIDER_WINNT50 (3)
        if (!LogonUser(username, domain, password, 9, 3, out nint token))
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Logon failed for '{domain}\\{username}' (Win32 error {error}). " +
                "Check RemoteUsername, RemotePassword, RemoteDomain.");
        }

        try
        {
            using var identity = new WindowsIdentity(token);
            WindowsIdentity.RunImpersonated(identity.AccessToken, () => ConnectDirect(progId, host));
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [SupportedOSPlatform("windows")]
    private void ConnectDirect(string progId, string? host)
    {
        Type? serverType = host is null
            ? Type.GetTypeFromProgID(progId, throwOnError: false)
            : Type.GetTypeFromProgID(progId, host, throwOnError: false);

        if (serverType is null)
        {
            throw new InvalidOperationException(host is null
                ? $"OPC DA server ProgID '{progId}' is not registered on this machine."
                : $"OPC DA server ProgID '{progId}' is not available on host '{host}'.");
        }

        object serverObject = Activator.CreateInstance(serverType)
            ?? throw new InvalidOperationException($"Failed to create OPC DA server '{progId}'.");
        IOPCServer server = serverObject as IOPCServer
            ?? throw new InvalidOperationException($"COM server '{progId}' does not expose IOPCServer.");

        Guid itemManagementGuid = typeof(IOPCItemMgt).GUID;
        int addGroupHresult = server.AddGroup(
            "OpcBridge",
            1,
            Math.Max(100, options_.UpdateRateMs),
            1,
            IntPtr.Zero,
            IntPtr.Zero,
            0,
            out int serverGroupHandle,
            out _,
            ref itemManagementGuid,
            out object groupObject);
        ThrowOnFailed(addGroupHresult, "Failed to create OPC DA group.");

        IOPCItemMgt itemManagement = groupObject as IOPCItemMgt
            ?? throw new InvalidOperationException("OPC DA group does not expose IOPCItemMgt.");
        IOPCSyncIO syncIo = groupObject as IOPCSyncIO
            ?? throw new InvalidOperationException("OPC DA group does not expose IOPCSyncIO.");

        server_com_object_ = serverObject;
        group_com_object_ = groupObject;
        server_ = server;
        item_management_ = itemManagement;
        sync_io_ = syncIo;
        server_group_handle_ = serverGroupHandle;
        item_bindings_ = [];
    }

    public Task<IReadOnlyList<BridgeValue>> ReadAsync(
        IReadOnlyList<TagMapping> mappings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (mappings.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<BridgeValue>>(Array.Empty<BridgeValue>());
        }

        EnsureConnected();
        EnsureItemsConfigured(mappings);

        IOPCSyncIO syncIo = sync_io_!;
        int[] serverHandles = new int[item_bindings_.Length];
        for (int i = 0; i < item_bindings_.Length; i++)
        {
            serverHandles[i] = item_bindings_[i].ServerHandle;
        }

        IntPtr itemStatesPointer = IntPtr.Zero;
        IntPtr errorsPointer = IntPtr.Zero;

        try
        {
            int readHresult = syncIo.Read(
                OpcDataSourceDevice,
                serverHandles.Length,
                serverHandles,
                out itemStatesPointer,
                out errorsPointer);
            ThrowOnFailed(readHresult, "OPC DA read failed.");

            int[] itemErrors = new int[item_bindings_.Length];
            Marshal.Copy(errorsPointer, itemErrors, 0, itemErrors.Length);

            BridgeValue[] values = new BridgeValue[item_bindings_.Length];
            for (int i = 0; i < item_bindings_.Length; i++)
            {
                IntPtr itemStatePointer = IntPtr.Add(itemStatesPointer, i * ItemStateSize);
                OpcItemState itemState = Marshal.PtrToStructure<OpcItemState>(itemStatePointer);

                try
                {
                    ThrowOnFailed(itemErrors[i], $"OPC DA item read failed for '{item_bindings_[i].DaItemId}'.");

                    int quality = (ushort)itemState.Quality;
                    values[i] = new BridgeValue(
                        options_.SourceId,
                        item_bindings_[i].DaItemId,
                        itemState.Value,
                        FileTimeToUtc(itemState.Timestamp),
                        quality,
                        QualityMapper.IsGoodDaQuality(quality));
                }
                finally
                {
                    VariantClear(IntPtr.Add(itemStatePointer, ItemValueOffset));
                }
            }

            return Task.FromResult<IReadOnlyList<BridgeValue>>(values);
        }
        finally
        {
            if (errorsPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(errorsPointer);
            }

            if (itemStatesPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(itemStatesPointer);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            RemoveConfiguredItems();

            if (server_ is not null && server_group_handle_ != 0)
            {
                server_.RemoveGroup(server_group_handle_, 0);
            }
        }
        finally
        {
            item_bindings_ = [];
            server_group_handle_ = 0;
            sync_io_ = null;
            item_management_ = null;
            server_ = null;

            if (OperatingSystem.IsWindows())
            {
                ReleaseComObject(ref group_com_object_);
                ReleaseComObject(ref server_com_object_);
            }
            else
            {
                group_com_object_ = null;
                server_com_object_ = null;
            }
        }

        return ValueTask.CompletedTask;
    }

    private void EnsureConnected()
    {
        if (server_ is null || item_management_ is null || sync_io_ is null)
        {
            throw new InvalidOperationException("OPC DA client is not connected.");
        }
    }

    private void EnsureItemsConfigured(IReadOnlyList<TagMapping> mappings)
    {
        if (item_bindings_.Length != 0)
        {
            if (item_bindings_.Length != mappings.Count)
            {
                throw new InvalidOperationException("OPC DA mappings changed after the client was connected.");
            }

            for (int i = 0; i < item_bindings_.Length; i++)
            {
                if (!string.Equals(item_bindings_[i].DaItemId, mappings[i].DaItemId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("OPC DA mappings changed after the client was connected.");
                }
            }

            return;
        }

        IOPCItemMgt itemManagement = item_management_!;
        OpcItemDefinition[] definitions = new OpcItemDefinition[mappings.Count];
        for (int i = 0; i < mappings.Count; i++)
        {
            definitions[i] = new OpcItemDefinition
            {
                AccessPath = string.Empty,
                ItemId = mappings[i].DaItemId,
                IsActive = 1,
                ClientHandle = i + 1,
                RequestedDataType = (short)MapVarType(mappings[i].DataType)
            };
        }

        IntPtr resultsPointer = IntPtr.Zero;
        IntPtr errorsPointer = IntPtr.Zero;
        List<int>? cleanupHandles = null;

        try
        {
            int addItemsHresult = itemManagement.AddItems(
                definitions.Length,
                definitions,
                out resultsPointer,
                out errorsPointer);
            ThrowOnFailed(addItemsHresult, "Failed to add OPC DA items.");

            int[] itemErrors = new int[definitions.Length];
            Marshal.Copy(errorsPointer, itemErrors, 0, itemErrors.Length);

            ItemBinding[] bindings = new ItemBinding[definitions.Length];
            cleanupHandles = new List<int>(definitions.Length);
            int resultSize = Marshal.SizeOf<OpcItemResult>();

            for (int i = 0; i < definitions.Length; i++)
            {
                IntPtr resultPointer = IntPtr.Add(resultsPointer, i * resultSize);
                OpcItemResult result = Marshal.PtrToStructure<OpcItemResult>(resultPointer);

                if (result.BlobPointer != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(result.BlobPointer);
                }

                ThrowOnFailed(itemErrors[i], $"Failed to add OPC DA item '{mappings[i].DaItemId}'.");

                bindings[i] = new ItemBinding(mappings[i].DaItemId, result.ServerHandle);
                cleanupHandles.Add(result.ServerHandle);
            }

            item_bindings_ = bindings;
        }
        catch
        {
            if (cleanupHandles is { Count: > 0 })
            {
                RemoveItems(cleanupHandles.ToArray());
            }

            throw;
        }
        finally
        {
            if (errorsPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(errorsPointer);
            }

            if (resultsPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(resultsPointer);
            }
        }
    }

    private void RemoveConfiguredItems()
    {
        if (item_bindings_.Length == 0)
        {
            return;
        }

        int[] serverHandles = new int[item_bindings_.Length];
        for (int i = 0; i < item_bindings_.Length; i++)
        {
            serverHandles[i] = item_bindings_[i].ServerHandle;
        }

        RemoveItems(serverHandles);
        item_bindings_ = [];
    }

    private void RemoveItems(int[] serverHandles)
    {
        if (item_management_ is null || serverHandles.Length == 0)
        {
            return;
        }

        IntPtr errorsPointer = IntPtr.Zero;
        try
        {
            item_management_.RemoveItems(serverHandles.Length, serverHandles, out errorsPointer);
        }
        finally
        {
            if (errorsPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(errorsPointer);
            }
        }
    }

    private static string? NormalizeHost(string host)
    {
        string trimmed = host.Trim();
        if (trimmed.Length == 0 ||
            string.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, ".", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }

    private static VarEnum MapVarType(string dataType)
    {
        return dataType.Trim().ToUpperInvariant() switch
        {
            "BOOL" or "BOOLEAN" => VarEnum.VT_BOOL,
            "BYTE" => VarEnum.VT_UI1,
            "SBYTE" => VarEnum.VT_I1,
            "INT16" or "SHORT" => VarEnum.VT_I2,
            "UINT16" => VarEnum.VT_UI2,
            "INT32" or "INT" => VarEnum.VT_I4,
            "UINT32" => VarEnum.VT_UI4,
            "INT64" or "LONG" => VarEnum.VT_I8,
            "UINT64" => VarEnum.VT_UI8,
            "FLOAT" or "SINGLE" => VarEnum.VT_R4,
            "DOUBLE" or "REAL8" => VarEnum.VT_R8,
            "STRING" => VarEnum.VT_BSTR,
            _ => VarEnum.VT_EMPTY
        };
    }

    private static DateTime FileTimeToUtc(FILETIME value)
    {
        long fileTime = ((long)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime;
        return fileTime <= 0 ? DateTime.UtcNow : DateTime.FromFileTimeUtc(fileTime);
    }

    private static void ThrowOnFailed(int hresult, string message)
    {
        if (hresult < 0)
        {
            throw new COMException(message, hresult);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ReleaseComObject(ref object? comObject)
    {
        if (comObject is null)
        {
            return;
        }

        Marshal.FinalReleaseComObject(comObject);
        comObject = null;
    }

    [DllImport("oleaut32.dll")]
    private static extern int VariantClear(IntPtr pvarg);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LogonUser(string username, string domain, string password,
        int logonType, int logonProvider, out nint token);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    private sealed record ItemBinding(string DaItemId, int ServerHandle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpcItemDefinition
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string AccessPath;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string ItemId;

        public int IsActive;
        public int ClientHandle;
        public int BlobSize;
        public IntPtr BlobPointer;
        public short RequestedDataType;
        public short Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpcItemResult
    {
        public int ServerHandle;
        public short CanonicalDataType;
        public short Reserved;
        public int AccessRights;
        public int BlobSize;
        public IntPtr BlobPointer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpcItemState
    {
        public int ClientHandle;
        public FILETIME Timestamp;
        public short Quality;
        public short Reserved;

        [MarshalAs(UnmanagedType.Struct)]
        public object? Value;
    }

    [ComImport]
    [Guid("39C13A4D-011E-11D0-9675-0020AFD8ADB3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOPCServer
    {
        int AddGroup(
            [MarshalAs(UnmanagedType.LPWStr)] string name,
            int active,
            int requestedUpdateRate,
            int clientGroupHandle,
            IntPtr timeBias,
            IntPtr percentDeadband,
            int lcid,
            out int serverGroupHandle,
            out int revisedUpdateRate,
            ref Guid requestedInterface,
            [MarshalAs(UnmanagedType.IUnknown)] out object groupInterface);

        int GetErrorString(int error, int locale, [MarshalAs(UnmanagedType.LPWStr)] out string errorString);

        int GetGroupByName(
            [MarshalAs(UnmanagedType.LPWStr)] string name,
            ref Guid requestedInterface,
            [MarshalAs(UnmanagedType.IUnknown)] out object groupInterface);

        int GetStatus(out IntPtr serverStatus);

        int RemoveGroup(int serverGroupHandle, int force);

        int CreateGroupEnumerator(
            int scope,
            ref Guid requestedInterface,
            [MarshalAs(UnmanagedType.IUnknown)] out object enumerator);
    }

    [ComImport]
    [Guid("39C13A54-011E-11D0-9675-0020AFD8ADB3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOPCItemMgt
    {
        int AddItems(int count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] OpcItemDefinition[] itemDefinitions, out IntPtr results, out IntPtr errors);

        int ValidateItems(int count, IntPtr itemDefinitions, int blobUpdate, out IntPtr validationResults, out IntPtr errors);

        int RemoveItems(int count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] int[] serverHandles, out IntPtr errors);

        int SetActiveState(int count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] int[] serverHandles, int active, out IntPtr errors);

        int SetClientHandles(int count, IntPtr serverHandles, IntPtr clientHandles, out IntPtr errors);

        int SetDatatypes(int count, IntPtr serverHandles, IntPtr requestedDatatypes, out IntPtr errors);

        int CreateEnumerator(ref Guid requestedInterface, [MarshalAs(UnmanagedType.IUnknown)] out object enumerator);
    }

    [ComImport]
    [Guid("39C13A52-011E-11D0-9675-0020AFD8ADB3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOPCSyncIO
    {
        int Read(int dataSource, int count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] int[] serverHandles, out IntPtr itemValues, out IntPtr errors);

        int Write(int count, IntPtr serverHandles, IntPtr values, out IntPtr errors);
    }
}
