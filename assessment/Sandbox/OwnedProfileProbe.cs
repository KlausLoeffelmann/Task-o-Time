using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;
using System.Security.AccessControl;

// Only the trusted diagnostic launcher stages this owned probe. It is not a replay producer.
public static class OwnedProfileProbe
{
    [StructLayout(LayoutKind.Sequential)] struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] struct Groups { public uint Count; public SidAndAttributes First; }
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenThread(uint access, bool inherit, uint thread);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll")] static extern bool IsTokenRestricted(IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr token, int kind, IntPtr info, int size, out int length);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool LookupPrivilegeName(string system, IntPtr luid, StringBuilder name, ref int size);

    static object Attempt(Action action)
    {
        try { action(); return new { Allowed = true }; }
        catch (Exception error) { return new { Allowed = false, ExceptionType = error.GetType().FullName, error.HResult }; }
    }
    static bool ProcessAccess(uint pid, uint access)
    {
        var handle = OpenProcess(access, false, pid);
        if (handle == IntPtr.Zero) return false;
        CloseHandle(handle);
        return true;
    }
    static object FileAccessInventory(string path)
    {
        var permissions = new Dictionary<string, object>();
        foreach (var access in new Dictionary<string, uint> { ["write-data-or-add-file"] = 2, ["delete"] = 0x10000,
            ["delete-child"] = 0x40, ["write-dacl"] = 0x40000, ["write-owner"] = 0x80000 })
        {
            var handle = CreateFile(path, access.Value, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
            bool allowed = handle != new IntPtr(-1);
            int error = allowed ? 0 : Marshal.GetLastWin32Error();
            if (allowed) CloseHandle(handle);
            permissions[access.Key] = new { Allowed = allowed, Win32Error = error };
        }
        return new { Path = path, Permissions = permissions, NoMutationPerformed = true,
            RenameAssessment = "Only source-delete/directory-add prerequisites measured; no rename performed." };
    }
    static object RegistryInventory()
    {
        var results = new Dictionary<string, object>();
        foreach (var path in new[] { @"Environment", @"Software\Classes", @"Software\Microsoft\.NETFramework",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders", @"Control Panel\International" })
        {
            var rights = new Dictionary<string, object>();
            foreach (var permission in new Dictionary<string, RegistryRights> { ["set-value"] = RegistryRights.SetValue,
                ["create-subkey"] = RegistryRights.CreateSubKey, ["write-dacl"] = RegistryRights.ChangePermissions,
                ["delete"] = RegistryRights.Delete, ["write-owner"] = RegistryRights.TakeOwnership })
            {
                rights[permission.Key] = Attempt(() =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(path, RegistryKeyPermissionCheck.Default, permission.Value);
                    if (key == null) throw new IOException("Requested registry key was not present in this token's view.");
                });
            }
            results[path] = rights;
        }
        return new { View = "Current token HKCU view; AppContainer virtualization may differ from controller view.",
            NoMutationPerformed = true, Access = results };
    }
    static object KernelAccessInventory(uint controller, string threads)
    {
        var process = new Dictionary<string, bool>();
        foreach (var access in new Dictionary<string, uint> { ["create-thread"] = 2, ["duplicate-handles"] = 0x40,
            ["suspend-resume"] = 0x800, ["write-dacl"] = 0x40000, ["write-owner"] = 0x80000, ["query-limited"] = 0x1000 })
            process[access.Key] = ProcessAccess(controller, access.Value);
        var tokens = new Dictionary<string, bool>();
        var handle = OpenProcess(0x1000, false, controller);
        try
        {
            foreach (var access in new Dictionary<string, uint> { ["query"] = 8, ["duplicate"] = 2,
                ["impersonate"] = 4, ["assign-primary"] = 1, ["adjust-privileges"] = 0x20, ["write-dacl"] = 0x40000 })
            {
                IntPtr token = IntPtr.Zero;
                tokens[access.Key] = handle != IntPtr.Zero && OpenProcessToken(handle, access.Value, out token);
                if (token != IntPtr.Zero) CloseHandle(token);
            }
        }
        finally { if (handle != IntPtr.Zero) CloseHandle(handle); }
        var threadAccess = new Dictionary<string, object>();
        foreach (var text in threads.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var rights = new Dictionary<string, bool>();
            foreach (var access in new Dictionary<string, uint> { ["terminate"] = 1, ["suspend"] = 2, ["get-context"] = 8,
                ["set-context"] = 0x10, ["set-token"] = 0x80, ["impersonate"] = 0x100,
                ["direct-impersonation"] = 0x200, ["write-dacl"] = 0x40000 })
            {
                var thread = OpenThread(access.Value, false, uint.Parse(text));
                rights[access.Key] = thread != IntPtr.Zero;
                if (thread != IntPtr.Zero) CloseHandle(thread);
            }
            threadAccess[text] = rights;
        }
        return new { Process = process, Token = tokens, Threads = threadAccess, NoMutationPerformed = true };
    }
    static object Mapping(string name)
    {
        try
        {
            using var mapping = MemoryMappedFile.CreateNew(name, 4096);
            using (var writer = mapping.CreateViewStream()) { writer.WriteByte(37); writer.Flush(); }
            using var reader = mapping.CreateViewStream(0, 4096, MemoryMappedFileAccess.Read);
            if (reader.ReadByte() != 37) throw new InvalidOperationException("Mapping bytes changed.");
            mapping.Dispose();
            reader.Position = 0;
            if (reader.ReadByte() != 37) throw new InvalidOperationException("Held view failed.");
            return new { Succeeded = true, SameProcessViews = true, HeldViewAfterMappingDisposed = true };
        }
        catch (Exception error) { return new { Succeeded = false, ExceptionType = error.GetType().FullName, error.HResult }; }
    }
    static T TokenInfo<T>(IntPtr token, int kind, Func<IntPtr, T> read)
    {
        GetTokenInformation(token, kind, IntPtr.Zero, 0, out int length);
        if (length <= 0 || length > 65536) throw new InvalidOperationException("Invalid token information length.");
        var memory = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetTokenInformation(token, kind, memory, length, out _)) throw new InvalidOperationException("Cannot inspect token.");
            return read(memory);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    static List<object> ReadGroups(IntPtr memory)
    {
        var result = new List<object>();
        int offset = Marshal.OffsetOf<Groups>(nameof(Groups.First)).ToInt32();
        for (int i = 0; i < Marshal.ReadInt32(memory); i++)
        {
            var item = Marshal.PtrToStructure<SidAndAttributes>(memory + offset + i * Marshal.SizeOf<SidAndAttributes>());
            result.Add(new { Sid = new SecurityIdentifier(item.Sid).Value, item.Attributes });
        }
        return result;
    }
    static object Token()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 8, out var token)) throw new InvalidOperationException("Cannot inspect current token.");
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new
            {
                Integrity = TokenInfo(token, 25, p => new SecurityIdentifier(Marshal.PtrToStructure<SidAndAttributes>(p).Sid).Value),
                Restricted = IsTokenRestricted(token),
                Administrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
                IsAppContainer = TokenInfo(token, 29, p => Marshal.ReadInt32(p) != 0),
                CapabilityCount = TokenInfo(token, 30, p => Marshal.ReadInt32(p)),
                RestrictedSids = TokenInfo(token, 11, ReadGroups),
                Groups = TokenInfo(token, 2, ReadGroups),
                EnabledPrivileges = TokenInfo(token, 3, p =>
                {
                    var names = new List<string>();
                    for (int i = 0; i < Marshal.ReadInt32(p); i++)
                    {
                        var entry = p + 4 + i * 12;
                        if ((Marshal.ReadInt32(entry, 8) & 2) == 0) continue;
                        var name = new StringBuilder(256); int length = name.Capacity;
                        if (!LookupPrivilegeName(null, entry, name, ref length)) throw new InvalidOperationException("Cannot inspect privilege.");
                        names.Add(name.ToString());
                    }
                    return names;
                })
            };
        }
        finally { CloseHandle(token); }
    }

    public static int Main(string[] args)
    {
        if (!IsProcessInJob(GetCurrentProcess(), IntPtr.Zero, out bool inJob)) throw new InvalidOperationException("Cannot inspect job.");
        if (args.Length == 2 && args[0] == "--child")
        {
            File.WriteAllText(args[1] + ".writing", JsonSerializer.Serialize(new { Pid = Environment.ProcessId, InJob = inJob }));
            File.Move(args[1] + ".writing", args[1]);
            Thread.Sleep(300_000);
            return 0;
        }
        if (args.Length < 2 || args.Length > 5) return 2;
        uint controller = uint.Parse(args[0]);
        var work = args[1]; Directory.CreateDirectory(work);
        var marker = Path.Combine(work, "child.json");
        var dotnet = args.Length >= 3 ? args[2] : @"C:\PublicSdk\dotnet.exe";
        var start = new ProcessStartInfo(dotnet) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(typeof(OwnedProfileProbe).Assembly.Location);
        start.ArgumentList.Add("--child"); start.ArgumentList.Add(marker);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Owned child did not start.");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!File.Exists(marker) && DateTime.UtcNow < deadline) Thread.Sleep(50);
        if (!File.Exists(marker)) throw new InvalidOperationException("Owned child did not signal.");
        using var childInfo = JsonDocument.Parse(File.ReadAllText(marker));
        var evidence = new
        {
            Scope = "owned-profile-capability-only-not-security-approval", FormalVerified = false,
            Runtime = RuntimeInformation.FrameworkDescription, Token = Token(), InJob = inJob,
            ChildPid = child.Id, ChildInJob = childInfo.RootElement.GetProperty("InJob").GetBoolean(),
            ControllerFileRead = Attempt(() => File.ReadAllText(@"C:\Controller\secret.txt")),
            ControllerFileWrite = Attempt(() => File.WriteAllText(@"C:\ProbeWork\controller-marker.txt", "invalid owned probe write")),
            ControllerProcessRead = ProcessAccess(controller, 0x10),
            ControllerProcessWrite = ProcessAccess(controller, 0x20 | 0x8),
            ControllerProcessTerminate = ProcessAccess(controller, 0x1),
            ReadonlyPayloadWrite = Attempt(() => File.WriteAllText(@"C:\ProbePayload\owned-profile-write.txt", "invalid owned probe write")),
            ReadonlySdkWrite = Attempt(() => File.WriteAllText(Path.Combine(Path.GetDirectoryName(dotnet), "owned-profile-write.txt"), "invalid owned probe write")),
            ReadonlyBinaryWrite = Attempt(() => File.WriteAllText(Path.Combine(Path.GetDirectoryName(typeof(OwnedProfileProbe).Assembly.Location), "owned-profile-write.txt"), "invalid owned probe write")),
            ReadonlyInputWrite = args.Length >= 4 ? Attempt(() => File.WriteAllText(Path.Combine(args[3], "owned-profile-write.txt"), "invalid owned probe write")) : null,
            EffectiveFilesystemAccess = Array.ConvertAll(new[] { @"C:\Controller", @"C:\Controller\owned", @"C:\Controller\tmp",
                @"C:\Controller\home", @"C:\Controller\profile", @"C:\Controller\packages",
                @"C:\Windows\System32\WindowsPowerShell\v1.0\Modules", Path.GetDirectoryName(dotnet),
                Path.GetDirectoryName(typeof(OwnedProfileProbe).Assembly.Location),
                args.Length >= 4 ? args[3] : Path.GetDirectoryName(typeof(OwnedProfileProbe).Assembly.Location), work }, FileAccessInventory),
            RegistryAccess = RegistryInventory(),
            ControllerKernelAccess = KernelAccessInventory(controller, args.Length == 5 ? args[4] : ""),
            ExportForgeryWrite = Attempt(() => File.WriteAllText(@"C:\ProbeOutput\probe.json", "{\"Success\":true,\"FormalVerified\":true,\"TransportNonce\":\"forged\"}")),
            OwnWorkWrite = Attempt(() => File.WriteAllText(Path.Combine(work, "owned.txt"), "owned")),
            NamedMapping = Mapping("Roslyn Shared File: Size=4096 Id=" + Guid.NewGuid().ToString("N")),
            UnnamedMapping = Mapping(null)
        };
        Console.WriteLine(JsonSerializer.Serialize(evidence));
        return 0;
    }
}
