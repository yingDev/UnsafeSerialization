using System;
using System.Collections.Generic;
using YingDev.UnsafeSerialization;
using static YingDev.UnsafeSerialization.Readers;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using YingDev.UnsafeSerialization.Utils;
using System.Linq;
using System.Threading;
using System.Runtime;
using static YingDev.UnsafeSerialization.Writers;


namespace Tests
{
    class ConsoleLogger : ILOGGER
    {
        public void WriteLine(string msg)
        {
            Console.WriteLine(msg);
        }
    }

    public class UnitTest1
    {
        public static void Main(string[] args)
        {
            Start(new ConsoleLogger());
        }

        public static unsafe void Start(ILOGGER log)
        {
            LOGGER.impl = log;

            var defReaders = new Dictionary<Type, Delegate>
            {
                {typeof(int), new StructReader(I32Reader)},
                {typeof(bool), new StructReader(ByteReader)},
                {typeof(double), new StructReader(F64Reader)},
                {typeof(string), new ObjectReader(CStringReader)},
            };
            var defWriters = new Dictionary<Type, Delegate>
            {
                {typeof(int), new StructWriter(I32Writer)},
                {typeof(bool), new StructWriter(ByteWriter)},
                {typeof(double), new StructWriter(F64Writer)},
                {typeof(string), new ObjectWriter(CStringWriter)},
            };
            LayoutInfo.Add<Point>(defReaders, defWriters);
            LayoutInfo.Add<Inner>(defReaders, defWriters);
            LayoutInfo.Add<MyStruct>(defReaders, defWriters);
            LayoutInfo.Add<X>(defReaders, defWriters);

            var src = new X
            {
                serverId = 888,
                a = 123,
                pt = new Point { x = 1, y = 2 },
                str = "hello world",
                value = 789,
                isOk = true,
                inner = new MyStruct { A = 222, inner = new Inner(1) { name = "holy shit" }, strr = "hello world" },
                f64 = 123.0,
                pts = new[] { new Point { x = 1990, y = 2 }, new Point { x = 3, y = 4 }, new Point { x = 5, y = 6 }, new Point { x = 5, y = 6 } },
                pt2 = 630
            };

            var data = new byte[1024];

            var N = 1000 * 1000;

            fixed (byte* dataPtr = &data[0])
            {
                var fastBuf = new UnsafeBuffer { p = dataPtr };
                /*new Thread(s =>
                {
                    while (true)
                    {
                        Thread.Sleep(100);
                        Console.Write("*");
                        GC.Collect(2, GCCollectionMode.Forced, false, true);
                    }

                }).Start();*/

                for(var i=0; i<1; i++)
                {
                    fastBuf._wPos = fastBuf._rPos = 0;

                    Console.WriteLine("RunWriter");
                    DoGC();
                    var writerTime = RunWriter(fastBuf, src, N);
                    var result = (X)MessageReader(fastBuf, typeof(X));
                    log.WriteLine(result.ToString());

                    Console.WriteLine("RunReadDirect");
                    DoGC();
                    var directTime = RunReadDirect(fastBuf, N);

                    Console.WriteLine("RunReader");
                    DoGC();
                    var readerTime = RunReader(fastBuf, N);


                    Console.WriteLine("RunReadReflect");
                    DoGC();
                    var reflectTime = RunReadReflect(fastBuf, N);


                    log.WriteLine($"\nDIRECT: {directTime}\nREADER: {readerTime}\nWRITER: {writerTime}\nREFLECT: {reflectTime}\n\n");
                }
                
                Console.Read();
            }

        }

        static void DoGC()
        {
            GC.Collect(2, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
            Thread.Sleep(1000);
        }

        static long RunWriter(UnsafeBuffer buf, X obj, int N)
        {
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < N; i++)
            {
                buf._wPos = 0;
                MessageWriter(buf, obj);
            }
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        static long RunReader(UnsafeBuffer buf, int N)
        {
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < N; i++)
            {
                buf._rPos = 0;
                MessageReader(buf, typeof(X));

            }
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        static long RunReadDirect(UnsafeBuffer buf, int N)
        {
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < N; i++)
            {
                buf._rPos = 0;
                var x = (X)Activator.CreateInstance(typeof(X));

                x.a = i;
                x.f64 = 123.0;
                x.inner = new MyStruct { A = 5, inner = new Inner(1) { name = new string(new char[] { 'h', 'l', 'l', 'o', 'w', 'o', 'l', 'd', 'x' }) }};
                x.str = new string(new char[] { 'h', 'l', 'l', 'o', 'w', 'o', 'l', 'd', 'x' });
                x.haha = "";
                x.alert = null;
                x.numMap = new Dictionary<int, int> { { 112, 222 } };
                x.isOk = i % 2 == 0;
                x.pts = new Point[]
                    {new Point {x = 1, y = 2}, new Point {x = 3, y = 4}, new Point {x = 5, y = 6}};
                x.pt = new Point { x = 1, y = 1990 };
                x.serverId = 888;
                x.value = i;
                x.pt2 = 222;

            }
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        static long RunReadReflect(UnsafeBuffer buf, int N)
        {
            var t = typeof(X);

            var fa = t.GetField("a");
            var ff64 = t.GetField("f64");
            var finner = t.GetField("inner");
            var fstr = t.GetField("str");
            var fnumMap = t.GetField("numMap");
            var fhaha = t.GetField("haha");
            var falert = t.GetField("alert");
            var fisOk = t.GetField("isOk");
            var fpts = t.GetField("pts");
            var fpt = t.GetField("pt");
            var fserverId = t.GetField("serverId");
            var fvalue = t.GetField("value");
            var fpt2 = t.GetField("pt2");

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < N; i++)
            {
                buf._rPos = 0;
                var x = (X)Activator.CreateInstance(typeof(X));

                fa.SetValue(x, buf.ReadByte());
                ff64.SetValue(x, buf.ReadByte());
                finner.SetValue(x, new MyStruct { A = 5, inner = new Inner(2) { name = new string(new char[] { 'h', 'l', 'l', 'o', 'w', 'o', 'l', 'd', 'x' }) }});
                fstr.SetValue(x, new string(new char[] { 'h', 'l', 'l', 'o', 'w', 'o', 'l', 'd', 'x' }));
                fnumMap.SetValue(x, new Dictionary<int, int> { { 112, 222 } });
                fisOk.SetValue(x, Convert.ToBoolean(buf.ReadByte()));
                falert.SetValue(x, null);
                fhaha.SetValue(x, "haha");
                fpts.SetValue(x,
                    new Point[]
                    {
                        new Point {x = buf.ReadByte(), y = buf.ReadByte()},
                        new Point {x = buf.ReadByte(), y = buf.ReadByte()},
                        new Point {x = buf.ReadByte(), y = buf.ReadByte()}
                    });
                fpt.SetValue(x, new Point { x = buf.ReadByte(), y = buf.ReadByte() });
                fserverId.SetValue(x, buf.ReadByte());
                fvalue.SetValue(x, buf.ReadByte());
                fpt2.SetValue(x, buf.ReadByte());
            }
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
public abstract class BaseX
{
    public int serverId;

    public object alert;
    static ObjectWriter alertWriter = (w, o) => { };// skip
    static ObjectReader alertReader = (r, o) => null;
}

[UnsafeSerialize, StructLayout(LayoutKind.Sequential)]
public class X : BaseX
{
    object hoho;
    static ObjectWriter hohoWriter = (r, o) => { }; //skip

    public int a;
    public Point pt;
    public string str;
    public int value;

    public bool isOk;
    public double f64;
   
    public Point[] pts;
    static ObjectReader ptsReader = BlittableValueArrayReader<Point>((r, o) => new Point[r.ReadByte()]);
    static ObjectWriter ptsWriter = ValueArrayWriter<Point>((w, o) => w.WriteByte((byte)((Point[])o).Length), LayoutWriter<Point>());

    public object numMap;
    static ObjectReader numMapReader = (r, o) =>  new Dictionary<int, int> { { 112, 222 } };
    static ObjectWriter numMapWriter = (r, o) => { };//skip

    public string haha;
    static ObjectWriter hahaWriter = (w, o) => { };//skip
    static ObjectReader hahaReader = CondReader<X>(o => true, (r, o) => "shit");

    public MyStruct inner;

    static StructReader pt2Reader = CondReader<X>(o => true, I32Reader);
    public int pt2;

    public override string ToString() => this.ToStringUsingLayoutInfo();

    /*~X()
    {
        Console.WriteLine("~X");
    }*/
}

[UnsafeSerialize, StructLayout(LayoutKind.Sequential)]
public struct MyStruct
{
    public int A;
    public Inner inner;

    public string strr;
    static ObjectWriter strrWriter = (r, o) => { };//skip
    static ObjectReader strrReader = (r, o) => "strrrrrrrr";

    public override string ToString()
    {
        return this.ToStringUsingLayoutInfo();
    }
}

[UnsafeSerialize, StructLayout(LayoutKind.Sequential)]
public class Inner
{
    public string name;
    double _dum1;
    double _dum2;

    public Inner(int a)
    {

    }

    public void Test()
    {
        Console.WriteLine("Inner.Test: " + name);
    }

    public override string ToString()
    {
        return "Inner: " + name;
    }

    /*~Inner()
    {
        Console.Write("~Inner");
    }*/
}

[UnsafeSerialize, StructLayout(LayoutKind.Sequential)]
public struct Point
{
    public int x;
    public int y;

    public override string ToString()
    {
        return string.Format("Point({0}, {1})", x, y);
    }
}