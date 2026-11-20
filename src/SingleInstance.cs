// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace Prisma
{
    // One Prisma window per Windows user. A second launch (double-clicking another video)
    // hands its files to the open window through a named pipe that only this user can open,
    // and exits. The receiving window treats them like files chosen in the Open dialog.
    internal sealed class SingleInstance : IDisposable
    {
        private const int MaxMessage = 256 * 1024;
        private readonly Mutex mutex;
        private readonly string pipeName;
        private Thread listener;
        private volatile bool stopping;
        public bool IsFirst { get; private set; }
        public event Action<string[]> FilesReceived;

        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);

        public SingleInstance()
        {
            string user = WindowsIdentity.GetCurrent().User.Value;
            pipeName = "Prisma.Player.Open." + user;
            bool created;
            mutex = new Mutex(true, @"Local\Prisma.Player." + user, out created);
            IsFirst = created;
        }

        public void Listen()
        {
            listener = new Thread(Loop) { IsBackground = true, Name = "Prisma: abrir desde otra instancia" };
            listener.Start();
        }

        private void Loop()
        {
            PipeSecurity security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
            while (!stopping)
            {
                try
                {
                    using (NamedPipeServerStream pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None, 0, MaxMessage, security))
                    {
                        pipe.WaitForConnection();
                        if (stopping) return;
                        byte[] buffer = new byte[MaxMessage];
                        int total = 0, read;
                        while (total < MaxMessage && (read = pipe.Read(buffer, total, MaxMessage - total)) > 0) total += read;
                        string[] files = Decode(Encoding.UTF8.GetString(buffer, 0, total));
                        if (FilesReceived != null) FilesReceived(files);
                    }
                }
                catch { if (!stopping) Thread.Sleep(200); }
            }
        }

        internal static string[] Decode(string message)
        {
            List<string> files = new List<string>();
            foreach (string line in message.Split('\n'))
            {
                string path = line.Trim('\r');
                if (path.Length > 0 && path.Length < 32768 && files.Count < 500) files.Add(path);
            }
            return files.ToArray();
        }

        // Sends the files to the first instance. Returns false if it could not be reached.
        public bool Send(string[] files)
        {
            List<string> full = new List<string>();
            foreach (string f in files)
            {
                try { full.Add(Path.GetFullPath(f)); } catch { }
            }
            byte[] data = Encoding.UTF8.GetBytes(string.Join("\n", full.ToArray()));
            if (data.Length > MaxMessage) return false;
            AllowSetForegroundWindow(-1);
            for (int attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
                    {
                        pipe.Connect(200);
                        pipe.Write(data, 0, data.Length); pipe.Flush();
                        return true;
                    }
                }
                catch { Thread.Sleep(100); }
            }
            return false;
        }

        public void Dispose()
        {
            stopping = true;
            try { using (NamedPipeClientStream wake = new NamedPipeClientStream(".", pipeName, PipeDirection.Out)) wake.Connect(100); } catch { }
            try { if (IsFirst) mutex.ReleaseMutex(); } catch { }
            mutex.Dispose();
        }
    }
}
