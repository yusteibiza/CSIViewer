using System.Runtime.InteropServices;
using CSIViewer.Media;
using CSIViewer.Protocol;
using CSIViewer.Services;

internal static class ViewerInputFixture
{
    // Exercises the real encrypted remote-input route, but only inside this
    // disposable diagnostic form. Never invoke against an installed endpoint.
    public static async Task ExerciseAsync(Form form, RemoteClient client, Action<string, bool> check)
    {
        var monitor = MonitorService.GetMonitors()[0];
        var previousCursor = Cursor.Position;
        var previousWindow = GetForegroundWindow();
        var clicks = 0;
        using var button = new Button { Text = "Diagnostic click target", Bounds = new Rectangle(25, 25, 260, 40) };
        using var text = new TextBox { Bounds = new Rectangle(25, 85, 350, 35) };
        using var drag = new Panel { Bounds = new Rectangle(25, 155, 180, 60), BackColor = Color.DarkCyan };
        var dragging = false;
        var origin = Point.Empty;
        button.Click += (_, _) => clicks++;
        drag.MouseDown += (_, e) => { dragging = true; origin = e.Location; drag.Capture = true; };
        drag.MouseMove += (_, e) =>
        {
            if (dragging) drag.Location = new Point(drag.Left + e.X - origin.X, drag.Top + e.Y - origin.Y);
        };
        drag.MouseUp += (_, _) => { dragging = false; drag.Capture = false; };
        form.Controls.AddRange([button, text, drag]);
        form.Text = "CSIViewer isolated remote-input test";
        form.ShowInTaskbar = true;
        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = new Rectangle(monitor.X + 40, monitor.Y + 40, 540, 380);
        form.TopMost = true;
        form.Show();
        form.Activate();
        SetForegroundWindow(form.Handle);
        await Task.Delay(400);
        var down = false;

        async Task SendMouseAsync(string action, Point point)
        {
            if (GetForegroundWindow() != form.Handle)
                throw new InvalidOperationException("Refusing remote input: diagnostic form lost foreground");
            if (!form.Bounds.Contains(point))
                throw new InvalidOperationException("Refusing remote input outside diagnostic form");
            if (GetAncestor(WindowFromPoint(point), 2) != form.Handle)
                throw new InvalidOperationException("Refusing remote input: another window covers the diagnostic target");
            var x = Math.Clamp((int)Math.Round((point.X - monitor.X) * 65535.0 / Math.Max(1, monitor.Width - 1)), 0, 65535);
            var y = Math.Clamp((int)Math.Round((point.Y - monitor.Y) * 65535.0 / Math.Max(1, monitor.Height - 1)), 0, 65535);
            await client.SendInputAsync(new InputEventPayload("mouse", action, x, y, 0, 0, 0, 0, action == "leftDown"));
            await Task.Delay(120);
        }

        async Task ClickAsync(Control control)
        {
            var point = control.PointToScreen(new Point(control.Width / 2, control.Height / 2));
            down = true;
            await SendMouseAsync("leftDown", point);
            await SendMouseAsync("leftUp", point);
            down = false;
        }

        try
        {
            await ClickAsync(button);
            await WaitAsync(() => clicks == 1);
            check("receiving-viewer-applies-mouse-click", clicks == 1);
            await ClickAsync(text);
            check("remote-click-focuses-diagnostic-textbox", text.Focused);
            if (GetForegroundWindow() != form.Handle || !text.Focused)
                throw new InvalidOperationException("Refusing text input outside diagnostic textbox");
            await client.SendInputAsync(new InputEventPayload("text", "QA-remote-input-only", 0, 0, 0, 0, 0, 0, false));
            await WaitAsync(() => text.Text == "QA-remote-input-only");
            check("receiving-viewer-applies-keyboard-text", text.Text == "QA-remote-input-only");
            var initial = drag.Location;
            var start = drag.PointToScreen(new Point(20, 20));
            down = true;
            await SendMouseAsync("leftDown", start);
            for (var i = 1; i <= 4; i++)
                await SendMouseAsync("move", new Point(start.X + 15 * i, start.Y + 5 * i));
            await SendMouseAsync("leftUp", new Point(start.X + 60, start.Y + 20));
            down = false;
            await WaitAsync(() => !dragging && drag.Left >= initial.X + 45);
            check("receiving-viewer-applies-mouse-drag", drag.Left >= initial.X + 45 && drag.Top >= initial.Y + 10);
        }
        finally
        {
            if (down && GetForegroundWindow() == form.Handle)
            {
                try { await SendMouseAsync("leftUp", form.PointToScreen(new Point(30, 30))); }
                catch { /* RemoteClient releases pressed input when the session ends. */ }
            }
            form.TopMost = false;
            if (GetForegroundWindow() == form.Handle)
            {
                Cursor.Position = previousCursor;
                SetForegroundWindow(previousWindow);
            }
            form.Hide();
            form.Controls.Clear();
        }
    }

    private static async Task WaitAsync(Func<bool> ready)
    {
        for (var i = 0; i < 100 && !ready(); i++) await Task.Delay(50);
        if (!ready()) throw new TimeoutException("Remote input did not reach diagnostic control");
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);
}
