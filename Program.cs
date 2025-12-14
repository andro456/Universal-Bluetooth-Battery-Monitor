using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BluetoothBatteryMonitor
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // This MUTEX part ensures you can't accidentally open the app twice.
            using (System.Threading.Mutex mutex = new System.Threading.Mutex(false, "Global\\" + "MyUniversalBatteryApp"))
            {
                if (!mutex.WaitOne(0, false))
                {
                    // App is already running!
                    return;
                }
                Application.Run(new TrayAppContext());
            }
        }
    }

    public class TrayAppContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private System.Windows.Forms.Timer updateTimer;
        private IntPtr currentIconHandle = IntPtr.Zero;

        // Import the function to clean up memory (prevents crashes after long use)
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        extern static bool DestroyIcon(IntPtr handle);

        public TrayAppContext()
        {
            trayIcon = new NotifyIcon()
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "Starting..."
            };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Refresh", null, (s, e) => _ = UpdateBatteryAsync());
            menu.Items.Add("-");
            menu.Items.Add("Exit", null, Exit);
            trayIcon.ContextMenuStrip = menu;

            _ = UpdateBatteryAsync();

            updateTimer = new System.Windows.Forms.Timer();
            updateTimer.Interval = 300000; // Check every 5 Minutes
            updateTimer.Tick += (sender, e) => _ = UpdateBatteryAsync();
            updateTimer.Start();
        }

        private async Task UpdateBatteryAsync()
        {
            // Run the PowerShell check in the background
            int level = await Task.Run(() => GetUniversalBatteryLevel());

            if (level >= 0)
            {
                // FOUND ONE!
                trayIcon.Text = $"Battery: {level}%";
                UpdateDynamicIcon(level);
            }
            else
            {
                // NO DEVICE FOUND
                trayIcon.Text = "No Bluetooth Audio Connected";
                UpdateDynamicIcon(-1); // Draws the gray empty icon
            }
        }

        private void UpdateDynamicIcon(int level)
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // --- DISCONNECTED STATE (Gray) ---
                if (level == -1)
                {
                    using (Pen grayPen = new Pen(Color.Gray, 2))
                    {
                        g.DrawRectangle(grayPen, 4, 7, 24, 18); // Outline
                        g.DrawLine(grayPen, 4, 7, 28, 25);       // Cross line
                    }

                    SetIcon(bitmap);
                    return;
                }

                // --- CONNECTED STATE ---
                Color batteryColor = Color.LimeGreen;
                if (level <= 50) batteryColor = Color.Orange;
                if (level <= 20) batteryColor = Color.Red;

                // 1. Draw the "Nub" (White tip)
                g.FillRectangle(Brushes.White, 28, 12, 2, 8);

                // 2. Draw Outline (White)
                using (Pen whitePen = new Pen(Color.White, 2))
                    g.DrawRectangle(whitePen, 4, 7, 24, 18);

                // 3. Draw Fill Level
                // Math: Convert percentage to pixel width (Max width is 20px)
                int fillWidth = (int)((level / 100.0) * 20);
                if (fillWidth < 1) fillWidth = 1;

                using (SolidBrush fillBrush = new SolidBrush(batteryColor))
                    g.FillRectangle(fillBrush, 6, 9, fillWidth, 14);

                SetIcon(bitmap);
            }
        }

        private void SetIcon(Bitmap bitmap)
        {
            // Clean up the old icon from memory
            if (currentIconHandle != IntPtr.Zero) DestroyIcon(currentIconHandle);

            // Set the new one
            currentIconHandle = bitmap.GetHicon();
            trayIcon.Icon = Icon.FromHandle(currentIconHandle);
        }

        private int GetUniversalBatteryLevel()
        {
            try
            {
                // --- THE UNIVERSAL SEARCH COMMAND ---
                // 1. Look for ANY device where the ID contains 'BTH' (Bluetooth Hardware)
                // 2. Must be Status 'OK' (Connected)
                // 3. We loop through them and check for the Battery Property
                // 4. If found, we stop and return that number immediately.

                string psCommand = "$devs = Get-PnpDevice | Where-Object { $_.InstanceId -like '*BTH*' -and $_.Status -eq 'OK' }; foreach ($d in $devs) { $prop = Get-PnpDeviceProperty -InstanceId $d.InstanceId -KeyName '{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2' -ErrorAction SilentlyContinue; if ($prop.Data -match '^[0-9]+$') { Write-Output $prop.Data; break; } }";

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "powershell.exe";
                psi.Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"";
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;

                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();

                    if (!string.IsNullOrEmpty(output))
                    {
                        // Clean the result (take the first number found)
                        string cleanNum = output.Split(new char[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                        if (int.TryParse(cleanNum, out int result))
                        {
                            return result;
                        }
                    }
                }
            }
            catch { }

            return -1; // -1 means "Nothing found"
        }

        private void Exit(object? sender, EventArgs e)
        {
            trayIcon.Visible = false;
            if (currentIconHandle != IntPtr.Zero) DestroyIcon(currentIconHandle);
            Application.Exit();
        }
    }
}