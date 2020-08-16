using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using Gma.System.MouseKeyHook;
using Loamen.KeyMouseHook;
using Loamen.KeyMouseHook.Native;
using System.Runtime.Caching;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;


namespace 按键转换
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary
    /// 
    public partial class MainWindow : Window
    {
        struct KeyRecord
        {
            public DateTime time;
            public char key;

            public KeyRecord(char key)
            {
                this.key = key;
                this.time = DateTime.Now;
            }
        }

        private IKeyboardMouseEvents m_GlobalHook;
        private InputSimulator sim = new InputSimulator();
        private int count = 0;
        ObjectCache KeyDownCache = CreateFastMemoryCache("KeyDown");
        ObjectCache KeyUpCache = CreateFastMemoryCache("KeyUp");
        private bool isSim = false;
        Object simLocker = new Object();


        private const int WaitTime = 350; // 记录超过此时间就小时
        private const int AutoStartTime = 500; // 按下超过此时间即使未放开也自动替换

        // private const int MaxLength = 16;
        //
        // List<KeyRecord> keys = new List<KeyRecord>(MaxLength);

        public MainWindow()
        {
            InitializeComponent();
            m_GlobalHook = Hook.GlobalEvents();
            m_GlobalHook.KeyDown += GlobalHookKeyDown;
            m_GlobalHook.KeyUp += GlobalHookKeyUp;
        }

        private static MemoryCache CreateFastMemoryCache(string name) {
            MemoryCache instance = null;
            Assembly assembly = typeof(CacheItemPolicy).Assembly;
            Type type = assembly.GetType("System.Runtime.Caching.CacheExpires");
            if (type != null) {
                FieldInfo field = type.GetField("_tsPerBucket", BindingFlags.Static | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(TimeSpan)) {
                    TimeSpan originalValue = (TimeSpan)field.GetValue(null);
                    Console.WriteLine(originalValue);
                    field.SetValue(null, TimeSpan.FromMilliseconds(10));
                    instance = new MemoryCache(name);
                    field.SetValue(null, originalValue); // reset to original value
                }
            }
            return instance ?? new MemoryCache(name);
        }

        public void ReplaceKeyWithShift(int to, uint backCount = 1)
        {
            Task.Run(() =>
            {
                Console.WriteLine("sim ");
                

                Thread.Sleep(1);
                lock (simLocker)
                {
                    isSim = true;
                    for (var i = 0; i < backCount; i++) {
                        sim.Keyboard.KeyPress(VirtualKeyCode.BACK);
                    }

                    sim.Keyboard.KeyDown(VirtualKeyCode.SHIFT);
                    sim.Keyboard.KeyPress((VirtualKeyCode)to);
                    sim.Keyboard.KeyUp(VirtualKeyCode.SHIFT);
                    isSim = false;
                }
                

            });
        }

        public IEnumerable<string> FindKeyInCache(ObjectCache theCache, Keys key)
        {
            return (
                from item in theCache
                where item.Key.StartsWith(key.ToString())
                select item.Key
            );
        }

        private void GlobalHookKeyUp(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (e.KeyValue < '0' || (e.KeyValue >= 160 && e.KeyValue <= 164)) 
                return;
            
            if (isSim)
                return;

            var watch = new Stopwatch();

            watch.Start();

            var l = FindKeyInCache(
                KeyDownCache, e.KeyCode
            ).ToList();
            if (l.Count > 0)
            {
                var policy = new CacheItemPolicy
                {
                    AbsoluteExpiration = DateTimeOffset.Now.AddMilliseconds(AutoStartTime),
                    RemovedCallback = arguments => {
                        Console.WriteLine($"up {DateTime.Now.Ticks} {arguments.CacheItem.Key} removed {GetInv(arguments.CacheItem.Key)} ms");

                    }
                };
                var key = $"{e.KeyCode}|{DateTime.Now.Ticks}";
                Console.WriteLine($"{key} up added");
                KeyUpCache.Set(key, e.KeyValue, policy);

                l.Sort();
                l.ForEach(x => Console.Write($"{x} "));
                Console.WriteLine();

                foreach (var x in l.Take(l.Count - 1))
                {
                    KeyDownCache.Remove(x);
                    Console.WriteLine($"手动删除前面的 {x}");
                }
            }
            else
            {
                ReplaceKeyWithShift(e.KeyValue);
            }

            watch.Stop();
            Console.WriteLine(
                $"GlobalHookKeyUp: ticks: {watch.ElapsedTicks}|" +
                $"{watch.ElapsedMilliseconds}|" +
                $"{DateTime.Now.Millisecond} "
            ); 
            // Console.WriteLine($"e.KeyValue: {e.KeyValue} {e.KeyCode} {e.KeyData}");

            // l.ForEach(x=>Console.Write($"{x.Substring(0, 4)} "));
            // Console.WriteLine();
        }

        public double GetInv(string key)
        {
            return (DateTime.Now.Ticks - long.Parse(key.Split('|')[1])) / 10000.0;
        }
        private void GlobalHookKeyDown(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (e.KeyValue < '0' || (e.KeyValue >= 160 && e.KeyValue <= 164))
                return;

            if (isSim)
                return;


            count++;

            var watch = new Stopwatch();
            watch.Start();

            
            var policy = new CacheItemPolicy
            {
                AbsoluteExpiration = DateTimeOffset.Now.AddMilliseconds(WaitTime),
                RemovedCallback = async arguments =>
                {
                    if (arguments.RemovedReason == CacheEntryRemovedReason.Removed)
                    {
                        Console.WriteLine($"{DateTime.Now.Ticks} {arguments.CacheItem.Key} 手动删除 {GetInv(arguments.CacheItem.Key)} ms");
                        return;
                    }

                    var delta = GetInv(arguments.CacheItem.Key);
                    if (delta > WaitTime * 1.1)
                    {
                        Console.WriteLine($"{DateTime.Now.Ticks} {arguments.CacheItem.Key} 超时 {delta} ms");
                        return;
                    }
                    Console.WriteLine($"{DateTime.Now.Ticks} {arguments.CacheItem.Key} start wait {GetInv(arguments.CacheItem.Key)} ms");

                    await Task.Delay(AutoStartTime - WaitTime);
                    Console.WriteLine($"{DateTime.Now.Ticks} {arguments.CacheItem.Key} will be removed {GetInv(arguments.CacheItem.Key)} ms");

                    var keysList = FindKeyInCache(
                        KeyUpCache, e.KeyCode
                    ).ToList();
                    
                    if (keysList.Count == 0)
                    {
                        ReplaceKeyWithShift(e.KeyValue);
                    }
                    
                    Console.WriteLine($"down {DateTime.Now.Ticks} {arguments.CacheItem.Key} removed {GetInv(arguments.CacheItem.Key)} ms");
                }
            };
            var key = $"{e.KeyCode}|{DateTime.Now.Ticks}";
            Console.WriteLine($"{key} down added");
            KeyDownCache.Set(key, e.KeyValue, policy);

            watch.Stop();

            Console.WriteLine(
                $"GlobalHookKeyDown: ticks: {watch.ElapsedTicks}|" +
                $"{watch.ElapsedMilliseconds}|" +
                $"{DateTime.Now.Millisecond} "
            );
            // var l = KeyDownCache.Select(x => x.Key).ToList();
            // l.Sort();
            // Console.WriteLine($"e.KeyValue: {e.KeyValue} {e.KeyCode} {e.KeyData}");
            // KeyUpCache.ToList().ForEach(x => Console.Write($@"{x.Key} "));
            // Console.WriteLine("KeyDownUpCache: {0}, {1}", KeyDownCache.ToList().Count, KeyUpCache.ToList().Count);
        }

        [DllImport("User32.dll")]
        public static extern int SystemParametersInfo(
            uint uiAction,
            uint uiParam,
            IntPtr pvParam,
            uint fWinIni
        );


        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine($"{SystemParameters.KeyboardDelay}, {SystemParameters.KeyboardSpeed}");
            
            const uint SPI_SETKEYBOARDDELAY = 0x17;
            const uint SPI_SETKEYBOARDSPEED = 0x0b;

            SystemParametersInfo(SPI_SETKEYBOARDDELAY, 3, IntPtr.Zero, 0);
            SystemParametersInfo(SPI_SETKEYBOARDSPEED, 0, IntPtr.Zero, 0);

        }
    }
}