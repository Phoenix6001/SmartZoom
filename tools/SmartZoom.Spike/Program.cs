// Spike: can we do Safari-style smart zoom in a browser WITHOUT an extension?
//   1. Accessibility hit-test under the cursor -> element bounds -> pick a "block".
//      Chromium builds its accessibility tree lazily, and only exposes MSAA/IAccessible2 by default,
//      so we first poke the render widget (AccessibleObjectFromWindow) and hit-test through IAccessible.
//      UIA is tried as well for comparison.
//   2. Inject a synthetic two-finger pinch centered on the block -> browser visual-viewport zoom.
//   3. Inject the reverse pinch -> restore.
//
// Usage: SmartZoom.Spike [--wait <seconds>] [--auto <url>] [--scale <factor>] [--no-pinch]

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using IAccessible = Accessibility.IAccessible;
using Interop.UIAutomationClient;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Input.Pointer;
using Windows.Win32.UI.WindowsAndMessaging;

var wait = 8;
double? forcedScale = null;
var pinch = true;
string? autoUrl = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--wait": wait = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--scale": forcedScale = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--no-pinch": pinch = false; break;
        case "--auto": autoUrl = args[++i]; break;
    }
}

// Physical pixels everywhere: accessibility rects, cursor, touch injection.
PInvoke.SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

if (autoUrl is not null)
{
    // Self-driving mode: open the page in the default browser and park the pointer inside the article text.
    using (Process.Start(new ProcessStartInfo(autoUrl) { UseShellExecute = true })) { }
    Thread.Sleep(8000);
    var browser = Process.GetProcessesByName("brave").Concat(Process.GetProcessesByName("chrome")).Concat(Process.GetProcessesByName("msedge"))
        .FirstOrDefault(p => p.MainWindowHandle != 0 && !string.IsNullOrEmpty(p.MainWindowTitle))
        ?? throw new InvalidOperationException("No browser window found.");
    Console.WriteLine($"Browser window: {browser.ProcessName} \"{browser.MainWindowTitle}\"");
    var fg = new HWND(browser.MainWindowHandle);
    PInvoke.SetForegroundWindow(fg);
    Thread.Sleep(300);
    PInvoke.GetWindowRect(fg, out var fgRect);
    var px = fgRect.left + (int)((fgRect.right - fgRect.left) * 0.55);
    var py = fgRect.top + (int)((fgRect.bottom - fgRect.top) * 0.50);
    PInvoke.SetCursorPos(px, py);
    Thread.Sleep(500);
    wait = 0;
}

for (var s = wait; s > 0; s--)
{
    Console.Write($"\rHover over a paragraph in the browser... {s}  ");
    Thread.Sleep(1000);
}
Console.WriteLine();

PInvoke.GetCursorPos(out var cursor);
Console.WriteLine($"Cursor: ({cursor.X}, {cursor.Y})");

var hwnd = PInvoke.WindowFromPoint(cursor);
var root = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_ROOTOWNER);
_ = PInvoke.GetWindowThreadProcessId(root, out var pid);
Console.WriteLine($"Window: pid {pid} {Process.GetProcessById((int)pid).ProcessName}, hit class '{ClassName(hwnd)}'");

// ---------- 1a. Wake up Chromium accessibility and hit-test via MSAA ----------
var render = FindDescendant(root, "Chrome_RenderWidgetHostHWND");
if (render.IsNull)
{
    Console.WriteLine("No Chrome_RenderWidgetHostHWND child: not a Chromium browser. Stop.");
    return 1;
}

var sw = Stopwatch.StartNew();
var iidAccessible = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
var hr = AccessibleObjectFromWindow(render, 0xFFFFFFFC /* OBJID_CLIENT */, ref iidAccessible, out var accRoot);
Console.WriteLine($"AccessibleObjectFromWindow: hr=0x{hr:X8} in {sw.ElapsedMilliseconds} ms");
if (hr != 0 || accRoot is null)
    return 1;

// The first WM_GETOBJECT only switches Chromium into its basic mode (a native root with an empty
// web-contents placeholder). Screen readers get the full tree by walking the root's children and asking
// for IAccessible2, so do the same, then give the renderer time to ship the tree.
Console.WriteLine($"Root child count: {accRoot.accChildCount}");
try
{
    var sp = (IServiceProvider)accRoot;
    var iidIA2 = new Guid("E89F726E-C4F4-4C19-BB19-B647D7FA8478");
    var qs = sp.QueryService(ref iidAccessible, ref iidIA2, out var ia2);
    Console.WriteLine($"QueryService(IAccessible2): hr=0x{qs:X8}");
    if (ia2 != IntPtr.Zero) Marshal.Release(ia2);
}
catch (InvalidCastException)
{
    Console.WriteLine("Root does not implement IServiceProvider.");
}
try
{
    var first = accRoot.accNavigate(0x7 /* NAVDIR_FIRSTCHILD */, 0);
    Console.WriteLine($"First child: {(first is IAccessible fc ? RoleName(Role(fc, 0)) + " children=" + fc.accChildCount : first?.ToString() ?? "null")}");
}
catch (COMException ex)
{
    Console.WriteLine($"accNavigate failed: 0x{ex.HResult:X8}");
}

// Chromium hit-tests asynchronously too: the first call answers from cache and queues the precise test.
// Retry until the leaf is narrower than the document (i.e. we're inside real content) or time runs out.
sw.Restart();
IAccessible leaf = accRoot;
var leafChild = 0;
for (var attempt = 0; attempt < 3; attempt++)
{
    var (l, c, hops) = Descend(accRoot, cursor.X, cursor.Y);
    var r = Location(l, c);
    Console.WriteLine($"  hit-test attempt {attempt + 1} @{sw.ElapsedMilliseconds} ms: {hops} hops, {RoleName(Role(l, c))} {Fmt(r)} children={SafeChildCount(l)}");
    leaf = l;
    leafChild = c;
    if (r.right - r.left < 1500)
        break;
    Thread.Sleep(150);
}
Console.WriteLine($"MSAA accHitTest settled in {sw.ElapsedMilliseconds} ms");

// Fallback / cross-check: descend by rectangle containment through accChild, printing the path.
if (leafChild == 0)
{
    Thread.Sleep(700); // give the renderer time to serialize the full tree
    Console.WriteLine("Body children:");
    for (var i = 1; i <= SafeChildCount(leaf); i++)
    {
        try
        {
            if (leaf.get_accChild(i) is IAccessible bc)
                Console.WriteLine($"    {RoleName(Role(bc, 0)),-12} {Fmt(Location(bc, 0))} children={SafeChildCount(bc)} \"{Name(bc, 0)}\"");
        }
        catch (COMException) { }
    }
    sw.Restart();
    var visited = 0;
    var cur = leaf;
    for (var depth = 0; depth < 40; depth++)
    {
        IAccessible? next = null;
        var nextChild = 0;
        var count = SafeChildCount(cur);
        for (var i = 1; i <= count; i++)
        {
            object? childObj;
            try { childObj = cur.get_accChild(i); } catch (COMException) { continue; }
            visited++;
            if (childObj is IAccessible ca)
            {
                RECT cr;
                try { cr = Location(ca, 0); } catch (COMException) { continue; }
                if (cursor.X >= cr.left && cursor.X < cr.right && cursor.Y >= cr.top && cursor.Y < cr.bottom)
                {
                    next = ca;
                    break;
                }
            }
            else if (childObj is int simpleId)
            {
                RECT cr;
                try { cr = Location(cur, simpleId); } catch (COMException) { continue; }
                if (cursor.X >= cr.left && cursor.X < cr.right && cursor.Y >= cr.top && cursor.Y < cr.bottom)
                {
                    nextChild = simpleId;
                    break;
                }
            }
        }

        if (next is null)
        {
            leafChild = nextChild;
            break;
        }

        cur = next;
        Console.WriteLine($"  rect-descend depth {depth + 1}: {RoleName(Role(cur, 0)),-12} {Fmt(Location(cur, 0))} children={SafeChildCount(cur)}");
    }
    leaf = cur;
    Console.WriteLine($"Rect descent visited {visited} nodes in {sw.ElapsedMilliseconds} ms");
}

// Walk ancestors.
var chain = new List<(IAccessible Acc, int Child)>();
{
    var cur = leaf;
    var child = leafChild;
    for (var guard = 0; guard < 40 && cur is not null; guard++)
    {
        chain.Add((cur, child));
        child = 0;
        try { cur = cur.accParent as IAccessible; } catch (COMException) { break; }
    }
}

Console.WriteLine("MSAA ancestor chain (leaf first):");
RECT? docRect = null;
var rects = new List<RECT>();
foreach (var (acc, child) in chain)
{
    var r = Location(acc, child);
    rects.Add(r);
    var role = Role(acc, child);
    var name = Name(acc, child);
    if (name.Length > 50) name = name[..50] + "…";
    Console.WriteLine($"  {RoleName(role),-12} {Fmt(r)}  \"{name}\"");
    if (docRect is null && role == 15 /* ROLE_SYSTEM_DOCUMENT */)
        docRect = r;
}

// ---------- 1b. UIA for comparison (may work now that accessibility is on) ----------
try
{
    var uia = new CUIAutomation();
    var el = uia.ElementFromPoint(new tagPOINT { x = cursor.X, y = cursor.Y });
    var walker = uia.ControlViewWalker;
    var uiaDoc = false;
    for (var e = el; e is not null; e = walker.GetParentElement(e))
    {
        if (e.CurrentControlType == UIA_ControlTypeIds.UIA_DocumentControlTypeId) { uiaDoc = true; break; }
    }
    Console.WriteLine($"UIA ElementFromPoint now sees web content: {uiaDoc}");
    for (var e = el; e is not null; e = walker.GetParentElement(e))
    {
        var r = e.CurrentBoundingRectangle;
        var n = e.CurrentName ?? "";
        if (n.Length > 40) n = n[..40] + "…";
        Console.WriteLine($"  UIA {e.CurrentLocalizedControlType,-12} [{r.left},{r.top} {r.right - r.left}x{r.bottom - r.top}] \"{n}\"");
        if (e.CurrentControlType == UIA_ControlTypeIds.UIA_DocumentControlTypeId) break;
    }
}
catch (COMException ex)
{
    Console.WriteLine($"UIA failed: 0x{ex.HResult:X8}");
}

if (docRect is null)
{
    Console.WriteLine("No DOCUMENT ancestor found via MSAA. Stop.");
    return 1;
}

var viewport = docRect.Value;
var viewportWidth = viewport.right - viewport.left;
Console.WriteLine($"Viewport (document): {Fmt(viewport)}");

// Block heuristic: innermost ancestor between 200 px and 90% of the viewport wide, at least 16 px tall.
var blockIndex = -1;
for (var i = 0; i < chain.Count; i++)
{
    var r = rects[i];
    var w = r.right - r.left;
    var h = r.bottom - r.top;
    if (Role(chain[i].Acc, chain[i].Child) == 15) break;
    if (w >= 200 && w <= viewportWidth * 0.9 && h >= 16)
    {
        blockIndex = i;
        break;
    }
}

if (blockIndex < 0)
{
    Console.WriteLine("No suitable block found. Stop.");
    return 1;
}

var blockRect = rects[blockIndex];
var blockWidth = blockRect.right - blockRect.left;
var scale = forcedScale ?? Math.Clamp((double)viewportWidth / (blockWidth + 32), 1.1, 3.0);
var centerX = (blockRect.left + blockRect.right) / 2;
var centerY = Math.Clamp(cursor.Y, blockRect.top, blockRect.bottom);
Console.WriteLine($"Block: {RoleName(Role(chain[blockIndex].Acc, chain[blockIndex].Child))} {Fmt(blockRect)} -> scale {scale:F2}, pinch center ({centerX}, {centerY})");

if (!pinch)
    return 0;

// ---------- 2. Synthetic pinch ----------
if (!PInvoke.InitializeTouchInjection(2, TOUCH_FEEDBACK_MODE.TOUCH_FEEDBACK_NONE))
{
    Console.WriteLine($"InitializeTouchInjection failed: {Marshal.GetLastWin32Error()}");
    return 2;
}

PInvoke.SetForegroundWindow(root);
Thread.Sleep(200);

using var before = Capture(viewport);
Console.WriteLine("Pinching in...");
sw.Restart();
Pinch(centerX, centerY, 1.0, scale, frames: 12);
Console.WriteLine($"  done in {sw.ElapsedMilliseconds} ms");

Thread.Sleep(1500);
var shotDir = Environment.GetEnvironmentVariable("SPIKE_SHOTS");
if (shotDir is not null) before.Save(Path.Combine(shotDir, "1-before.png"));
using (var after = Capture(viewport))
{
    Console.WriteLine($"Screen pixels changed in viewport after zoom: {Diff(before, after):P1}");
    if (shotDir is not null) after.Save(Path.Combine(shotDir, "2-zoomed.png"));
}
Thread.Sleep(2500);

// Re-measure: did the block grow by the requested factor?
try
{
    var after = Location(chain[blockIndex].Acc, chain[blockIndex].Child);
    Console.WriteLine($"Block after zoom: {Fmt(after)} (width ratio {(after.right - after.left) / (double)blockWidth:F2}, expected {scale:F2})");
}
catch (COMException ex)
{
    Console.WriteLine($"Re-measure failed: 0x{ex.HResult:X8}");
}

Console.WriteLine("Pinching out (restore)...");
Pinch(centerX, centerY, scale, 1.0 / scale * 0.9 /* overshoot: browsers clamp at 1.0 */, frames: 12);
Thread.Sleep(600);
try
{
    var restored = Location(chain[blockIndex].Acc, chain[blockIndex].Child);
    Console.WriteLine($"Block after restore: {Fmt(restored)} (original {Fmt(blockRect)})");
}
catch (COMException ex)
{
    Console.WriteLine($"Re-measure failed: 0x{ex.HResult:X8}");
}
using (var restoredShot = Capture(viewport))
{
    Console.WriteLine($"Screen pixels differing before vs restored: {Diff(before, restoredShot):P1}");
    if (shotDir is not null) restoredShot.Save(Path.Combine(shotDir, "3-restored.png"));
}
Console.WriteLine("Done.");
return 0;

// Two contacts start on a horizontal line through the center and end 'ratio' times further apart.
static void Pinch(int cx, int cy, double startScale, double ratio, int frames)
{
    const int baseGap = 120; // half-distance between fingers at scale 1.0
    var startHalf = baseGap * startScale;
    var endHalf = startHalf * ratio;

    var contacts = new POINTER_TOUCH_INFO[2];
    for (uint i = 0; i < 2; i++)
    {
        contacts[i].pointerInfo.pointerType = POINTER_INPUT_TYPE.PT_TOUCH;
        contacts[i].pointerInfo.pointerId = i;
        contacts[i].touchFlags = 0; // TOUCH_FLAG_NONE
        contacts[i].touchMask = 0x1 | 0x2 | 0x4; // CONTACTAREA | ORIENTATION | PRESSURE
        contacts[i].orientation = 90;
        contacts[i].pressure = 32000;
    }

    void Place(double half)
    {
        for (var i = 0; i < 2; i++)
        {
            var x = (int)Math.Round(cx + (i == 0 ? -half : half));
            contacts[i].pointerInfo.ptPixelLocation = new System.Drawing.Point(x, cy);
            contacts[i].rcContact = new RECT { left = x - 2, top = cy - 2, right = x + 2, bottom = cy + 2 };
        }
    }

    void Inject(POINTER_FLAGS flags)
    {
        contacts[0].pointerInfo.pointerFlags = flags;
        contacts[1].pointerInfo.pointerFlags = flags;
        if (!PInvoke.InjectTouchInput(contacts))
            throw new InvalidOperationException($"InjectTouchInput failed: {Marshal.GetLastWin32Error()}");
    }

    Place(startHalf);
    Inject(POINTER_FLAGS.POINTER_FLAG_DOWN | POINTER_FLAGS.POINTER_FLAG_INRANGE | POINTER_FLAGS.POINTER_FLAG_INCONTACT);
    Thread.Sleep(16);

    for (var f = 1; f <= frames; f++)
    {
        var t = f / (double)frames;
        t = 1 - Math.Pow(1 - t, 3); // ease-out
        Place(startHalf + (endHalf - startHalf) * t);
        Inject(POINTER_FLAGS.POINTER_FLAG_UPDATE | POINTER_FLAGS.POINTER_FLAG_INRANGE | POINTER_FLAGS.POINTER_FLAG_INCONTACT);
        Thread.Sleep(16);
    }

    Inject(POINTER_FLAGS.POINTER_FLAG_UP);
}

static (IAccessible Leaf, int Child, int Hops) Descend(IAccessible from, int x, int y)
{
    var cur = from;
    var hops = 0;
    for (var guard = 0; guard < 64; guard++)
    {
        object? hit;
        try { hit = cur.accHitTest(x, y); } catch (COMException) { break; }
        if (hit is IAccessible deeper && !ReferenceEquals(deeper, cur))
        {
            cur = deeper;
            hops++;
            continue;
        }
        return (cur, hit is int id ? id : 0, hops);
    }
    return (cur, 0, hops);
}

static HWND FindDescendant(HWND parent, string className)
{
    var found = HWND.Null;
    PInvoke.EnumChildWindows(parent, (child, _) =>
    {
        if (ClassName(child) == className)
        {
            found = child;
            return false;
        }
        return true;
    }, default);
    return found;
}

static unsafe string ClassName(HWND window)
{
    Span<char> buffer = stackalloc char[257];
    var length = PInvoke.GetClassName(window, buffer);
    return length > 0 ? new string(buffer[..length]) : string.Empty;
}

static RECT Location(IAccessible acc, int child)
{
    acc.accLocation(out var left, out var top, out var width, out var height, child);
    return new RECT { left = left, top = top, right = left + width, bottom = top + height };
}

static int Role(IAccessible acc, int child)
{
    try { return acc.get_accRole(child) is int r ? r : -1; } catch (COMException) { return -1; }
}

static string Name(IAccessible acc, int child)
{
    try { return acc.get_accName(child) ?? ""; } catch (COMException) { return ""; }
}

static string Fmt(RECT r) => $"[{r.left},{r.top} {r.right - r.left}x{r.bottom - r.top}]";

static string RoleName(int role) => role switch
{
    10 => "Client", 15 => "Document", 16 => "Pane", 20 => "Grouping", 24 => "Table", 27 => "Cell",
    30 => "Link", 33 => "List", 34 => "ListItem", 40 => "Graphic", 41 => "StaticText", 42 => "Text",
    43 => "PushButton", _ => role.ToString(CultureInfo.InvariantCulture),
};

static System.Drawing.Bitmap Capture(RECT r)
{
    var bmp = new System.Drawing.Bitmap(r.right - r.left, r.bottom - r.top);
    using var g = System.Drawing.Graphics.FromImage(bmp);
    g.CopyFromScreen(r.left, r.top, 0, 0, bmp.Size);
    return bmp;
}

static double Diff(System.Drawing.Bitmap a, System.Drawing.Bitmap b)
{
    long changed = 0, total = 0;
    for (var y = 0; y < a.Height; y += 4)
    for (var x = 0; x < a.Width; x += 4)
    {
        total++;
        if (a.GetPixel(x, y) != b.GetPixel(x, y)) changed++;
    }
    return total == 0 ? 0 : changed / (double)total;
}

static int SafeChildCount(IAccessible acc)
{
    try { return acc.accChildCount; } catch (COMException) { return -1; }
}

[DllImport("oleacc.dll")]
static extern int AccessibleObjectFromWindow(HWND hwnd, uint id, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IAccessible? ppvObject);

[ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IServiceProvider
{
    [PreserveSig]
    int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
}
